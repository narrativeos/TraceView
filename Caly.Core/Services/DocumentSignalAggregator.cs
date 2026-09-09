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
/// Document-level aggregation unit (pure: no I/O, no UI dependency).
/// Consumes block-level <see cref="SemanticBlockResult"/> outputs and produces:
///   - Step C: document-level noun signals (deduped by text, counted, sorted by
///             role priority + score + occurrences).
///   - F3:     cross-block frequency demotion (high-frequency noise).
///   - F4:     cross-signal validation (entity + noun signal + dep core component).
/// See entity-extraction-quality-spec §5 (F3/F4) and §6 (Step C).
/// </summary>
public sealed class DocumentSignalAggregator
{
    /// <summary>F3: entity texts appearing &gt;= this many times are high-frequency noise.</summary>
    public int HighFrequencyThreshold { get; set; } = 10;

    /// <summary>F4: confidence below this, with no cross-signal support, triggers demotion.</summary>
    public double LowConfidence { get; set; } = 0.6;

    /// <summary>Cap on sample evidence strings kept per document noun signal.</summary>
    public int MaxEvidencePerSignal { get; set; } = 5;

    /// <summary>Dependency relations that mark a token as a core (Subject/Object) component.</summary>
    private static readonly HashSet<string> CoreRels = new(StringComparer.OrdinalIgnoreCase)
    {
        "nsubj", "nsubjpass", "dobj", "iobj",
    };

    /// <summary>
    /// Aggregates all block-level results into a document-level result
    /// (Step C noun signals + F3/F4 entity quality).
    /// </summary>
    public DocumentAggregationResult Aggregate(IEnumerable<SemanticBlockResult>? blocks)
    {
        var blockList = blocks?.ToList() ?? new List<SemanticBlockResult>();
        var result = new DocumentAggregationResult { TotalBlocks = blockList.Count };

        var entityGroups = new Dictionary<string, List<SemanticEntity>>(StringComparer.OrdinalIgnoreCase);
        var nounGroups = new Dictionary<string, List<SemanticNounSignal>>(StringComparer.OrdinalIgnoreCase);
        var nounBlockSets = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
        var depCoreTexts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int totalEntities = 0;
        int totalNounSignals = 0;

        for (int bi = 0; bi < blockList.Count; bi++)
        {
            var block = blockList[bi];

            foreach (var e in block.Entities ?? new List<SemanticEntity>())
            {
                if (string.IsNullOrWhiteSpace(e.Text)) continue;
                totalEntities++;
                var key = e.Text.Trim();
                if (!entityGroups.TryGetValue(key, out var elist))
                    entityGroups[key] = elist = new List<SemanticEntity>();
                elist.Add(e);
            }

            foreach (var s in block.NounSignals ?? new List<SemanticNounSignal>())
            {
                if (string.IsNullOrWhiteSpace(s.Text)) continue;
                totalNounSignals++;
                var key = s.Text.Trim();
                if (!nounGroups.TryGetValue(key, out var slist))
                    nounGroups[key] = slist = new List<SemanticNounSignal>();
                slist.Add(s);
                if (!nounBlockSets.TryGetValue(key, out var bset))
                    nounBlockSets[key] = bset = new HashSet<int>();
                bset.Add(bi);
            }

            CollectDepCoreTexts(block, depCoreTexts);
        }

        result.TotalEntities = totalEntities;
        result.TotalNounSignals = totalNounSignals;

        // Step C: document-level noun signals.
        result.NounSignals = BuildDocumentNounSignals(nounGroups, nounBlockSets);

        // F3 + F4: entity quality.
        result.EntityQuality = BuildEntityQuality(entityGroups, nounGroups, depCoreTexts);

        return result;
    }

    /// <summary>
    /// Collects the text of every token that is a core (Subject/Object) component
    /// in the block's dependency parse (child of an nsubj/nsubjpass/dobj/iobj edge).
    /// </summary>
    private static void CollectDepCoreTexts(SemanticBlockResult block, HashSet<string> coreTexts)
    {
        var tokens = block.DepTokens;
        var edges = block.DepEdges;
        if (tokens is null || tokens.Count == 0 || edges is null || edges.Count == 0)
            return;

        // Map token id -> text (robust to list ordering).
        var textById = new Dictionary<int, string>();
        foreach (var t in tokens)
            if (!string.IsNullOrWhiteSpace(t.Text))
                textById[t.Id] = t.Text.Trim();

        foreach (var edge in edges)
        {
            if (!CoreRels.Contains(edge.Rel)) continue;
            // The child of a core relation is the Subject/Object component.
            if (textById.TryGetValue(edge.Child, out var text))
                coreTexts.Add(text);
        }
    }

    /// <summary>Step C: dedupe noun signals by text, count, pick dominant role + best score, sort.</summary>
    private List<DocumentNounSignal> BuildDocumentNounSignals(
        Dictionary<string, List<SemanticNounSignal>> groups,
        Dictionary<string, HashSet<int>> blockSets)
    {
        var signals = new List<DocumentNounSignal>();
        foreach (var (text, list) in groups)
        {
            var roles = new HashSet<string>(StringComparer.Ordinal);
            var evidence = new List<string>();
            double bestScore = 0;
            string pos = string.Empty;

            foreach (var s in list)
            {
                roles.Add(s.SyntacticRole);
                if (s.Score > bestScore)
                {
                    bestScore = s.Score;
                    pos = s.Pos;
                }
                if (evidence.Count < MaxEvidencePerSignal)
                {
                    var ev = FormatEvidence(s.Evidence);
                    if (!string.IsNullOrEmpty(ev))
                        evidence.Add(ev);
                }
            }

            signals.Add(new DocumentNounSignal
            {
                Text = text,
                Pos = pos,
                SyntacticRole = PickDominantRole(roles),
                BestScore = bestScore,
                Occurrences = list.Count,
                BlockCount = blockSets.TryGetValue(text, out var bs) ? bs.Count : 1,
                Roles = roles.OrderBy(r => -RolePriority(r)).ToList(),
                Evidence = evidence,
            });
        }

        signals.Sort((a, b) =>
        {
            var c = RolePriority(b.SyntacticRole).CompareTo(RolePriority(a.SyntacticRole));
            if (c != 0) return c;
            c = b.BestScore.CompareTo(a.BestScore);
            if (c != 0) return c;
            return b.Occurrences.CompareTo(a.Occurrences);
        });

        return signals;
    }

    /// <summary>F3 (frequency) + F4 (cross-signal) per entity text.</summary>
    private List<DocumentEntityQuality> BuildEntityQuality(
        Dictionary<string, List<SemanticEntity>> entityGroups,
        Dictionary<string, List<SemanticNounSignal>> nounGroups,
        HashSet<string> depCoreTexts)
    {
        var result = new List<DocumentEntityQuality>();
        foreach (var (text, list) in entityGroups)
        {
            var occurrences = list.Count;
            var inNounSignals = nounGroups.ContainsKey(text);
            var inDepCore = depCoreTexts.Contains(text);
            var signalSupport = 1 + (inNounSignals ? 1 : 0) + (inDepCore ? 1 : 0);
            var boosted = inNounSignals && inDepCore;

            var isHighFreqNoise = occurrences >= HighFrequencyThreshold;
            var maxConfidence = list.Max(e => e.Confidence);
            var blockDemotionCount = list.Count(e => !e.Keep);

            var category = list
                .GroupBy(e => e.Category ?? string.Empty)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key)
                .First()
                .Key;

            var reasons = new List<string>();
            var keep = true;
            string? filter = null;

            // F3: high-frequency noise — strong demotion, wins over a cross-signal boost.
            if (isHighFreqNoise)
            {
                keep = false;
                filter = "high_frequency_noise";
                reasons.Add($"F3:high_frequency_noise(occurrences={occurrences}>=threshold={HighFrequencyThreshold})");
            }

            // F4: cross-signal validation.
            if (boosted)
            {
                reasons.Add("F4:cross_signal_boost(noun+dep_core)");
            }
            else if (!inNounSignals && !inDepCore && maxConfidence < LowConfidence)
            {
                if (keep) { keep = false; filter ??= "low_confidence_no_support"; }
                reasons.Add($"F4:low_confidence_no_support(confidence={maxConfidence:F2}<{LowConfidence})");
            }

            // Block-level consistent demotion (from F1/F2); a cross-signal boost overrides it.
            if (blockDemotionCount == occurrences && occurrences > 0 && !boosted)
            {
                if (keep) { keep = false; filter ??= "block_level_demoted"; }
                reasons.Add($"block_level:demoted_in_all_blocks({blockDemotionCount}/{occurrences})");
            }

            result.Add(new DocumentEntityQuality
            {
                Text = text,
                Category = category,
                Occurrences = occurrences,
                InNounSignals = inNounSignals,
                InDepCore = inDepCore,
                SignalSupport = signalSupport,
                Boosted = boosted,
                IsHighFrequencyNoise = isHighFreqNoise,
                Keep = keep,
                Filter = filter,
                Reasons = reasons,
            });
        }

        result.Sort((a, b) =>
        {
            if (a.Keep != b.Keep) return a.Keep ? -1 : 1;
            var c = b.SignalSupport.CompareTo(a.SignalSupport);
            if (c != 0) return c;
            return b.Occurrences.CompareTo(a.Occurrences);
        });

        return result;
    }

    /// <summary>Picks the highest-priority role present (Subject &gt; Object &gt; Attributive &gt; Adverbial &gt; other).</summary>
    private static string PickDominantRole(IEnumerable<string> roles)
    {
        string best = "unknown";
        int bestPriority = -1;
        foreach (var role in roles)
        {
            var p = RolePriority(role);
            if (p > bestPriority) { bestPriority = p; best = role; }
        }
        return best;
    }

    private static int RolePriority(string role) => role switch
    {
        "Subject" => 4,
        "Object" => 3,
        "Attributive" => 2,
        "Adverbial" => 1,
        _ => 0,
    };

    private static string FormatEvidence(Dictionary<string, object>? evidence)
    {
        if (evidence is null || evidence.Count == 0) return string.Empty;
        return string.Join("; ", evidence.Select(kv => $"{kv.Key}={kv.Value}"));
    }
}
