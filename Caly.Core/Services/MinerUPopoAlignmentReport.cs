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

using Caly.Core.Models;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Caly.Core.Services;

/// <summary>
/// Represents a single MinerU block entry in the alignment report.
/// </summary>
public sealed class MinerUBlockReportEntry
{
    /// <summary>
    /// Block ID (UUID string) from MinerU.
    /// </summary>
    public string BlockId { get; set; } = string.Empty;

    /// <summary>
    /// Block type (text, image, title, caption, etc.).
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Block source: "para" (adopted), "discarded" (rejected), or empty.
    /// </summary>
    public string BlockSource { get; set; } = string.Empty;

    /// <summary>
    /// Page number this block belongs to.
    /// </summary>
    public int Page { get; set; }

    /// <summary>
    /// Content preview (truncated).
    /// </summary>
    public string ContentPreview { get; set; } = string.Empty;

    /// <summary>
    /// Source block IDs (inherited parent block_ids).
    /// </summary>
    public List<string> SourceBlockIds { get; set; } = new();

    /// <summary>
    /// Whether this block was matched to a Popo node.
    /// </summary>
    public bool IsMatched { get; set; }

    /// <summary>
    /// The matched Popo node type (if matched).
    /// </summary>
    public string? MatchedPopoNodeType { get; set; }

    /// <summary>
    /// The matched Popo node title (if matched).
    /// </summary>
    public string? MatchedPopoNodeTitle { get; set; }

    /// <summary>
    /// How the match was found: "BlockId" or "SourceBlockIds".
    /// </summary>
    public string? MatchMethod { get; set; }
}

/// <summary>
/// Represents a Popo node entry in the alignment report.
/// </summary>
public sealed class PopoNodeReportEntry
{
    /// <summary>
    /// Node type (text, image, title, etc.).
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Node title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Source block IDs referenced by this node.
    /// </summary>
    public List<string> SourceBlockIds { get; set; } = new();

    /// <summary>
    /// Block IDs (integer IDs from MinerU).
    /// </summary>
    public List<int> BlockIds { get; set; } = new();

    /// <summary>
    /// Number of MinerU blocks matched to this node.
    /// </summary>
    public int MatchedBlockCount { get; set; }

    /// <summary>
    /// Whether this node has at least one matched MinerU block.
    /// </summary>
    public bool HasMatchedBlocks => MatchedBlockCount > 0;

    /// <summary>
    /// Content preview (truncated).
    /// </summary>
    public string ContentPreview { get; set; } = string.Empty;
}

/// <summary>
/// Complete alignment report comparing MinerU blocks and Popo nodes.
/// </summary>
public sealed class MinerUPopoAlignmentReport
{
    /// <summary>
    /// Document ID.
    /// </summary>
    public string DocId { get; set; } = string.Empty;

    /// <summary>
    /// Total MinerU block count.
    /// </summary>
    public int TotalMinerUBlocks { get; set; }

    /// <summary>
    /// Total Popo node count (excluding root).
    /// </summary>
    public int TotalPopoNodes { get; set; }

    /// <summary>
    /// Number of MinerU blocks matched to Popo nodes.
    /// </summary>
    public int MatchedMinerUBlocks { get; set; }

    /// <summary>
    /// Number of MinerU blocks NOT matched to any Popo node.
    /// </summary>
    public int UnmatchedMinerUBlocks { get; set; }

    /// <summary>
    /// Number of Popo nodes with at least one matched MinerU block.
    /// </summary>
    public int MatchedPopoNodes { get; set; }

    /// <summary>
    /// Number of Popo nodes with NO matched MinerU blocks.
    /// </summary>
    public int UnmatchedPopoNodes { get; set; }

    /// <summary>
    /// All MinerU block entries.
    /// </summary>
    public List<MinerUBlockReportEntry> MinerUBlocks { get; set; } = new();

    /// <summary>
    /// All Popo node entries.
    /// </summary>
    public List<PopoNodeReportEntry> PopoNodes { get; set; } = new();

    /// <summary>
    /// MinerU blocks that were NOT matched to any Popo node.
    /// </summary>
    public List<MinerUBlockReportEntry> UnmatchedMinerUBlocksList =>
        MinerUBlocks.Where(b => !b.IsMatched).ToList();

    /// <summary>
    /// Popo nodes that have NO matched MinerU blocks.
    /// </summary>
    public List<PopoNodeReportEntry> UnmatchedPopoNodesList =>
        PopoNodes.Where(n => !n.HasMatchedBlocks).ToList();

    /// <summary>
    /// Generates a text summary of the alignment report.
    /// </summary>
    public string GenerateSummary()
    {
        var lines = new List<string>();
        lines.Add("=== MinerU-Popo Block Alignment Report ===");
        lines.Add($"Document: {DocId}");
        lines.Add("");
        lines.Add("--- Overview ---");
        lines.Add($"Total MinerU Blocks: {TotalMinerUBlocks}");
        lines.Add($"Total Popo Nodes: {TotalPopoNodes}");
        lines.Add($"Matched MinerU Blocks: {MatchedMinerUBlocks}");
        lines.Add($"Unmatched MinerU Blocks: {UnmatchedMinerUBlocks}");
        lines.Add($"Matched Popo Nodes: {MatchedPopoNodes}");
        lines.Add($"Unmatched Popo Nodes: {UnmatchedPopoNodes}");
        lines.Add("");

        if (UnmatchedMinerUBlocks > 0)
        {
            lines.Add("--- Unmatched MinerU Blocks ---");
            foreach (var block in UnmatchedMinerUBlocksList)
            {
                lines.Add($"  [{block.Type}] Page {block.Page} Source={block.BlockSource} " +
                    $"BlockId={block.BlockId} Content=\"{block.ContentPreview}\"");
            }
            lines.Add("");
        }

        if (UnmatchedPopoNodes > 0)
        {
            lines.Add("--- Unmatched Popo Nodes ---");
            foreach (var node in UnmatchedPopoNodesList)
            {
                lines.Add($"  [{node.Type}] Title=\"{node.Title}\" " +
                    $"SourceBlockIds=[{string.Join(",", node.SourceBlockIds)}] " +
                    $"BlockIds=[{string.Join(",", node.BlockIds)}]");
            }
            lines.Add("");
        }

        return string.Join("\n", lines);
    }
}

/// <summary>
/// Service for generating alignment reports between MinerU blocks and Popo nodes.
/// </summary>
public static class MinerUPopoAlignmentReportService
{
    /// <summary>
    /// Generates an alignment report comparing MinerU blocks and Popo tree nodes.
    /// </summary>
    /// <param name="doc">The structure document containing both MinerU blocks and Popo tree.</param>
    /// <returns>A complete alignment report.</returns>
    public static MinerUPopoAlignmentReport GenerateReport(StructureDocument doc)
    {
        var report = new MinerUPopoAlignmentReport
        {
            DocId = doc.DocId
        };

        // Get all MinerU blocks
        var allMinerUBlocks = doc.GetAllBlocks();
        report.TotalMinerUBlocks = allMinerUBlocks.Count;

        // Get all Popo nodes (flattened, excluding root)
        var popoNodes = new List<AnalysisTreeNode>();
        if (doc.TreeRoot is not null)
        {
            FlattenTreeNodes(doc.TreeRoot.Children, popoNodes);
        }
        report.TotalPopoNodes = popoNodes.Count;

        // Build a map: BlockId (UUID) -> Popo node
        var blockIdToPopoNode = new Dictionary<string, AnalysisTreeNode>();
        foreach (var node in popoNodes)
        {
            foreach (var srcId in node.SourceBlockIds)
            {
                if (!string.IsNullOrEmpty(srcId) && !blockIdToPopoNode.ContainsKey(srcId))
                {
                    blockIdToPopoNode[srcId] = node;
                }
            }
        }

        // Build Popo node entries
        var popoEntries = new List<PopoNodeReportEntry>();
        var popoMatchedBlockIds = new HashSet<string>();

        foreach (var node in popoNodes)
        {
            var entry = new PopoNodeReportEntry
            {
                Type = node.Type,
                Title = node.Title,
                SourceBlockIds = new List<string>(node.SourceBlockIds),
                BlockIds = new List<int>(node.BlockIds),
                ContentPreview = Truncate(node.Content, 100)
            };
            popoEntries.Add(entry);
        }

        report.PopoNodes = popoEntries;

        // Match MinerU blocks to Popo nodes
        var matchedCount = 0;

        foreach (var block in allMinerUBlocks)
        {
            var entry = new MinerUBlockReportEntry
            {
                BlockId = block.BlockId,
                Type = block.Type,
                BlockSource = block.BlockSource,
                Page = block.Page,
                ContentPreview = Truncate(block.Content, 100),
                SourceBlockIds = new List<string>(block.SourceBlockIds)
            };

            // Try to match using BlockId
            AnalysisTreeNode? matchedNode = null;
            string? matchMethod = null;

            if (!string.IsNullOrEmpty(block.BlockId) && blockIdToPopoNode.TryGetValue(block.BlockId, out var node))
            {
                matchedNode = node;
                matchMethod = "BlockId";
            }
            else
            {
                // Try SourceBlockIds
                foreach (var srcId in block.SourceBlockIds)
                {
                    if (!string.IsNullOrEmpty(srcId) && blockIdToPopoNode.TryGetValue(srcId, out node))
                    {
                        matchedNode = node;
                        matchMethod = "SourceBlockIds";
                        break;
                    }
                }
            }

            if (matchedNode is not null)
            {
                entry.IsMatched = true;
                entry.MatchedPopoNodeType = matchedNode.Type;
                entry.MatchedPopoNodeTitle = matchedNode.Title;
                entry.MatchMethod = matchMethod;
                matchedCount++;

                // Track which MinerU block IDs matched this Popo node
                if (!string.IsNullOrEmpty(block.BlockId))
                    popoMatchedBlockIds.Add(block.BlockId);
                foreach (var srcId in block.SourceBlockIds)
                    if (!string.IsNullOrEmpty(srcId))
                        popoMatchedBlockIds.Add(srcId);
            }

            report.MinerUBlocks.Add(entry);
        }

        report.MatchedMinerUBlocks = matchedCount;
        report.UnmatchedMinerUBlocks = allMinerUBlocks.Count - matchedCount;

        // Calculate matched Popo nodes
        int matchedPopoCount = 0;
        foreach (var entry in popoEntries)
        {
            entry.MatchedBlockCount = 0;
            foreach (var srcId in entry.SourceBlockIds)
            {
                if (popoMatchedBlockIds.Contains(srcId))
                {
                    entry.MatchedBlockCount++;
                }
            }
            if (entry.HasMatchedBlocks)
                matchedPopoCount++;
        }

        report.MatchedPopoNodes = matchedPopoCount;
        report.UnmatchedPopoNodes = popoNodes.Count - matchedPopoCount;

        return report;
    }

    private static void FlattenTreeNodes(
        System.Collections.ObjectModel.ObservableCollection<AnalysisTreeNode> children,
        List<AnalysisTreeNode> result)
    {
        foreach (var child in children)
        {
            result.Add(child);
            FlattenTreeNodes(child.Children, result);
        }
    }

    private static string Truncate(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        return text.Length > maxLength ? text.Substring(0, maxLength) + "..." : text;
    }
}