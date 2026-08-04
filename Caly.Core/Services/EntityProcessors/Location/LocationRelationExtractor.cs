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

namespace Caly.Core.Services.EntityProcessors.Location;

/// <summary>
/// Extracts location-to-location relations from HanLP dependency parse and coreference chains.
///
/// Extracted from <see cref="SemanticAnalysisService"/> to serve as a shared utility
/// for both the legacy geocoding pipeline and the new <see cref="LocationProcessor"/>.
///
/// Relation types:
/// - ParentChild: nn dependency between two LOCATION entities (e.g., "北京" → "北三环")
/// - Ownership: assmod/assm dependency (e.g., "海淀区" → "五道口")
/// - Parallel: conj dependency (e.g., "北京" → "上海")
/// - Coreference: HanLP coreference chain mentions (e.g., "帝都" → "北京")
/// </summary>
public static class LocationRelationExtractor
{
    /// <summary>
    /// Extracts intra-block location relations from dependency parse data.
    /// Mirrors the logic in SemanticAnalysisService.ExtractLocationRelations().
    /// </summary>
    public static List<LocationRelation> ExtractRelations(
        List<HanLPToken> tokens,
        List<HanLPDepEdge> deps,
        List<SemanticEntity> allEntities,
        string fullText)
    {
        var relations = new List<LocationRelation>();
        var locationEntities = allEntities.Where(e => e.Category == "LOCATION").ToList();

        if (locationEntities.Count < 2 && !HasMultiTokenLocationEntity(locationEntities, tokens))
            return relations;

        // Build lookup: tokenId → entities that contain this token
        var tokenIdToEntities = new Dictionary<int, List<SemanticEntity>>();
        foreach (var entity in allEntities)
        {
            int entityStart = entity.Span.Length >= 1 ? entity.Span[0] : 0;
            int entityEnd = entity.Span.Length >= 2 ? entity.Span[1] : entityStart;

            foreach (var token in tokens)
            {
                if (token.Span.Count < 2) continue;
                int tokenStart = token.Span[0];
                int tokenEnd = token.Span[1];
                if (tokenStart < entityEnd && tokenEnd > entityStart)
                {
                    if (!tokenIdToEntities.ContainsKey(token.Id))
                        tokenIdToEntities[token.Id] = new List<SemanticEntity>();
                    if (!tokenIdToEntities[token.Id].Contains(entity))
                        tokenIdToEntities[token.Id].Add(entity);
                }
            }
        }

        // Build lookup: tokenId → HanLPToken
        var tokenById = tokens.ToDictionary(t => t.Id, t => t);

        // Track processed entity pairs to avoid duplicates
        var processedPairs = new HashSet<string>();

        // Process each LOCATION entity's tokens for external relations
        foreach (var entity in locationEntities)
        {
            int entityStart = entity.Span.Length >= 1 ? entity.Span[0] : 0;
            int entityEnd = entity.Span.Length >= 2 ? entity.Span[1] : entityStart;

            var entityTokens = tokens.Where(t =>
            {
                if (t.Span.Count < 2) return false;
                return t.Span[0] < entityEnd && t.Span[1] > entityStart;
            }).ToList();

            var entityTokenIds = new HashSet<int>(entityTokens.Select(t => t.Id));

            foreach (var token in entityTokens)
            {
                var edge = deps.FirstOrDefault(d => d.Child == token.Id);
                if (edge == null) continue;

                int headId = edge.Head;
                if (headId < 0) continue;
                if (headId == token.Id) continue;

                if (!tokenIdToEntities.TryGetValue(headId, out var headEntities))
                    continue;

                foreach (var headEntity in headEntities)
                {
                    if (headEntity.Id == entity.Id) continue;

                    var pairKey = GetPairKey(entity.Text, headEntity.Text);
                    if (processedPairs.Contains(pairKey)) continue;

                    LocationRelation? relation = null;

                    switch (edge.Rel)
                    {
                        case "nn":
                            if (headEntity.Category == "LOCATION")
                            {
                                relation = new LocationRelation
                                {
                                    RelationType = LocationRelationType.ParentChild,
                                    SourceLocation = headEntity.Text,
                                    TargetLocation = entity.Text,
                                    DepRel = edge.Rel,
                                    Confidence = entity.Confidence,
                                    Evidence = ExtractEvidence(fullText, entityStart, entityEnd)
                                };
                            }
                            break;

                        case "assmod":
                        case "assm":
                            if (headEntity.Category == "LOCATION")
                            {
                                relation = new LocationRelation
                                {
                                    RelationType = LocationRelationType.Ownership,
                                    SourceLocation = headEntity.Text,
                                    TargetLocation = entity.Text,
                                    DepRel = edge.Rel,
                                    Confidence = entity.Confidence,
                                    Evidence = ExtractEvidence(fullText, entityStart, entityEnd)
                                };
                            }
                            break;

                        case "conj":
                            if (headEntity.Category == "LOCATION")
                            {
                                relation = new LocationRelation
                                {
                                    RelationType = LocationRelationType.Parallel,
                                    SourceLocation = headEntity.Text,
                                    TargetLocation = entity.Text,
                                    DepRel = edge.Rel,
                                    Confidence = entity.Confidence,
                                    Evidence = ExtractEvidence(fullText,
                                        Math.Min(entityStart, headEntity.Span.Length >= 1 ? headEntity.Span[0] : entityStart),
                                        Math.Max(entityEnd, headEntity.Span.Length >= 2 ? headEntity.Span[1] : entityEnd))
                                };
                            }
                            break;
                    }

                    if (relation != null)
                    {
                        processedPairs.Add(pairKey);
                        relations.Add(relation);
                    }
                }
            }

            // Handle multi-token entity internal nn edges
            if (entityTokens.Count >= 2)
            {
                foreach (var token in entityTokens)
                {
                    var edge = deps.FirstOrDefault(d => d.Child == token.Id);
                    if (edge == null || edge.Rel != "nn") continue;

                    int headId = edge.Head;
                    if (!entityTokenIds.Contains(headId)) continue;
                    if (!tokenById.TryGetValue(headId, out var headToken)) continue;

                    var internalPairKey = $"{headToken.Text}|{token.Text}|internal";
                    if (processedPairs.Contains(internalPairKey)) continue;

                    if (IsLikelyIndependentLocation(headToken.Text) && IsLikelyIndependentLocation(token.Text))
                    {
                        relations.Add(new LocationRelation
                        {
                            RelationType = LocationRelationType.ParentChild,
                            SourceLocation = headToken.Text,
                            TargetLocation = token.Text,
                            DepRel = $"nn(internal:{entity.Text})",
                            Confidence = entity.Confidence,
                            Evidence = entity.Text
                        });
                        processedPairs.Add(internalPairKey);
                    }
                }
            }
        }

        return relations;
    }

    /// <summary>
    /// Resolves cross-block coreference chains for LOCATION entities.
    /// </summary>
    public static List<LocationRelation> ResolveCrossBlockCoreferences(List<SemanticBlockResult> blocks)
    {
        var relations = new List<LocationRelation>();

        var allChains = new List<HanLPCorefChain>();
        foreach (var block in blocks)
        {
            if (block.CorefChains != null)
                allChains.AddRange(block.CorefChains);
        }

        if (allChains.Count == 0)
            return relations;

        var locationTexts = new HashSet<string>();
        foreach (var block in blocks)
        {
            foreach (var entity in block.LocationEntities)
            {
                locationTexts.Add(entity.Text);
                if (!string.IsNullOrEmpty(entity.Normalized) && entity.Normalized != entity.Text)
                    locationTexts.Add(entity.Normalized);
            }
        }

        foreach (var chain in allChains)
        {
            if (chain.Mentions == null || chain.Mentions.Count < 2)
                continue;

            var locationMentions = chain.Mentions
                .Where(m => locationTexts.Contains(m.Text))
                .Select(m => m.Text)
                .Distinct()
                .ToList();

            if (locationMentions.Count < 2)
                continue;

            string canonicalName;
            var principalMention = chain.Mentions.FirstOrDefault(m => m.IsPrincipal);
            if (principalMention != null && locationMentions.Contains(principalMention.Text))
                canonicalName = principalMention.Text;
            else
                canonicalName = locationMentions.OrderByDescending(m => m.Length).First();

            foreach (var mention in locationMentions)
            {
                if (mention == canonicalName) continue;

                relations.Add(new LocationRelation
                {
                    RelationType = LocationRelationType.Coreference,
                    SourceLocation = mention,
                    TargetLocation = canonicalName,
                    DepRel = string.Empty,
                    Confidence = chain.Confidence,
                    Evidence = $"{mention} → {canonicalName} (chain: {chain.ChainId})"
                });
            }
        }

        return relations;
    }

    private static bool HasMultiTokenLocationEntity(List<SemanticEntity> locationEntities, List<HanLPToken> tokens)
    {
        foreach (var entity in locationEntities)
        {
            int entityStart = entity.Span.Length >= 1 ? entity.Span[0] : 0;
            int entityEnd = entity.Span.Length >= 2 ? entity.Span[1] : entityStart;

            int tokenCount = tokens.Count(t =>
            {
                if (t.Span.Count < 2) return false;
                return t.Span[0] < entityEnd && t.Span[1] > entityStart;
            });

            if (tokenCount >= 2) return true;
        }
        return false;
    }

    private static bool IsLikelyIndependentLocation(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (text.Length <= 1) return false;
        if (!System.Text.RegularExpressions.Regex.IsMatch(text, @"[\u4e00-\u9fff]")) return false;
        if (text.All(c => char.IsPunctuation(c) || char.IsWhiteSpace(c))) return false;
        return true;
    }

    private static string ExtractEvidence(string fullText, int start, int end)
    {
        if (string.IsNullOrEmpty(fullText)) return string.Empty;
        if (start < 0 || end > fullText.Length || start >= end) return string.Empty;
        return fullText.Substring(start, end - start);
    }

    private static string GetPairKey(string a, string b)
    {
        return string.CompareOrdinal(a, b) <= 0 ? $"{a}|{b}" : $"{b}|{a}";
    }
}
