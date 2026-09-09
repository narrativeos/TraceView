// Copyright (c) 2025 BobLd
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using System.Collections.Generic;
using System.Linq;
using Caly.Core.Models;

namespace Caly.Core.Services;

/// <summary>
/// Collects NLP analysis units from the Popo tree
/// (entity-extraction-quality-spec §13.4 / §14.2 B2).
///
/// The Popo tree is the single traceability trunk. The collector walks the
/// tree in document order and dispatches each node by the granularity matrix
/// (type × size):
/// <list type="bullet">
/// <item>text &lt;30 chars → merged into the previous unit (context for fragments)</item>
/// <item>text 30–2000 chars → 1 unit per node</item>
/// <item>text ≥2000 chars → packed at segment boundaries (never splits a paragraph)</item>
/// <item>table_data / table_key_value → structured serialization (TableUnitSerializer)</item>
/// <item>caption / image_footnote → 1 high-value unit</item>
/// <item>title → skipped (covered by the batch TOC unit)</item>
/// <item>empty content + leaf → smooth degradation to per-block units</item>
/// </list>
///
/// Traceability tiers (spec §13.8.1, verified on real data):
/// <list type="bullet">
/// <item>Tier 2 direct: delta = segments − source_block_ids == 0 → segment i ↔ block i</item>
/// <item>Tier 2 offset: delta == −1 and the first block is a MinerU "title"
/// block → segment i ↔ block i+1 (the title block's text is not in the content)</item>
/// <item>Tier 1 node-level: any other delta → the unit carries the node's full
/// block arrays (always safe)</item>
/// </list>
///
/// Aggregation nodes (empty content + children) are skipped: their block
/// arrays overlap the children's, and the children's content already covers
/// those blocks (verified on GB50150: 909/910 "uncovered" blocks of such
/// nodes appear in a descendant's content).
///
/// Pure unit: no I/O, no UI dependencies.
/// </summary>
public static class NlpUnitCollector
{
    /// <summary>Text length below which a text node is merged into the previous unit.</summary>
    public const int MinTextLength = 30;

    /// <summary>Text length above which a text node is split at segment boundaries.</summary>
    public const int MaxUnitLength = 2000;

    /// <summary>Segment separator used by the Popo API in node content.</summary>
    public const string SegmentSeparator = "<|txt_split|>";

    /// <summary>
    /// Collects NLP units from the document in document order, followed by the
    /// batch TOC unit(s). Returns an empty list when the tree is unavailable.
    /// </summary>
    /// <param name="doc">The Popo structure document (tree + TOC).</param>
    /// <param name="blockMap">
    /// MinerU block lookup by block_id (UUID) — used for the title-label tier
    /// check and for smooth degradation content. May be empty.
    /// </param>
    public static List<NlpUnit> Collect(
        StructureDocument? doc,
        IReadOnlyDictionary<string, MinerUBlock> blockMap)
    {
        var output = new List<NlpUnit>();
        if (doc?.TreeRoot == null)
            return output;

        var blockSectionPaths = SectionPathResolver.Resolve(doc).BlockSectionPaths;

        Traverse(doc.TreeRoot, blockSectionPaths, blockMap, output);
        CollectTocUnits(doc, output);

        return output;
    }

    // ── Tree walk ────────────────────────────────────────────────────────────

    private static void Traverse(
        AnalysisTreeNode node,
        Dictionary<string, string> blockSectionPaths,
        IReadOnlyDictionary<string, MinerUBlock> blockMap,
        List<NlpUnit> output)
    {
        var sectionPath = GetSectionPath(node, blockSectionPaths);

        switch (node.Type)
        {
            case "title":
                // Titles are covered by the batch TOC unit (spec decision #17).
                break;

            case "text":
                CollectTextUnits(node, sectionPath, blockMap, output);
                break;

            case "table_data":
            case "table_key_value":
                CollectTableUnit(node, sectionPath, blockMap, output);
                break;

            case "caption":
            case "image_footnote":
                CollectCaptionUnit(node, sectionPath, output);
                break;

            default:
                // root / image / other — not NLP units.
                break;
        }

        foreach (var child in node.Children)
            Traverse(child, blockSectionPaths, blockMap, output);
    }

    /// <summary>
    /// The section path recorded on any of the node's source blocks by
    /// SectionPathResolver (the resolver writes the node's own path onto all
    /// of its SourceBlockIds).
    /// </summary>
    private static string? GetSectionPath(AnalysisTreeNode node, Dictionary<string, string> blockSectionPaths)
    {
        foreach (var sbid in node.SourceBlockIds)
        {
            if (!string.IsNullOrEmpty(sbid) && blockSectionPaths.TryGetValue(sbid, out var path))
                return path;
        }
        return null;
    }

    // ── text nodes ───────────────────────────────────────────────────────────

    private static void CollectTextUnits(
        AnalysisTreeNode node,
        string? sectionPath,
        IReadOnlyDictionary<string, MinerUBlock> blockMap,
        List<NlpUnit> output)
    {
        var content = node.Content;
        if (string.IsNullOrWhiteSpace(content))
        {
            // Aggregation nodes (empty content + children) are skipped: the
            // children cover their content, and degrading here would duplicate
            // it (see class docs). Only leaf nodes degrade to block units.
            if (node.Children.Count == 0)
                DegradeToBlockUnits(node, sectionPath, "text", blockMap, output);
            return;
        }

        var segments = content.Split(SegmentSeparator);
        var tier = DetermineTier(segments.Length, node, blockMap);
        var cleanFull = SemanticAnalysisService.SanitizeTextForNlp(content);

        if (cleanFull.Length < MinTextLength)
        {
            MergeShortText(output, node, sectionPath, cleanFull, tier, blockMap);
            return;
        }

        if (cleanFull.Length <= MaxUnitLength)
        {
            output.Add(BuildTextNodeUnit(node, sectionPath, cleanFull, null, tier, blockMap));
            return;
        }

        // ≥ MaxUnitLength: pack at segment boundaries (never split a paragraph).
        // Every chunk carries the same section prefix.
        foreach (var (start, end) in PackSegments(segments, MaxUnitLength))
        {
            var chunkRaw = string.Join(SegmentSeparator, segments[start..(end + 1)]);
            var chunkClean = SemanticAnalysisService.SanitizeTextForNlp(chunkRaw);
            output.Add(BuildTextNodeUnit(node, sectionPath, chunkClean, new[] { start, end }, tier, blockMap));
        }
    }

    /// <summary>
    /// Merges a short text fragment into the previous unit (spec §13.4:
    /// "&lt;30 字并入相邻单元"). When there is no previous unit the fragment
    /// is emitted standalone.
    /// </summary>
    private static void MergeShortText(
        List<NlpUnit> output,
        AnalysisTreeNode node,
        string? sectionPath,
        string cleanText,
        TraceTier tier,
        IReadOnlyDictionary<string, MinerUBlock> blockMap)
    {
        if (output.Count == 0)
        {
            output.Add(BuildTextNodeUnit(node, sectionPath, cleanText, null, tier, blockMap));
            return;
        }

        var last = output[^1];
        last.Text += "\n" + cleanText;

        // Union traceability (order-preserving).
        foreach (var sbid in node.SourceBlockIds)
            if (!last.SourceBlockIds.Contains(sbid))
                last.SourceBlockIds.Add(sbid);
        foreach (var bid in node.BlockIds)
            if (!last.BlockIds.Contains(bid))
                last.BlockIds.Add(bid);
        if (last.OriginNodeId != node.NodeId)
            last.Locations.AddRange(node.Location);

        // Mixed provenance: the segment range no longer describes the unit.
        if (last.OriginNodeId != node.NodeId)
            last.SegmentRange = null;
    }

    private static NlpUnit BuildTextNodeUnit(
        AnalysisTreeNode node,
        string? sectionPath,
        string cleanText,
        int[]? segmentRange,
        TraceTier tier,
        IReadOnlyDictionary<string, MinerUBlock> blockMap)
    {
        var (text, prefixLen) = SemanticAnalysisService.ApplySectionPathPrefix(cleanText, sectionPath);
        var unit = new NlpUnit
        {
            Text = text,
            PrefixLen = prefixLen,
            UnitType = "text",
            OriginNodeId = node.NodeId,
            SegmentRange = segmentRange,
            SectionPath = sectionPath,
        };
        ApplyTraceability(unit, node, segmentRange, tier);
        return unit;
    }

    // ── table nodes ──────────────────────────────────────────────────────────

    private static void CollectTableUnit(
        AnalysisTreeNode node,
        string? sectionPath,
        IReadOnlyDictionary<string, MinerUBlock> blockMap,
        List<NlpUnit> output)
    {
        var unitType = node.Type == "table_data" ? "table_data" : "table_kv";
        var text = TableUnitSerializer.Serialize(node);

        if (string.IsNullOrWhiteSpace(text))
        {
            // No structured data — degrade to the raw block content (the
            // flattened table text), same as the legacy path.
            DegradeToBlockUnits(node, sectionPath, unitType, blockMap, output);
            return;
        }

        var cleanText = SemanticAnalysisService.SanitizeTextForNlp(text);
        var (prefixed, prefixLen) = SemanticAnalysisService.ApplySectionPathPrefix(cleanText, sectionPath);
        var unit = new NlpUnit
        {
            Text = prefixed,
            PrefixLen = prefixLen,
            UnitType = unitType,
            OriginNodeId = node.NodeId,
            SectionPath = sectionPath,
        };
        ApplyTraceability(unit, node, null, TraceTier.Node);
        output.Add(unit);
    }

    // ── caption / image_footnote nodes ───────────────────────────────────────

    private static void CollectCaptionUnit(AnalysisTreeNode node, string? sectionPath, List<NlpUnit> output)
    {
        // image_footnote content may live in Metadata rather than Content.
        var content = !string.IsNullOrWhiteSpace(node.Content) ? node.Content : node.Metadata;
        if (string.IsNullOrWhiteSpace(content))
            return;

        var cleanText = SemanticAnalysisService.SanitizeTextForNlp(content);
        if (cleanText.Length <= 1)
            return;

        var (prefixed, prefixLen) = SemanticAnalysisService.ApplySectionPathPrefix(cleanText, sectionPath);
        var unit = new NlpUnit
        {
            Text = prefixed,
            PrefixLen = prefixLen,
            UnitType = "caption",
            OriginNodeId = node.NodeId,
            SectionPath = sectionPath,
        };
        ApplyTraceability(unit, node, null, TraceTier.Node);
        output.Add(unit);
    }

    // ── smooth degradation (spec §13.4 last row) ─────────────────────────────

    /// <summary>
    /// Degrades a node without usable content to one unit per source block,
    /// taking the block content from the MinerU block map (block-level
    /// traceability). Title and image blocks are skipped — titles are covered
    /// by the TOC unit, images carry no text.
    /// </summary>
    private static void DegradeToBlockUnits(
        AnalysisTreeNode node,
        string? sectionPath,
        string unitType,
        IReadOnlyDictionary<string, MinerUBlock> blockMap,
        List<NlpUnit> output)
    {
        foreach (var sbid in node.SourceBlockIds)
        {
            if (string.IsNullOrEmpty(sbid) || !blockMap.TryGetValue(sbid, out var block))
                continue;
            if (block.Type == "title" || block.Type == "image")
                continue;
            if (string.IsNullOrWhiteSpace(block.Content))
                continue;

            var cleanText = SemanticAnalysisService.SanitizeTextForNlp(block.Content);
            if (cleanText.Length <= 1)
                continue;

            var (prefixed, prefixLen) = SemanticAnalysisService.ApplySectionPathPrefix(cleanText, sectionPath);
            output.Add(new NlpUnit
            {
                Text = prefixed,
                PrefixLen = prefixLen,
                UnitType = unitType,
                OriginNodeId = node.NodeId,
                SourceBlockIds = { sbid },
                BlockIds = { block.Id },
                Locations = { new LocationEntry { Bbox = block.Bbox, Page = block.Page } },
                SectionPath = sectionPath,
            });
        }
    }

    // ── batch TOC unit (spec §13.5 用法二) ───────────────────────────────────

    private static void CollectTocUnits(StructureDocument doc, List<NlpUnit> output)
    {
        var entries = doc.TocEntries
            .Where(e => !string.IsNullOrWhiteSpace(e.Title))
            .ToList();
        if (entries.Count == 0)
            return;

        foreach (var (text, parentPath) in SplitToc(entries))
        {
            output.Add(new NlpUnit
            {
                Text = text,
                PrefixLen = 0,
                UnitType = "toc",
                SectionPath = parentPath,
            });
        }
    }

    /// <summary>
    /// Joins TOC entries with level indentation into one or more units.
    /// When the full TOC exceeds <see cref="MaxUnitLength"/> it is split by
    /// chapter; oversized chapters are split by subsection and each chunk
    /// carries the chapter title as its parent path (spec §13.5).
    /// Returns (sanitized text, parent path or null).
    /// </summary>
    private static List<(string Text, string? ParentPath)> SplitToc(List<TocEntry> entries)
    {
        var result = new List<(string, string?)>();
        var minLevel = entries.Min(e => e.Level);

        var full = string.Join("\n", entries.Select(e => FormatTocLine(e, minLevel)));
        if (full.Length <= MaxUnitLength)
        {
            result.Add((SemanticAnalysisService.SanitizeTextForNlp(full), null));
            return result;
        }

        // Split by top-level chapters.
        var groups = new List<List<TocEntry>>();
        var current = new List<TocEntry>();
        foreach (var entry in entries)
        {
            if (entry.Level <= minLevel && current.Count > 0)
            {
                groups.Add(current);
                current = new List<TocEntry>();
            }
            current.Add(entry);
        }
        if (current.Count > 0)
            groups.Add(current);

        foreach (var group in groups)
        {
            var head = group[0];
            var text = string.Join("\n", group.Select(e => FormatTocLine(e, minLevel)));
            if (text.Length <= MaxUnitLength)
            {
                result.Add((SemanticAnalysisService.SanitizeTextForNlp(text), null));
                continue;
            }

            // Oversized chapter: split by subsections; each chunk carries the
            // chapter title line as its parent path.
            var children = group.Skip(1).ToList();
            if (children.Count > 0)
            {
                var headLine = FormatTocLine(head, minLevel);
                foreach (var (subText, _) in SplitToc(children))
                    result.Add((SemanticAnalysisService.SanitizeTextForNlp(headLine + "\n" + subText), head.Title.Trim()));
            }
            else
            {
                // A single entry larger than the limit — emit as-is (soft limit).
                result.Add((SemanticAnalysisService.SanitizeTextForNlp(text), null));
            }
        }

        return result;
    }

    private static string FormatTocLine(TocEntry entry, int minLevel)
    {
        var indent = new string(' ', Math.Max(0, entry.Level - minLevel) * 2);
        return indent + entry.Title.Trim();
    }

    // ── traceability tiers (spec §13.8.1) ────────────────────────────────────

    private enum TraceTier
    {
        /// <summary>Node-level: the unit carries the node's full block arrays.</summary>
        Node,
        /// <summary>Segment i ↔ block i (delta == 0).</summary>
        SegmentDirect,
        /// <summary>Segment i ↔ block i+1 (delta == −1, first block is a title).</summary>
        SegmentOffset,
    }

    private static TraceTier DetermineTier(
        int segmentCount,
        AnalysisTreeNode node,
        IReadOnlyDictionary<string, MinerUBlock> blockMap)
    {
        var sbids = node.SourceBlockIds;
        if (sbids.Count == 0)
            return TraceTier.Node;

        var delta = segmentCount - sbids.Count;
        if (delta == 0)
            return TraceTier.SegmentDirect;
        if (delta == -1 &&
            !string.IsNullOrEmpty(sbids[0]) &&
            blockMap.TryGetValue(sbids[0], out var first) &&
            first.Type == "title")
            return TraceTier.SegmentOffset;

        return TraceTier.Node;
    }

    /// <summary>
    /// Fills the unit's SourceBlockIds / BlockIds / Locations from the node's
    /// parallel traceability arrays, narrowed to the segment range when a
    /// segment-level tier applies.
    /// </summary>
    private static void ApplyTraceability(
        NlpUnit unit,
        AnalysisTreeNode node,
        int[]? segmentRange,
        TraceTier tier)
    {
        var sbids = node.SourceBlockIds;
        if (sbids.Count == 0)
            return;

        // A SegmentOffset node's first block is a title whose content is not
        // part of the unit text — exclude it even for a single (unpacked) unit.
        int start = tier == TraceTier.SegmentOffset ? 1 : 0;
        int end = sbids.Count - 1;
        if (tier != TraceTier.Node && segmentRange is { Length: >= 2 })
        {
            var offset = tier == TraceTier.SegmentOffset ? 1 : 0;
            start = segmentRange[0] + offset;
            end = segmentRange[1] + offset;
            start = Math.Max(0, Math.Min(start, sbids.Count - 1));
            end = Math.Max(start, Math.Min(end, sbids.Count - 1));
        }

        for (int i = start; i <= end; i++)
        {
            unit.SourceBlockIds.Add(sbids[i]);
            if (i < node.BlockIds.Count)
                unit.BlockIds.Add(node.BlockIds[i]);
            if (i < node.Location.Count)
                unit.Locations.Add(node.Location[i]);
        }
    }

    // ── segment packing ──────────────────────────────────────────────────────

    /// <summary>
    /// Greedily packs consecutive segments into chunks of at most
    /// <paramref name="maxLen"/> characters (separator included). A single
    /// segment longer than the limit becomes its own chunk — paragraphs are
    /// never split mid-segment.
    /// </summary>
    private static IEnumerable<(int Start, int End)> PackSegments(string[] segments, int maxLen)
    {
        int start = 0;
        int len = 0;
        for (int i = 0; i < segments.Length; i++)
        {
            var segLen = segments[i].Length;
            if (i > start && len + SegmentSeparator.Length + segLen > maxLen)
            {
                yield return (start, i - 1);
                start = i;
                len = 0;
            }
            len += (i > start ? SegmentSeparator.Length : 0) + segLen;
        }
        if (start < segments.Length)
            yield return (start, segments.Length - 1);
    }
}
