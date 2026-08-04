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
using Caly.Core.Services.EntityProcessors.Temporal.Models;

namespace Caly.Core.Services.EntityProcessors.Temporal;

/// <summary>
/// Extracts temporal relations from dependency parse data within a single block.
///
/// Relation types derived from dependency edges:
/// - conj → Before/After (ordered by normalized ISO 8601 value)
/// - nn / compound:nn → Contains/ContainedBy (parent time span contains child)
/// - appos → Equivalent (different expressions for the same time)
/// </summary>
public static class TemporalRelationExtractor
{
    /// <summary>
    /// Extract temporal relations between DATE entities in a single block.
    /// </summary>
    /// <param name="entities">All DATE entities in the block.</param>
    /// <param name="block">The block containing dependency parse data.</param>
    /// <param name="normalizer">The normalizer to get ISO 8601 values for comparison.</param>
    public static IReadOnlyList<EntityRelation> ExtractRelations(
        IReadOnlyList<SemanticEntity> entities,
        SemanticBlockResult block,
        TemporalNormalizer normalizer)
    {
        var relations = new List<EntityRelation>();

        if (entities.Count < 2 || block.DepTokens.Count == 0)
            return relations;

        // Convert DepTokens/DepEdges to HanLP format for processing
        var hanlpTokens = block.DepTokens.Select(dt => new HanLPToken
        {
            Id = dt.Id,
            Text = dt.Text,
            Pos = dt.Pos,
            Span = new List<int> { 0, dt.Text.Length }
        }).ToList();

        var hanlpDeps = block.DepEdges.Select(de => new HanLPDepEdge
        {
            Child = de.Child,
            Head = de.Head,
            Rel = de.Rel
        }).ToList();

        var tokenById = hanlpTokens.ToDictionary(t => t.Id, t => t);

        // Build: tokenId → entities containing this token
        var tokenIdToEntities = BuildTokenEntityMap(hanlpTokens, entities);

        // Build normalized values cache
        var normalizedValues = new Dictionary<string, string?>();
        foreach (var entity in entities)
        {
            if (!string.IsNullOrEmpty(entity.Normalized))
                normalizedValues[entity.Id] = entity.Normalized;
            else
            {
                var result = normalizer.Normalize(entity);
                normalizedValues[entity.Id] = result.GetSortKey();
            }
        }

        // Track processed pairs
        var processedPairs = new HashSet<string>();

        foreach (var entity in entities)
        {
            int entityStart = entity.Span.Length >= 1 ? entity.Span[0] : 0;
            int entityEnd = entity.Span.Length >= 2 ? entity.Span[1] : entityStart;

            var entityTokens = hanlpTokens.Where(t =>
            {
                if (t.Span.Count < 2) return false;
                return t.Span[0] < entityEnd && t.Span[1] > entityStart;
            }).ToList();

            foreach (var token in entityTokens)
            {
                var edge = hanlpDeps.FirstOrDefault(d => d.Child == token.Id);
                if (edge == null) continue;
                if (edge.Head < 0 || edge.Head == token.Id) continue;

                if (!tokenIdToEntities.TryGetValue(edge.Head, out var headEntities))
                    continue;

                foreach (var headEntity in headEntities)
                {
                    if (headEntity.Id == entity.Id) continue;

                    var pairKey = GetRelationPairKey(entity.Id, headEntity.Id);
                    if (processedPairs.Contains(pairKey)) continue;

                    EntityRelation? relation = CreateRelation(edge.Rel, entity, headEntity, normalizedValues);
                    if (relation != null)
                    {
                        processedPairs.Add(pairKey);
                        relations.Add(relation);
                    }
                }
            }
        }

        return relations;
    }

    /// <summary>
    /// Build cross-block timeline relations by sorting all DATE entities by their ISO 8601 value.
    /// </summary>
    public static IReadOnlyList<EntityRelation> BuildTimeline(
        IReadOnlyList<(SemanticEntity Entity, SemanticBlockResult Block)> allEntities,
        TemporalNormalizer normalizer)
    {
        var relations = new List<EntityRelation>();

        if (allEntities.Count < 2)
            return relations;

        // Compute sort keys
        var withKeys = allEntities
            .Select(e => (
                Entity: e.Entity,
                SortKey: GetNormalizedSortKey(e.Entity, normalizer)))
            .Where(e => e.SortKey != null)
            .OrderBy(e => e.SortKey)
            .ToList();

        // Build Before/After chains
        for (int i = 0; i < withKeys.Count - 1; i++)
        {
            var current = withKeys[i];
            var next = withKeys[i + 1];

            if (current.SortKey == next.SortKey)
            {
                // Same timestamp → Equivalent
                relations.Add(new EntityRelation
                {
                    RelationType = TemporalRelationType.Equivalent.ToString(),
                    SourceEntityId = current.Entity.Id,
                    TargetEntityId = next.Entity.Id,
                    Confidence = 0.8,
                    Evidence = $"Same timestamp: {current.SortKey}"
                });
            }
            else
            {
                relations.Add(new EntityRelation
                {
                    RelationType = TemporalRelationType.Before.ToString(),
                    SourceEntityId = current.Entity.Id,
                    TargetEntityId = next.Entity.Id,
                    Confidence = 0.9,
                    Evidence = $"{current.SortKey} < {next.SortKey}"
                });

                relations.Add(new EntityRelation
                {
                    RelationType = TemporalRelationType.After.ToString(),
                    SourceEntityId = next.Entity.Id,
                    TargetEntityId = current.Entity.Id,
                    Confidence = 0.9,
                    Evidence = $"{next.SortKey} > {current.SortKey}"
                });
            }
        }

        return relations;
    }

    // ── Private helpers ─────────────────────────────────────────────

    private static Dictionary<int, List<SemanticEntity>> BuildTokenEntityMap(
        List<HanLPToken> tokens,
        IReadOnlyList<SemanticEntity> entities)
    {
        var map = new Dictionary<int, List<SemanticEntity>>();

        foreach (var entity in entities)
        {
            int entityStart = entity.Span.Length >= 1 ? entity.Span[0] : 0;
            int entityEnd = entity.Span.Length >= 2 ? entity.Span[1] : entityStart;

            foreach (var token in tokens)
            {
                if (token.Span.Count < 2) continue;
                if (token.Span[0] < entityEnd && token.Span[1] > entityStart)
                {
                    if (!map.ContainsKey(token.Id))
                        map[token.Id] = new List<SemanticEntity>();
                    if (!map[token.Id].Contains(entity))
                        map[token.Id].Add(entity);
                }
            }
        }

        return map;
    }

    private static EntityRelation? CreateRelation(
        string depRel,
        SemanticEntity child,
        SemanticEntity parent,
        Dictionary<string, string?> normalizedValues)
    {
        return depRel switch
        {
            "conj" => CreateConjRelation(child, parent, normalizedValues),
            "nn" or "compound:nn" => CreateContainsRelation(child, parent, normalizedValues),
            "appos" => CreateEquivalentRelation(child, parent),
            _ => null
        };
    }

    private static EntityRelation? CreateConjRelation(
        SemanticEntity a, SemanticEntity b,
        Dictionary<string, string?> values)
    {
        var keyA = values.GetValueOrDefault(a.Id);
        var keyB = values.GetValueOrDefault(b.Id);

        if (keyA == null || keyB == null)
            return null;

        var cmp = string.Compare(keyA, keyB, StringComparison.Ordinal);
        if (cmp == 0)
        {
            return new EntityRelation
            {
                RelationType = TemporalRelationType.Equivalent.ToString(),
                SourceEntityId = a.Id,
                TargetEntityId = b.Id,
                Confidence = 0.7,
                Evidence = $"conj: {keyA} = {keyB}"
            };
        }

        return new EntityRelation
        {
            RelationType = cmp < 0 ? TemporalRelationType.Before.ToString() : TemporalRelationType.After.ToString(),
            SourceEntityId = a.Id,
            TargetEntityId = b.Id,
            Confidence = 0.8,
            Evidence = $"conj: {keyA} vs {keyB}"
        };
    }

    private static EntityRelation? CreateContainsRelation(
        SemanticEntity child, SemanticEntity parent,
        Dictionary<string, string?> values)
    {
        var childKey = values.GetValueOrDefault(child.Id);
        var parentKey = values.GetValueOrDefault(parent.Id);

        // "2024年3月" (child) nn→ "2024年" (parent) → parent Contains child
        if (childKey != null && parentKey != null &&
            childKey.StartsWith(parentKey, StringComparison.Ordinal))
        {
            return new EntityRelation
            {
                RelationType = TemporalRelationType.Contains.ToString(),
                SourceEntityId = parent.Id,
                TargetEntityId = child.Id,
                Confidence = 0.85,
                Evidence = $"nn: {parentKey} contains {childKey}"
            };
        }

        return null;
    }

    private static EntityRelation CreateEquivalentRelation(SemanticEntity a, SemanticEntity b)
    {
        return new EntityRelation
        {
            RelationType = TemporalRelationType.Equivalent.ToString(),
            SourceEntityId = a.Id,
            TargetEntityId = b.Id,
            Confidence = 0.6,
            Evidence = $"appos: {a.Text} ↔ {b.Text}"
        };
    }

    private static string? GetNormalizedSortKey(SemanticEntity entity, TemporalNormalizer normalizer)
    {
        if (!string.IsNullOrEmpty(entity.Normalized))
            return entity.Normalized;

        var result = normalizer.Normalize(entity);
        return result.GetSortKey();
    }

    private static string GetRelationPairKey(string idA, string idB)
    {
        return string.CompareOrdinal(idA, idB) <= 0 ? $"{idA}|{idB}" : $"{idB}|{idA}";
    }
}
