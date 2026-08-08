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
using Caly.Core.Models.EntityProcessors;

namespace Caly.Core.Services;

/// <summary>
/// Scores term importance using multiple weighted dimensions:
/// TF-IDF, definition explicitness, position weight, relation density,
/// domain specificity, and cross-block frequency.
/// </summary>
public class TermScorer
{
    /// <summary>Weight for TF-IDF score (default: 0.30).</summary>
    public double TfIdfWeight { get; set; } = 0.30;

    /// <summary>Weight for explicit definition presence (default: 0.25).</summary>
    public double DefinitionWeight { get; set; } = 0.25;

    /// <summary>Weight for term's position in the document (default: 0.15).</summary>
    public double PositionWeight { get; set; } = 0.15;

    /// <summary>Weight for relation density (default: 0.15).</summary>
    public double RelationDensityWeight { get; set; } = 0.15;

    /// <summary>Weight for domain specificity (default: 0.10).</summary>
    public double DomainSpecificityWeight { get; set; } = 0.10;

    /// <summary>Weight for cross-block frequency (default: 0.05).</summary>
    public double CrossBlockWeight { get; set; } = 0.05;

    // Cached document statistics
    private int _totalBlocks;
    private int _totalTerms;
    private Dictionary<string, int>? _termFrequency;
    private Dictionary<string, int>? _termBlockCounts;

    /// <summary>
    /// Scores a set of enriched entities as terms.
    /// Returns a dictionary mapping normalized term text → score (0.0–1.0).
    /// </summary>
    public Dictionary<string, double> ScoreTerms(
        IReadOnlyList<(SemanticEntity Entity, SemanticBlockResult Block)> allTermEntities,
        IReadOnlyList<SemanticBlockResult> allBlocks,
        IReadOnlyDictionary<string, Dictionary<string, object>> enrichmentMetadata)
    {
        _totalBlocks = allBlocks.Count;

        // Compute term statistics
        ComputeTermStatistics(allTermEntities);

        _totalTerms = allTermEntities.Count;

        var scores = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var (entity, block) in allTermEntities)
        {
            var key = entity.Text; // Use entity text as key

            var tfIdf = ComputeTfIdf(entity.Text, block);
            var defScore = ComputeDefinitionScore(entity.Text, enrichmentMetadata);
            var posScore = ComputePositionScore(block, allBlocks);
            var relDensity = ComputeRelationDensity(entity.Id, enrichmentMetadata);
            var domainSpec = ComputeDomainSpecificity(entity);
            var crossBlock = ComputeCrossBlockScore(entity.Text);

            var score =
                TfIdfWeight * tfIdf +
                DefinitionWeight * defScore +
                PositionWeight * posScore +
                RelationDensityWeight * relDensity +
                DomainSpecificityWeight * domainSpec +
                CrossBlockWeight * crossBlock;

            scores[key] = Math.Clamp(score, 0.0, 1.0);
        }

        return scores;
    }

    private void ComputeTermStatistics(
        IReadOnlyList<(SemanticEntity Entity, SemanticBlockResult Block)> allTermEntities)
    {
        _termFrequency = new Dictionary<string, int>(StringComparer.Ordinal);
        _termBlockCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        var seenBlocks = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);

        foreach (var (entity, block) in allTermEntities)
        {
            var key = entity.Text;
            _termFrequency[key] = _termFrequency.TryGetValue(key, out var f) ? f + 1 : 1;

            if (!seenBlocks.ContainsKey(key))
                seenBlocks[key] = new HashSet<int>();
            foreach (var bid in block.BlockIds)
                seenBlocks[key].Add(bid);
        }

        foreach (var kvp in seenBlocks)
            _termBlockCounts[kvp.Key] = kvp.Value.Count;
    }

    /// <summary>
    /// TF-IDF: term frequency in document × inverse block frequency.
    /// Normalized to [0, 1].
    /// </summary>
    private double ComputeTfIdf(string termText, SemanticBlockResult block)
    {
        if (_termFrequency == null || _totalBlocks == 0 || _totalTerms == 0)
            return 0.0;

        var tf = _termFrequency.TryGetValue(termText, out var f) ? f : 1;
        var blockCount = _termBlockCounts?.TryGetValue(termText, out var bc) == true ? bc : 1;

        // Normalized TF: log(1 + tf)
        var tfNorm = Math.Log(1 + tf) / Math.Log(1 + _totalTerms);

        // IDF: log(totalBlocks / blockCount)
        var idf = Math.Log((double)_totalBlocks / Math.Max(1, blockCount));

        return Math.Clamp(tfNorm * idf / 5.0, 0.0, 1.0); // Divide by ~max to normalize
    }

    /// <summary>
    /// Definition score: 1.0 if has explicit definition, 0.5 if has context, 0.0 otherwise.
    /// </summary>
    private double ComputeDefinitionScore(
        string termText,
        IReadOnlyDictionary<string, Dictionary<string, object>> enrichmentMetadata)
    {
        // Check enriched metadata for definition presence
        foreach (var kvp in enrichmentMetadata)
        {
            if (kvp.Value.TryGetValue("original_text", out var origObj) &&
                origObj is string orig && orig == termText)
            {
                if (kvp.Value.TryGetValue("has_definition", out var hasDef) &&
                    hasDef is bool hd && hd)
                    return 1.0;

                if (kvp.Value.TryGetValue("context", out var ctx) &&
                    ctx is string cs && cs.Length > 10)
                    return 0.5;
            }
        }

        return 0.0;
    }

    /// <summary>
    /// Position score: higher for terms appearing early in the document (title, abstract).
    /// </summary>
    private double ComputePositionScore(SemanticBlockResult block, IReadOnlyList<SemanticBlockResult> allBlocks)
    {
        if (allBlocks.Count == 0) return 0.0;

        // Find the index of this block in the ordered list
        var blockIndex = -1;
        for (int i = 0; i < allBlocks.Count; i++)
        {
            if (allBlocks[i] == block) { blockIndex = i; break; }
        }
        if (blockIndex < 0) return 0.0;

        // First 10% of blocks get highest score
        var positionRatio = (double)blockIndex / allBlocks.Count;
        if (positionRatio <= 0.10) return 1.0;
        if (positionRatio <= 0.25) return 0.75;
        if (positionRatio <= 0.50) return 0.50;
        return 0.25;
    }

    /// <summary>
    /// Relation density: how many other terms this term is connected to.
    /// </summary>
    private double ComputeRelationDensity(
        string entityId,
        IReadOnlyDictionary<string, Dictionary<string, object>> enrichmentMetadata)
    {
        // Check if relations were stored in metadata
        foreach (var kvp in enrichmentMetadata)
        {
            if (kvp.Value.TryGetValue("source_entity_id", out var sidObj) &&
                sidObj is string sid && sid == entityId)
            {
                if (kvp.Value.TryGetValue("relation_count", out var rc) &&
                    rc is int count)
                    return Math.Min(count / 10.0, 1.0);
            }
        }
        return 0.0;
    }

    /// <summary>
    /// Domain specificity: non-generic entity types score higher.
    /// CONCEPT > PERSON/ORGANIZATION > LOCATION/DATE.
    /// </summary>
    private double ComputeDomainSpecificity(SemanticEntity entity)
    {
        return entity.Category switch
        {
            "CONCEPT" => 0.90,       // Most likely domain-specific
            "FACILITY" => 0.80,
            "ORGANIZATION" => 0.70,
            "PERSON" => 0.60,
            "LOCATION" => 0.40,
            "DATE" => 0.30,
            "TIME" => 0.30,
            _ => 0.50
        };
    }

    /// <summary>
    /// Cross-block frequency: higher for terms appearing in many different blocks.
    /// </summary>
    private double ComputeCrossBlockScore(string termText)
    {
        if (_termBlockCounts == null || _totalBlocks == 0)
            return 0.0;

        var blockCount = _termBlockCounts.TryGetValue(termText, out var bc) ? bc : 1;
        return Math.Min((double)blockCount / _totalBlocks * 5.0, 1.0);
    }
}
