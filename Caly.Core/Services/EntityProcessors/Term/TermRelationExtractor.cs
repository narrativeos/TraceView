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

namespace Caly.Core.Services.EntityProcessors.Term;

/// <summary>
/// Extracts typed relations between terms within and across blocks.
/// Combines definition-based relations, dependency-based relations, and co-occurrence.
/// </summary>
public class TermRelationExtractor
{
    /// <summary>
    /// Extracts intra-block term relations from definitions and dependency parse.
    /// </summary>
    public List<TermRelation> ExtractRelations(
        IReadOnlyList<SemanticEntity> entities,
        SemanticBlockResult block,
        List<TermDefinition> allDefinitions)
    {
        var relations = new List<TermRelation>();

        // ── 1. Definition-based relations (HYPERNYM / SYNONYM) ──
        foreach (var def in block.TermDefinitions)
        {
            if (def.RelationType == "IS_A")
            {
                relations.Add(new TermRelation
                {
                    RelationType = TermRelationType.Hypernym,
                    SourceTerm = def.TermText,
                    TargetTerm = def.DefinitionText,
                    Confidence = def.Confidence,
                    Evidence = def.Evidence,
                    SourceBlockIds = block.BlockIds.Count > 0 ? block.BlockIds : new List<int>(),
                    Source = "definition"
                });
            }
            else if (def.RelationType == "SYNONYM")
            {
                relations.Add(new TermRelation
                {
                    RelationType = TermRelationType.Synonym,
                    SourceTerm = def.TermText,
                    TargetTerm = def.DefinitionText,
                    Confidence = def.Confidence,
                    Evidence = def.Evidence,
                    SourceBlockIds = block.BlockIds.Count > 0 ? block.BlockIds : new List<int>(),
                    Source = "definition"
                });
            }
            else if (def.RelationType == "COMPOSED_OF")
            {
                relations.Add(new TermRelation
                {
                    RelationType = TermRelationType.ComposedOf,
                    SourceTerm = def.TermText,
                    TargetTerm = def.DefinitionText,
                    Confidence = def.Confidence,
                    Evidence = def.Evidence,
                    SourceBlockIds = block.BlockIds.Count > 0 ? block.BlockIds : new List<int>(),
                    Source = "definition"
                });
            }
            else if (def.RelationType == "RELATED_TERM")
            {
                relations.Add(new TermRelation
                {
                    RelationType = TermRelationType.RelatedTerm,
                    SourceTerm = def.TermText,
                    TargetTerm = def.DefinitionText,
                    Confidence = def.Confidence,
                    Evidence = def.Evidence,
                    SourceBlockIds = block.BlockIds.Count > 0 ? block.BlockIds : new List<int>(),
                    Source = "definition"
                });
            }
        }

        // ── 2. Dependency-based relations ──
        ExtractDependencyRelations(relations, entities, block);

        // ── 2.5. Substring-based COMPOSED_OF inference (P2) ──
        ExtractSubstringCompositionRelations(relations, entities);

        // ── 3. Co-occurrence relations ──
        ExtractCoOccurrenceRelations(relations, entities, block);

        return relations;
    }

    /// <summary>
    /// Extracts relations from dependency parse: nn → COMPOSED_OF, conj → CO_OCCURS.
    /// </summary>
    private void ExtractDependencyRelations(
        List<TermRelation> relations,
        IReadOnlyList<SemanticEntity> entities,
        SemanticBlockResult block)
    {
        if (block.DepTokens.Count == 0 || block.DepEdges.Count == 0)
            return;

        var tokenById = block.DepTokens.ToDictionary(t => t.Id, t => t);

        // Build entity → tokens mapping
        var entityTokens = new Dictionary<string, List<int>>();
        foreach (var entity in entities)
        {
            entityTokens[entity.Text] = block.DepTokens
                .Where(t => entity.Span.Length >= 2 && t.Id >= entity.Span[0] && t.Id <= entity.Span[1])
                .Select(t => t.Id)
                .ToList();
        }

        foreach (var edge in block.DepEdges)
        {
            if (!tokenById.TryGetValue(edge.Child, out var childToken) ||
                !tokenById.TryGetValue(edge.Head, out var headToken))
                continue;

            // nn (noun compound) → COMPOSED_OF / PART_OF
            if (edge.Rel == "nn")
            {
                var childEntity = entities.FirstOrDefault(e => e.Text.Contains(childToken.Text) || childToken.Text.Contains(e.Text));
                var headEntity = entities.FirstOrDefault(e => e.Text.Contains(headToken.Text) || headToken.Text.Contains(e.Text));

                if (childEntity != null && headEntity != null && childEntity.Text != headEntity.Text)
                {
                    relations.Add(new TermRelation
                    {
                        RelationType = TermRelationType.ComposedOf,
                        SourceTerm = headEntity.Text,       // 故宫 COMPOSED_OF 北京
                        TargetTerm = childEntity.Text,
                        Confidence = 0.70,
                        Evidence = $"nn: {childToken.Text} → {headToken.Text}",
                        SourceBlockIds = block.BlockIds.Count > 0 ? block.BlockIds : new List<int>(),
                        Source = "dependency"
                    });
                }
            }

            // conj (conjunction) → RELATED_TERM (weaker than nn)
            if (edge.Rel == "conj")
            {
                var childEntity = entities.FirstOrDefault(e => e.Text.Contains(childToken.Text));
                var headEntity = entities.FirstOrDefault(e => e.Text.Contains(headToken.Text));

                if (childEntity != null && headEntity != null && childEntity.Text != headEntity.Text)
                {
                    relations.Add(new TermRelation
                    {
                        RelationType = TermRelationType.RelatedTerm,
                        SourceTerm = childEntity.Text,
                        TargetTerm = headEntity.Text,
                        Confidence = 0.55,
                        Evidence = $"conj: {childToken.Text} ↔ {headToken.Text}",
                        SourceBlockIds = block.BlockIds.Count > 0 ? block.BlockIds : new List<int>(),
                        Source = "dependency"
                    });
                }
            }
        }
    }

    /// <summary>
    /// Extracts weak co-occurrence relations for entities appearing in the same block.
    /// </summary>
    private void ExtractCoOccurrenceRelations(
        List<TermRelation> relations,
        IReadOnlyList<SemanticEntity> entities,
        SemanticBlockResult block)
    {
        if (entities.Count < 2) return;

        // Only create co-occurrence for entity pairs that don't already have a stronger relation
        var existingPairs = new HashSet<string>(relations
            .Where(r => r.Source == "dependency" || r.Source == "definition")
            .Select(r => $"{r.SourceTerm}|{r.TargetTerm}"));

        for (int i = 0; i < entities.Count; i++)
        {
            for (int j = i + 1; j < entities.Count; j++)
            {
                var a = entities[i].Text;
                var b = entities[j].Text;
                if (a == b) continue;

                var pairKey = $"{a}|{b}";
                var revKey = $"{b}|{a}";
                if (existingPairs.Contains(pairKey) || existingPairs.Contains(revKey))
                    continue;

                relations.Add(new TermRelation
                {
                    RelationType = TermRelationType.CoOccurs,
                    SourceTerm = a,
                    TargetTerm = b,
                    Confidence = 0.30,
                    Evidence = $"Co-occurrence in block",
                    SourceBlockIds = block.BlockIds.Count > 0 ? block.BlockIds : new List<int>(),
                    Source = "cooccur"
                });
            }
        }
    }

    /// <summary>
    /// P2: Infers COMPOSED_OF relations when one entity text is a substring of another.
    /// E.g., "铝" is substring of "铝合金" → 铝 COMPOSED_OF 铝合金.
    /// Only triggers when the substring is >= 1 CJK character and the parent is >= 3 chars.
    /// </summary>
    private void ExtractSubstringCompositionRelations(
        List<TermRelation> relations,
        IReadOnlyList<SemanticEntity> entities)
    {
        if (entities.Count < 2) return;

        var entityTexts = entities.Select(e => e.Text).Distinct().ToList();

        for (int i = 0; i < entityTexts.Count; i++)
        {
            for (int j = 0; j < entityTexts.Count; j++)
            {
                if (i == j) continue;
                var shorter = entityTexts[i];
                var longer = entityTexts[j];

                // Shorter must be at least 1 char, longer at least 3
                if (shorter.Length < 1 || longer.Length < 3) continue;
                // Shorter must be a proper substring (not equal)
                if (!longer.Contains(shorter) || shorter == longer) continue;
                // Avoid trivial matches (e.g., "的" in "好的")
                if (shorter.Length == 1)
                {
                    var c = shorter[0];
                    // Only allow single-char if it's a CJK ideograph (not punctuation)
                    if (c < 0x4E00 || c > 0x9FFF) continue;
                }

                // Don't add if a stronger relation already exists
                var alreadyHas = relations.Any(r =>
                    (r.SourceTerm == longer && r.TargetTerm == shorter) ||
                    (r.SourceTerm == shorter && r.TargetTerm == longer));
                if (alreadyHas) continue;

                relations.Add(new TermRelation
                {
                    RelationType = TermRelationType.ComposedOf,
                    SourceTerm = longer,   // 铝合金 COMPOSED_OF 铝
                    TargetTerm = shorter,
                    Confidence = 0.65,      // Medium confidence — substring is a strong signal
                    Evidence = $"substring: {shorter} ⊂ {longer}",
                    SourceBlockIds = new List<int>(),
                    Source = "substring_inference"
                });
            }
        }
    }

    /// <summary>
    /// Resolves cross-block relations: merges relations from multiple blocks and normalizes.
    /// </summary>
    public List<TermRelation> ResolveCrossBlock(
        List<TermRelation> allRelations,
        IReadOnlyList<SemanticBlockResult> allBlocks)
    {
        // Deduplicate by (source, target, type)
        var seen = new HashSet<string>();
        var resolved = new List<TermRelation>();

        foreach (var rel in allRelations.OrderByDescending(r => r.Confidence))
        {
            var key = $"{rel.SourceTerm}|{rel.TargetTerm}|{rel.RelationType}";
            if (seen.Contains(key)) continue;
            seen.Add(key);

            // Collect all block IDs where this relation appears
            var allBlockIds = allRelations
                .Where(r => r.SourceTerm == rel.SourceTerm && r.TargetTerm == rel.TargetTerm && r.RelationType == rel.RelationType)
                .SelectMany(r => r.SourceBlockIds)
                .Distinct()
                .ToList();

            rel.SourceBlockIds = allBlockIds;
            resolved.Add(rel);
        }

        return resolved;
    }
}
