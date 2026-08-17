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
using Caly.Core.Services;

namespace Caly.Core.Models;

/// <summary>
/// Builds a <see cref="TermResultFile"/> from enriched entities, term relations,
/// and the term hierarchy tree.
/// </summary>
public static class TermResultBuilder
{
    /// <summary>
    /// Builds the complete term discovery result.
    /// </summary>
    /// <param name="enrichedResult">Enriched result from the EntityProcessorPipeline.</param>
    /// <param name="semanticResult">Semantic result with term definitions.</param>
    /// <param name="outputDir">Output directory for path reference.</param>
    /// <param name="entityMetadata">Enrichment metadata keyed by (entityId, metadataKey).</param>
    public static TermResultFile Build(
        EnrichedResultFile enrichedResult,
        SemanticResultFile semanticResult,
        string outputDir,
        IReadOnlyDictionary<string, Dictionary<string, object>> entityMetadata)
    {
        var result = new TermResultFile
        {
            Version = "1.0",
            Timestamp = DateTime.UtcNow.ToString("O"),
            SourceSemanticResult = System.IO.Path.Combine(outputDir, "semantic_result.json")
        };

        // ── Collect all entities as term candidates ──
        var allEntities = new List<(string Text, string Category, string Normalized, List<int> BlockIds, int Frequency)>();
        var textToEntry = new Dictionary<string, (int Index, TermEntry Entry)>(StringComparer.Ordinal);
        var blockIdSets = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        var freqCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var block in enrichedResult.Blocks)
        {
            foreach (var entry in block.Entities)
            {
                if (entry.ValidationStatus == "invalid") continue;

                var text = IsCleanEnrichedText(entry.EnrichedText) ? entry.EnrichedText : entry.Text;

                freqCounts[text] = freqCounts.TryGetValue(text, out var f) ? f + 1 : 1;

                if (!blockIdSets.ContainsKey(text))
                    blockIdSets[text] = new HashSet<int>();
                foreach (var bid in block.BlockIds)
                    blockIdSets[text].Add(bid);
            }
        }

        // Build unique term entries
        foreach (var block in enrichedResult.Blocks)
        {
            foreach (var entry in block.Entities)
            {
                if (entry.ValidationStatus == "invalid") continue;

                var text = IsCleanEnrichedText(entry.EnrichedText) ? entry.EnrichedText : entry.Text;
                if (textToEntry.ContainsKey(text)) continue;

                var termEntry = new TermEntry
                {
                    Text = text,
                    OriginalText = entry.Text,
                    Category = entry.Category,
                    Frequency = freqCounts.TryGetValue(text, out var freq) ? freq : 1,
                    BlockCount = blockIdSets.TryGetValue(text, out var bids) ? bids.Count : 1,
                    BlockIds = blockIdSets.TryGetValue(text, out var bidList) ? bidList.ToList() : new List<int>()
                };

                // Extract definition from entity metadata
                if (entityMetadata.TryGetValue(text, out var meta))
                {
                    if (meta.TryGetValue("definition", out var defObj) && defObj is string def)
                        termEntry.Definition = def;
                    if (meta.TryGetValue("context", out var ctxObj) && ctxObj is string ctx)
                        termEntry.Context = ctx;
                }

                textToEntry[text] = (result.Terms.Count, termEntry);
                result.Terms.Add(termEntry);
            }
        }

        // ── Collect relations ──
        // From enriched result's block relations
        foreach (var block in enrichedResult.Blocks)
        {
            foreach (var rel in block.Relations)
            {
                result.Relations.Add(new TermOutputRelation
                {
                    Type = rel.RelationType,
                    Source = rel.SourceEntityId,
                    Target = rel.TargetEntityId,
                    Confidence = rel.Confidence,
                    Evidence = rel.Evidence,
                    BlockIds = block.BlockIds
                });
            }
        }

        // From cross-block relations
        foreach (var rel in enrichedResult.CrossBlockRelations)
        {
            result.Relations.Add(new TermOutputRelation
            {
                Type = rel.RelationType,
                Source = rel.SourceEntityId,
                Target = rel.TargetEntityId,
                Confidence = rel.Confidence,
                Evidence = rel.Evidence
            });
        }

        // ── Build scores ──
        var scorer = new TermScorer();
        // Collect all term entities for scoring
        var allTermEntities = new List<(SemanticEntity Entity, SemanticBlockResult Block)>();
        foreach (var block in semanticResult.Blocks)
        {
            foreach (var entity in block.Entities)
            {
                allTermEntities.Add((entity, block));
            }
        }

        var scores = scorer.ScoreTerms(allTermEntities, semanticResult.Blocks, entityMetadata);

        // Apply scores to term entries
        foreach (var term in result.Terms)
        {
            if (scores.TryGetValue(term.Text, out var score))
                term.Score = score;
            else if (scores.TryGetValue(term.OriginalText, out var origScore))
                term.Score = origScore;
            else
                term.Score = 0.1; // Default low score
        }

        // ── Build hierarchy tree ──
        var hypernymRelations = result.Relations
            .Where(r => r.Type == "HYPERNYM" || r.Type == "IS_A")
            .Select(r => (r.Source, r.Target, r.Confidence))
            .ToList();

        var definitions = result.Terms
            .Where(t => !string.IsNullOrEmpty(t.Definition))
            .ToDictionary(t => t.Text, t => t.Definition!, StringComparer.Ordinal);

        var categories = result.Terms
            .ToDictionary(t => t.Text, t => t.Category, StringComparer.Ordinal);

        var termScores = result.Terms.ToDictionary(t => t.Text, t => t.Score, StringComparer.Ordinal);

        var hierarchyBuilder = new TermHierarchyBuilder();
        var treeRoots = hierarchyBuilder.Build(termScores, hypernymRelations, definitions, categories);

        result.TermTree = treeRoots.Select(ConvertToOutput).ToList();

        // Apply depth info back to flat term entries
        ApplyDepthInfo(result.TermTree, textToEntry);

        // ── Compute statistics ──
        result.Statistics = ComputeStatistics(result);

        return result;
    }

    private static TermTreeNodeOutput ConvertToOutput(TermTreeNode node)
    {
        return new TermTreeNodeOutput
        {
            Term = node.Term,
            Category = node.Category,
            Score = node.Score,
            Definition = node.Definition,
            Depth = node.Depth,
            Children = node.Children.Select(ConvertToOutput).ToList()
        };
    }

    private static void ApplyDepthInfo(
        List<TermTreeNodeOutput> nodes,
        Dictionary<string, (int Index, TermEntry Entry)> textToEntry)
    {
        foreach (var node in nodes)
        {
            if (textToEntry.TryGetValue(node.Term, out var entry))
                entry.Entry.Depth = node.Depth;
            ApplyDepthInfo(node.Children, textToEntry);
        }
    }

    private static TermStatistics ComputeStatistics(TermResultFile result)
    {
        var stats = new TermStatistics
        {
            TotalTerms = result.Terms.Count,
            DefinedTerms = result.Terms.Count(t => !string.IsNullOrEmpty(t.Definition)),
            HypernymRelations = result.Relations.Count(r => r.Type == "HYPERNYM" || r.Type == "IS_A"),
            SynonymRelations = result.Relations.Count(r => r.Type == "SYNONYM"),
            AverageScore = result.Terms.Count > 0 ? result.Terms.Average(t => t.Score) : 0.0,
            RootNodes = result.TermTree.Count,
            MaxDepth = GetMaxDepth(result.TermTree)
        };

        return stats;
    }

    private static int GetMaxDepth(List<TermTreeNodeOutput> nodes, int currentDepth = 0)
    {
        if (nodes.Count == 0) return currentDepth;
        return nodes.Max(n => GetMaxDepth(n.Children, n.Depth));
    }

    /// <summary>
    /// Checks whether an enriched text value is clean and suitable for use as a term's canonical text.
    /// Returns false for JSON-encoded strings, text with noise prefixes (e.g., "的", "了"),
    /// or text with suspicious segmentation artifacts (embedded spaces in CJK text).
    /// </summary>
    private static bool IsCleanEnrichedText(string? enrichedText)
    {
        if (string.IsNullOrEmpty(enrichedText))
            return false;

        var trimmed = enrichedText.Trim();

        // Reject JSON-encoded strings (e.g., '{"value":"Lao She"}')
        if (trimmed.StartsWith("{"))
            return false;

        // Reject text with noise prefixes (common Chinese function words)
        if (trimmed.StartsWith("的") || trimmed.StartsWith("了"))
            return false;

        // Reject text with embedded spaces mixed with CJK characters (segmentation artifacts)
        // e.g., "美好 京沪高速铁路股份有限公司" or "的齐鲁"
        if (trimmed.Contains(" ") && trimmed.Any(c => c >= '\u4E00' && c <= '\u9FFF'))
            return false;

        return true;
    }
}
