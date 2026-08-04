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

using System.Collections.Generic;
using System.Linq;
using Caly.Core.Models;
using Caly.Core.Models.EntityProcessors;
using Caly.Core.Services.EntityProcessors.Organization.Models;

namespace Caly.Core.Services.EntityProcessors.Organization;

/// <summary>
/// Extracts organization-to-organization relations from dependency parse data.
///
/// Relation types derived from dependency edges:
/// - nn → FullName (compound name parts forming the full org name)
/// - appos → Abbreviation (abbreviation ↔ full name pair)
/// - conj → Sibling (parallel organizations mentioned together)
/// - assmod/assm → Subsidiary (parent-subsidiary hierarchy)
/// </summary>
public static class OrganizationRelationExtractor
{
    /// <summary>
    /// Extract organization relations between ORG entities in a single block.
    /// </summary>
    public static IReadOnlyList<EntityRelation> ExtractRelations(
        IReadOnlyList<SemanticEntity> entities,
        SemanticBlockResult block)
    {
        var relations = new List<EntityRelation>();

        if (entities.Count < 2 || block.DepTokens.Count == 0)
            return relations;

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

        var tokenToEntities = BuildTokenEntityMap(hanlpTokens, entities);
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
                        "nn" => CreateFullNameRelation(entity, headEntity),
                        "appos" => CreateAbbreviationRelation(entity, headEntity),
                        "conj" => CreateSiblingRelation(entity, headEntity),
                        "assmod" or "assm" => CreateSubsidiaryRelation(entity, headEntity),
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

        return relations;
    }

    /// <summary>
    /// Resolve cross-block coreference chains for ORGANIZATION entities.
    /// </summary>
    public static IReadOnlyList<EntityRelation> ResolveCrossBlock(
        IReadOnlyList<(SemanticEntity Entity, SemanticBlockResult Block)> allEntities,
        IReadOnlyList<SemanticBlockResult> allBlocks)
    {
        var relations = new List<EntityRelation>();

        var allChains = new List<HanLPCorefChain>();
        foreach (var block in allBlocks)
        {
            if (block.CorefChains != null)
                allChains.AddRange(block.CorefChains);
        }

        if (allChains.Count == 0) return relations;

        var orgTexts = new HashSet<string>();
        foreach (var (entity, _) in allEntities)
        {
            orgTexts.Add(entity.Text);
            if (!string.IsNullOrEmpty(entity.Normalized) && entity.Normalized != entity.Text)
                orgTexts.Add(entity.Normalized);
        }

        foreach (var chain in allChains)
        {
            if (chain.Mentions == null || chain.Mentions.Count < 2) continue;

            var orgMentions = chain.Mentions
                .Where(m => orgTexts.Contains(m.Text))
                .Select(m => m.Text).Distinct().ToList();

            if (orgMentions.Count < 2) continue;

            var canonical = chain.Mentions.FirstOrDefault(m => m.IsPrincipal);
            var canonicalName = canonical != null && orgMentions.Contains(canonical.Text)
                ? canonical.Text
                : orgMentions.OrderByDescending(m => m.Length).First();

            foreach (var mention in orgMentions)
            {
                if (mention == canonicalName) continue;
                relations.Add(new EntityRelation
                {
                    RelationType = OrganizationRelationType.Coreference.ToString(),
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
        List<HanLPToken> tokens, IReadOnlyList<SemanticEntity> entities)
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
                    if (!map.ContainsKey(token.Id)) map[token.Id] = new List<SemanticEntity>();
                    if (!map[token.Id].Contains(entity)) map[token.Id].Add(entity);
                }
            }
        }
        return map;
    }

    private static EntityRelation CreateFullNameRelation(SemanticEntity child, SemanticEntity parent)
    {
        return new EntityRelation
        {
            RelationType = OrganizationRelationType.FullName.ToString(),
            SourceEntityId = child.Text,
            TargetEntityId = parent.Text,
            Confidence = 0.7,
            Evidence = $"nn: {child.Text} → {parent.Text}"
        };
    }

    private static EntityRelation? CreateAbbreviationRelation(SemanticEntity a, SemanticEntity b)
    {
        // The longer name is likely the full name, shorter is the abbreviation
        var (abbr, full) = a.Text.Length <= b.Text.Length ? (a, b) : (b, a);

        // Only create if length difference is significant (≥3 chars)
        if (full.Text.Length - abbr.Text.Length < 3)
            return null;

        return new EntityRelation
        {
            RelationType = OrganizationRelationType.Abbreviation.ToString(),
            SourceEntityId = abbr.Text,
            TargetEntityId = full.Text,
            Confidence = 0.75,
            Evidence = $"appos: {abbr.Text} ↔ {full.Text}"
        };
    }

    private static EntityRelation CreateSiblingRelation(SemanticEntity a, SemanticEntity b)
    {
        return new EntityRelation
        {
            RelationType = OrganizationRelationType.Sibling.ToString(),
            SourceEntityId = a.Text,
            TargetEntityId = b.Text,
            Confidence = 0.65,
            Evidence = $"conj: {a.Text} ↔ {b.Text}"
        };
    }

    private static EntityRelation CreateSubsidiaryRelation(SemanticEntity child, SemanticEntity parent)
    {
        return new EntityRelation
        {
            RelationType = OrganizationRelationType.Subsidiary.ToString(),
            SourceEntityId = parent.Text,
            TargetEntityId = child.Text,
            Confidence = 0.6,
            Evidence = $"assmod: {parent.Text} → {child.Text}"
        };
    }

    private static string GetPairKey(string a, string b)
    {
        return string.CompareOrdinal(a, b) <= 0 ? $"{a}|{b}" : $"{b}|{a}";
    }
}
