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
/// Resolves the section path (章节路径) for each MinerU block from the Popo tree
/// and the document TOC. Pure unit: no I/O, no UI dependencies.
///
/// Two products (entity-extraction-quality-spec §14.1 A1):
/// 1. <see cref="ResolutionResult.BlockSectionPaths"/> — source_block_id (UUID) →
///    section path like "第5章 总则 &gt; 5.13 绝缘电阻试验", built by walking the
///    Popo tree with an ancestor-title stack.
/// 2. <see cref="ResolutionResult.SectionAnchors"/> — numbering → TOC entry
///    (SECTION_REF anchor table for "见 5.13"-style references).
///
/// Blocks not covered by the tree (discarded blocks) fall back to a page-based
/// TOC match: TOC entries are sorted by page and a level stack is maintained,
/// so each page maps to the section path active at that page.
/// </summary>
public static class SectionPathResolver
{
    /// <summary>Separator used when joining ancestor titles into a section path.</summary>
    public const string Separator = " > ";

    /// <summary>Max characters taken from a title node's content when its Title is empty.</summary>
    private const int FallbackTitleLength = 30;

    /// <summary>
    /// Products of <see cref="Resolve"/>.
    /// </summary>
    public sealed class ResolutionResult
    {
        /// <summary>source_block_id (UUID) → section path. Tree-derived entries win over page fallback.</summary>
        public Dictionary<string, string> BlockSectionPaths { get; } = new(StringComparer.Ordinal);

        /// <summary>numbering → TOC entry (first occurrence wins). SECTION_REF anchor table.</summary>
        public Dictionary<string, TocEntry> SectionAnchors { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>
    /// Resolves section paths for all blocks of the document.
    /// Returns an empty result when the document is null.
    /// </summary>
    public static ResolutionResult Resolve(StructureDocument? doc)
    {
        var result = new ResolutionResult();
        if (doc == null)
            return result;

        // SECTION_REF anchor table: numbering → entry (first occurrence wins).
        foreach (var entry in doc.TocEntries)
        {
            if (string.IsNullOrWhiteSpace(entry.Numbering))
                continue;
            result.SectionAnchors.TryAdd(entry.Numbering.Trim(), entry);
        }

        // Tree walk: ancestor-title stack → per-block section paths.
        if (doc.TreeRoot != null)
        {
            var stack = new List<string>();
            Traverse(doc.TreeRoot, stack, result);
        }

        // Page fallback for blocks not covered by the tree (discarded blocks).
        // TryAdd keeps tree-derived paths intact.
        if (doc.TocEntries.Count > 0)
        {
            foreach (var blocks in doc.PagesBlocks.Values)
            {
                if (blocks == null || blocks.Count == 0)
                    continue;
                var page = blocks[0].Page;
                var path = PagePathAt(doc.TocEntries, page);
                if (string.IsNullOrEmpty(path))
                    continue;
                foreach (var block in blocks)
                {
                    if (!string.IsNullOrEmpty(block.BlockId))
                        result.BlockSectionPaths.TryAdd(block.BlockId, path);
                }
            }
        }

        return result;
    }

    private static void Traverse(AnalysisTreeNode node, List<string> stack, ResolutionResult result)
    {
        // Section headings extend the section path. The Popo tree represents
        // headings either as dedicated "title" nodes or (in the current API
        // format) as "text" nodes carrying a non-empty Title field — the same
        // convention used by PopoJsonService.BuildTocFromSubtree.
        bool pushed = false;
        if (node.Type == "title" || (node.Type == "text" && !string.IsNullOrWhiteSpace(node.Title)))
        {
            var label = !string.IsNullOrWhiteSpace(node.Title)
                ? node.Title.Trim()
                : Truncate(node.Content, FallbackTitleLength);
            if (!string.IsNullOrEmpty(label))
            {
                stack.Add(label);
                pushed = true;
            }
        }

        var path = stack.Count > 0 ? string.Join(Separator, stack) : null;
        if (path != null)
        {
            foreach (var blockId in node.SourceBlockIds)
            {
                if (!string.IsNullOrEmpty(blockId))
                    result.BlockSectionPaths.TryAdd(blockId, path);
            }
        }

        foreach (var child in node.Children)
            Traverse(child, stack, result);

        if (pushed)
            stack.RemoveAt(stack.Count - 1);
    }

    /// <summary>
    /// Page fallback: sorts TOC entries by page, maintains a level stack, and
    /// returns the section path active at the given page (null when no TOC
    /// entry starts at or before that page).
    /// </summary>
    public static string? PagePathAt(IReadOnlyList<TocEntry> tocEntries, int page)
    {
        if (tocEntries == null || tocEntries.Count == 0)
            return null;

        var stack = new List<(int Level, string Title)>();
        foreach (var entry in tocEntries
                     .Where(e => e.Page > 0 && e.Page <= page)
                     .OrderBy(e => e.Page)
                     .ThenBy(e => e.Level))
        {
            while (stack.Count > 0 && stack[^1].Level >= entry.Level)
                stack.RemoveAt(stack.Count - 1);
            stack.Add((entry.Level, entry.Title));
        }

        return stack.Count > 0 ? string.Join(Separator, stack.Select(s => s.Title)) : null;
    }

    private static string Truncate(string text, int maxLength)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
