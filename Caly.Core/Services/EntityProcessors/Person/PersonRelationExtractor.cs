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
using Caly.Core.Services.EntityProcessors.Person.Models;

namespace Caly.Core.Services.EntityProcessors.Person;

/// <summary>
/// Extracts person-to-person relations from dependency parse data within a single block.
///
/// Relation types derived from dependency edges:
/// - appos → Alias (courtesy name / alternative name)
/// - conj → Colleague (associated people)
/// - assmod/assm → Family (kinship — checked against kinship title map)
/// - nn → Title (title/role decorating a name)
/// </summary>
public static class PersonRelationExtractor
{
    /// <summary>
    /// Extract person relations between PERSON entities in a single block.
    /// </summary>
    public static IReadOnlyList<EntityRelation> ExtractRelations(
        IReadOnlyList<SemanticEntity> entities,
        SemanticBlockResult block)
    {
        var relations = new List<EntityRelation>();

        if (entities.Count < 2 || block.DepTokens.Count == 0)
            return relations;

        // Convert DepTokens/DepEdges to HanLP format
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

        // Build: tokenId → entities containing this token
        var tokenToEntities = BuildTokenEntityMap(hanlpTokens, entities);

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

                if (!tokenToEntities.TryGetValue(edge.Head, out var headEntities))
                    continue;

                foreach (var headEntity in headEntities)
                {
                    if (headEntity.Id == entity.Id) continue;

                    var pairKey = GetPairKey(entity.Id, headEntity.Id);
                    if (processedPairs.Contains(pairKey)) continue;

                    EntityRelation? relation = edge.Rel switch
                    {
                        "appos" => CreateAliasRelation(entity, headEntity),
                        "conj" => CreateColleagueRelation(entity, headEntity),
                        "assmod" or "assm" => CreateFamilyRelation(entity, headEntity),
                        "nn" => CreateTitleRelation(entity, headEntity),
                        _ => null
                    };

                    if (relation != null)
                    {
                        processedPairs.Add(pairKey);
                        relations.Add(relation);
                    }
                }
            }
        }

        // Filter out low-quality relations
        var filtered = relations.Where(r =>
            IsLikelyPersonEntity(r.SourceEntityId) &&
            IsLikelyPersonEntity(r.TargetEntityId) &&
            r.Confidence >= 0.5 &&
            r.SourceEntityId.Length >= 1 &&
            r.TargetEntityId.Length >= 1
        ).ToList();

        return filtered;
    }

    /// <summary>
    /// Checks if the text is likely a genuine person name rather than noise.
    /// </summary>
    private static bool IsLikelyPersonEntity(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        // Filter out text containing underscores (segmentation artifacts)
        if (text.Contains('_'))
            return false;

        // Filter out text with newlines (segmentation artifacts)
        if (text.Contains('\n') || text.Contains('\r'))
            return false;

        // Filter out known non-name English words
        var nonNameWords = new[] { "Journey", "Cuisine", "Mayura", "Travel", "Food" };
        if (nonNameWords.Contains(text, StringComparer.OrdinalIgnoreCase))
            return false;

        return true;
    }

    /// <summary>
    /// Resolve cross-block coreference chains for PERSON entities.
    /// </summary>
    public static IReadOnlyList<EntityRelation> ResolveCrossBlock(
        IReadOnlyList<(SemanticEntity Entity, SemanticBlockResult Block)> allEntities,
        IReadOnlyList<SemanticBlockResult> allBlocks)
    {
        var relations = new List<EntityRelation>();

        // Collect all coreference chains
        var allChains = new List<HanLPCorefChain>();
        foreach (var block in allBlocks)
        {
            if (block.CorefChains != null)
                allChains.AddRange(block.CorefChains);
        }

        if (allChains.Count == 0)
            return relations;

        // Build person text lookup
        var personTexts = new HashSet<string>();
        foreach (var (entity, _) in allEntities)
        {
            personTexts.Add(entity.Text);
            if (!string.IsNullOrEmpty(entity.Normalized) && entity.Normalized != entity.Text)
                personTexts.Add(entity.Normalized);
        }

        foreach (var chain in allChains)
        {
            if (chain.Mentions == null || chain.Mentions.Count < 2)
                continue;

            var personMentions = chain.Mentions
                .Where(m => personTexts.Contains(m.Text))
                .Select(m => m.Text)
                .Distinct()
                .ToList();

            if (personMentions.Count < 2)
                continue;

            // Select canonical name
            string canonicalName;
            var principal = chain.Mentions.FirstOrDefault(m => m.IsPrincipal);
            if (principal != null && personMentions.Contains(principal.Text))
                canonicalName = principal.Text;
            else
                canonicalName = personMentions.OrderByDescending(m => m.Length).First();

            foreach (var mention in personMentions)
            {
                if (mention == canonicalName) continue;

                // Filter out low-quality coreference relations
                if (!IsLikelyPersonEntity(mention) || !IsLikelyPersonEntity(canonicalName))
                    continue;
                if (chain.Confidence < 0.5)
                    continue;

                relations.Add(new EntityRelation
                {
                    RelationType = PersonRelationType.Coreference.ToString(),
                    SourceEntityId = mention,
                    TargetEntityId = canonicalName,
                    Confidence = chain.Confidence,
                    Evidence = $"{mention} → {canonicalName} (chain: {chain.ChainId})"
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
            int start = entity.Span.Length >= 1 ? entity.Span[0] : 0;
            int end = entity.Span.Length >= 2 ? entity.Span[1] : start;
            foreach (var token in tokens)
            {
                if (token.Span.Count < 2) continue;
                if (token.Span[0] < end && token.Span[1] > start)
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

    private static EntityRelation CreateAliasRelation(SemanticEntity a, SemanticEntity b)
    {
        // The head (b) is the primary name, child (a) is the alias
        return new EntityRelation
        {
            RelationType = PersonRelationType.Alias.ToString(),
            SourceEntityId = a.Text,
            TargetEntityId = b.Text,
            Confidence = 0.7,
            Evidence = $"appos: {a.Text} → {b.Text}"
        };
    }

    private static EntityRelation CreateColleagueRelation(SemanticEntity a, SemanticEntity b)
    {
        return new EntityRelation
        {
            RelationType = PersonRelationType.Colleague.ToString(),
            SourceEntityId = a.Text,
            TargetEntityId = b.Text,
            Confidence = 0.65,
            Evidence = $"conj: {a.Text} ↔ {b.Text}"
        };
    }

    private static EntityRelation? CreateFamilyRelation(SemanticEntity a, SemanticEntity b)
    {
        // Check if either entity text is a kinship term
        var roleA = PersonValidator.ClassifyKinship(a.Text);
        var roleB = PersonValidator.ClassifyKinship(b.Text);

        if (roleA != KinshipRole.None)
        {
            return new EntityRelation
            {
                RelationType = PersonRelationType.Family.ToString(),
                SourceEntityId = a.Text,
                TargetEntityId = b.Text,
                Confidence = 0.75,
                Evidence = $"assmod family: {a.Text}({roleA}) → {b.Text}"
            };
        }

        if (roleB != KinshipRole.None)
        {
            return new EntityRelation
            {
                RelationType = PersonRelationType.Family.ToString(),
                SourceEntityId = b.Text,
                TargetEntityId = a.Text,
                Confidence = 0.75,
                Evidence = $"assmod family: {b.Text}({roleB}) → {a.Text}"
            };
        }

        return null;
    }

    private static EntityRelation? CreateTitleRelation(SemanticEntity child, SemanticEntity parent)
    {
        // Child is the title/role part, parent is the real name
        // Only create if child looks like a title and parent looks like a name
        var (isValidChild, _, _, _) = PersonValidator.Validate(child);
        var (isValidParent, _, _, _) = PersonValidator.Validate(parent);

        if (!isValidChild && isValidParent)
        {
            return new EntityRelation
            {
                RelationType = PersonRelationType.Title.ToString(),
                SourceEntityId = child.Text,
                TargetEntityId = parent.Text,
                Confidence = 0.6,
                Evidence = $"nn: {child.Text}(title) → {parent.Text}"
            };
        }

        return null;
    }

    private static string GetPairKey(string a, string b)
    {
        return string.CompareOrdinal(a, b) <= 0 ? $"{a}|{b}" : $"{b}|{a}";
    }
}
