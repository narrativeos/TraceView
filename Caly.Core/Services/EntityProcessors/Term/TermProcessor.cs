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
using System.Threading;
using System.Threading.Tasks;
using Caly.Core.Models;
using Caly.Core.Models.EntityProcessors;

namespace Caly.Core.Services.EntityProcessors.Term;

/// <summary>
/// Entity processor for domain-specific terminology.
///
/// Validates entities as potential terms, enriches them with definitions and context,
/// and extracts term-specific relations (HYPERNYM, HYPONYM, SYNONYM, COMPOSED_OF, etc.).
///
/// Depends on all other entity types being processed first (LOCATION, PERSON, ORGANIZATION, DATE),
/// since terms may reference or be derived from enriched named entities.
/// </summary>
public class TermProcessor : IEntityProcessor
{
    private readonly TermValidator _validator = new();
    private readonly TermEnricher _enricher = new();
    private readonly TermRelationExtractor _relationExtractor = new();

    // Cache for cross-block definition lookup
    private List<TermDefinition>? _allDefinitions;
    // Cache for entity frequency across blocks
    private Dictionary<string, int>? _entityFrequency;

    /// <summary>Handles all entity categories that could be domain terms.</summary>
    public string EntityType => "TERM";

    /// <summary>Depends on all named entity types being processed first.</summary>
    public IReadOnlySet<string> DependsOn { get; } = new HashSet<string>
    {
        "LOCATION", "PERSON", "ORGANIZATION", "DATE"
    };

    /// <summary>Supports cross-block resolution for term relations.</summary>
    public bool SupportsCrossBlockResolution => true;

    /// <summary>
    /// Pattern-based validation: determines if an entity qualifies as a domain term.
    /// </summary>
    public EntityValidationResult Validate(SemanticEntity entity, SemanticBlockResult block)
    {
        var defCount = block.TermDefinitions.Count(d => d.TermText == entity.Text || entity.Text.Contains(d.TermText) || d.TermText.Contains(entity.Text));
        var frequency = _entityFrequency?.TryGetValue(entity.Text, out var freq) == true ? freq : 1;

        return _validator.Validate(entity, defCount, frequency);
    }

    /// <summary>
    /// Enriches a term entity with definitions, context, and normalization.
    /// </summary>
    public async Task<EntityEnrichmentResult> EnrichAsync(
        SemanticEntity entity,
        EntityContext context,
        CancellationToken ct)
    {
        return await _enricher.EnrichAsync(entity, context.Block, _allDefinitions ?? new List<TermDefinition>(), ct);
    }

    /// <summary>
    /// Extracts term relations from within a block.
    /// </summary>
    public IReadOnlyList<Models.EntityProcessors.EntityRelation> ExtractRelations(
        IReadOnlyList<SemanticEntity> entities,
        SemanticBlockResult block)
    {
        var termRelations = _relationExtractor.ExtractRelations(
            entities, block, _allDefinitions ?? new List<TermDefinition>());

        // Convert TermRelation to EntityRelation for pipeline compatibility
        return termRelations.Select(tr => new Models.EntityProcessors.EntityRelation
        {
            RelationType = tr.RelationType,
            SourceEntityId = tr.SourceTerm,
            TargetEntityId = tr.TargetTerm,
            Confidence = tr.Confidence,
            Evidence = tr.Evidence
        }).ToList();
    }

    /// <summary>
    /// Cross-block resolution for term relations.
    /// </summary>
    public IReadOnlyList<Models.EntityProcessors.EntityRelation> ResolveCrossBlock(
        IReadOnlyList<(SemanticEntity Entity, SemanticBlockResult Block)> allEntities,
        IReadOnlyList<SemanticBlockResult> allBlocks)
    {
        // Collect all definitions
        _allDefinitions = allBlocks.SelectMany(b => b.TermDefinitions).ToList();

        // Compute entity frequency across all blocks
        _entityFrequency = new Dictionary<string, int>();
        foreach (var (entity, _) in allEntities)
        {
            var key = entity.Text;
            if (_entityFrequency.ContainsKey(key))
                _entityFrequency[key]++;
            else
                _entityFrequency[key] = 1;

            // Also count normalized form
            var normalized = TermEnricher.NormalizeTermText(entity.Text);
            if (normalized != key)
            {
                if (_entityFrequency.ContainsKey(normalized))
                    _entityFrequency[normalized]++;
                else
                    _entityFrequency[normalized] = 1;
            }
        }

        // Collect all intra-block relations
        var allTermRelations = new List<Caly.Core.Models.TermRelation>();
        foreach (var (_, block) in allEntities)
        {
            var blockEntities = allEntities
                .Where(e => e.Block == block)
                .Select(e => e.Entity)
                .ToList();

            var blockRelations = _relationExtractor.ExtractRelations(
                blockEntities, block, _allDefinitions);
            allTermRelations.AddRange(blockRelations);
        }

        // Cross-block resolution
        var resolved = _relationExtractor.ResolveCrossBlock(allTermRelations, allBlocks);

        return resolved.Select(tr => new Models.EntityProcessors.EntityRelation
        {
            RelationType = tr.RelationType,
            SourceEntityId = tr.SourceTerm,
            TargetEntityId = tr.TargetTerm,
            Confidence = tr.Confidence,
            Evidence = tr.Evidence
        }).ToList();
    }
}
