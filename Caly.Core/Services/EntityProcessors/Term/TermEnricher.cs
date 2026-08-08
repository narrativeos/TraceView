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
using System.Threading;
using System.Threading.Tasks;
using Caly.Core.Models;
using Caly.Core.Models.EntityProcessors;

namespace Caly.Core.Services.EntityProcessors.Term;

/// <summary>
/// Enriches term entities by linking them to definitions, computing context summaries,
/// and normalizing term text for cross-block matching.
/// </summary>
public class TermEnricher
{
    /// <summary>
    /// Enriches a single term entity with definition and context.
    /// </summary>
    /// <param name="entity">The entity to enrich.</param>
    /// <param name="block">The block containing this entity.</param>
    /// <param name="allDefinitions">All term definitions across the document.</param>
    /// <param name="ct">Cancellation token.</param>
    public Task<EntityEnrichmentResult> EnrichAsync(
        SemanticEntity entity,
        SemanticBlockResult block,
        List<TermDefinition> allDefinitions,
        CancellationToken ct = default)
    {
        var result = new EntityEnrichmentResult
        {
            Source = "TermEnricher",
            Confidence = entity.Confidence
        };

        // ── 1. Normalize term text ──
        var normalized = NormalizeTermText(entity.Text);
        result.EnrichedText = normalized;
        result.Metadata["normalized"] = normalized;
        result.Metadata["original_text"] = entity.Text;
        result.Metadata["entity_category"] = entity.Category;

        // ── 2. Find matching definitions ──
        var matchingDefs = FindMatchingDefinitions(entity.Text, normalized, allDefinitions, block);
        if (matchingDefs.Count > 0)
        {
            var bestDef = matchingDefs.OrderByDescending(d => d.Confidence).First();
            result.Metadata["has_definition"] = true;
            result.Metadata["definition"] = bestDef.DefinitionText;
            result.Metadata["definition_source"] = bestDef.Source;
            result.Metadata["hypernym"] = bestDef.DefinitionText;
            result.Metadata["definition_confidence"] = bestDef.Confidence;
            result.Confidence = System.Math.Max(result.Confidence, bestDef.Confidence);

            if (matchingDefs.Count > 1)
                result.Metadata["definition_count"] = matchingDefs.Count;

            // Extract synonyms from SYNONYM-type definitions
            var synonyms = matchingDefs
                .Where(d => d.RelationType == "SYNONYM")
                .Select(d => d.DefinitionText)
                .Distinct()
                .ToList();
            if (synonyms.Count > 0)
                result.Metadata["synonyms"] = string.Join("|", synonyms);
        }
        else
        {
            result.Metadata["has_definition"] = false;
        }

        // ── 3. Context summary ──
        var contextSummary = ExtractContextSummary(entity, block);
        if (!string.IsNullOrEmpty(contextSummary))
            result.Metadata["context"] = contextSummary;

        return Task.FromResult(result);
    }

    /// <summary>
    /// Normalizes term text for consistent cross-block matching.
    /// </summary>
    public static string NormalizeTermText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        text = text.Trim();

        // Remove common prefixes
        if (text.StartsWith("的") && text.Length > 2) text = text.Substring(1);

        return text;
    }

    /// <summary>
    /// P2: Cross-block term normalization — merges term variants that are likely the same concept.
    /// Heuristic: if term A is a PREFIX of term B (A appears at the start of B),
    /// merge them using the longer form as canonical. E.g., "铝" → "铝合金".
    /// Also allows cross-category merging for closely related categories (e.g., PERSON↔MATERIAL for "奥氏"→"奥氏体").
    /// </summary>
    /// <param name="allEntities">All entities across all blocks, with their frequencies.</param>
    /// <returns>Dictionary mapping variant text → canonical text. Empty if no merges needed.</returns>
    public static Dictionary<string, string> ComputeTermMerges(
        IReadOnlyList<(SemanticEntity Entity, SemanticBlockResult Block)> allEntities)
    {
        var merges = new Dictionary<string, string>(StringComparer.Ordinal);

        // Build frequency and category lookup across ALL terms
        var freq = new Dictionary<string, int>(StringComparer.Ordinal);
        var catMap = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (entity, _) in allEntities)
        {
            var text = entity.Text;
            if (string.IsNullOrWhiteSpace(text)) continue;
            freq[text] = freq.TryGetValue(text, out var f) ? f + 1 : 1;
            catMap[text] = entity.Category ?? "UNKNOWN";
        }

        // Collect all unique terms
        var allTerms = freq.Keys.OrderByDescending(t => t.Length).ToList();

        // Categories that can cross-merge (conceptually related)
        var crossMergeable = new HashSet<string>(StringComparer.Ordinal)
        {
            "PERSON", "MATERIAL", "PRODUCT", "STANDARD", "ORGANIZATION", "INSTITUTION"
        };

        for (int i = 0; i < allTerms.Count; i++)
        {
            var longer = allTerms[i];
            if (longer.Length < 3) continue; // Too short to be canonical
            if (freq[longer] < 2) continue;  // Too rare

            for (int j = i + 1; j < allTerms.Count; j++)
            {
                var shorter = allTerms[j];
                if (shorter.Length < 1) continue;
                if (shorter.Length >= longer.Length) continue;
                if (merges.ContainsKey(shorter)) continue;

                // ── P2 fix: Use StartsWith instead of Contains ──
                // Only merge if shorter is a PREFIX of longer (not arbitrary substring)
                if (!longer.StartsWith(shorter, StringComparison.Ordinal))
                    continue;

                // ── P2 fix: Allow cross-category merging ──
                var longerCat = catMap.TryGetValue(longer, out var lc) ? lc : "UNKNOWN";
                var shorterCat = catMap.TryGetValue(shorter, out var sc) ? sc : "UNKNOWN";
                if (longerCat != shorterCat)
                {
                    // Only allow cross-category if both are in cross-mergeable set
                    if (!crossMergeable.Contains(longerCat) || !crossMergeable.Contains(shorterCat))
                        continue;
                }

                // Don't merge if shorter is much more frequent (it IS the canonical)
                if (freq[shorter] > freq[longer] * 2)
                    continue;

                merges[shorter] = longer;
                System.Diagnostics.Debug.WriteLine(
                    $"[TermEnricher] Merge: '{shorter}'→'{longer}' (cat {shorterCat}→{longerCat}, freq {freq[shorter]}→{freq[longer]})");
            }
        }

        return merges;
    }

    /// <summary>
    /// Finds term definitions that match the given entity text.
    /// Uses exact match first, then partial overlap.
    /// </summary>
    private static List<TermDefinition> FindMatchingDefinitions(
        string entityText,
        string normalizedText,
        List<TermDefinition> allDefinitions,
        SemanticBlockResult block)
    {
        var matches = new List<TermDefinition>();

        // Check block-local definitions first (higher priority)
        foreach (var def in block.TermDefinitions)
        {
            if (IsTermMatch(entityText, normalizedText, def.TermText))
                matches.Add(def);
        }

        // Then check cross-block definitions
        foreach (var def in allDefinitions)
        {
            if (block.TermDefinitions.Contains(def))
                continue; // Already checked

            if (IsTermMatch(entityText, normalizedText, def.TermText))
                matches.Add(def);
        }

        return matches;
    }

    private static bool IsTermMatch(string entityText, string normalizedText, string defTermText)
    {
        // Exact match
        if (entityText == defTermText || normalizedText == defTermText)
            return true;

        // One contains the other (for multi-word entities)
        if (entityText.Contains(defTermText) || defTermText.Contains(entityText))
            return true;

        return false;
    }

    /// <summary>
    /// Extracts a short context summary for a term from its surrounding text.
    /// </summary>
    private static string ExtractContextSummary(SemanticEntity entity, SemanticBlockResult block)
    {
        if (string.IsNullOrWhiteSpace(block.Content))
            return string.Empty;

        if (entity.Span.Length < 2)
            return string.Empty;

        var start = entity.Span[0];
        var end = entity.Span[1];
        var content = block.Content;

        // Get surrounding context (±30 chars around the entity)
        var contextStart = System.Math.Max(0, start - 15);
        var contextEnd = System.Math.Min(content.Length, end + 30);
        var context = content.Substring(contextStart, contextEnd - contextStart);

        // Truncate if too long
        if (context.Length > 80)
            context = context.Substring(0, 80) + "...";

        return context.Trim();
    }
}
