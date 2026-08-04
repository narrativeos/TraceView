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
using System.Threading;
using System.Threading.Tasks;
using Caly.Core.Models;
using Caly.Core.Models.EntityProcessors;

namespace Caly.Core.Services.EntityProcessors;

/// <summary>
/// Pluggable processor for a specific entity type (Location, Person, Organization, Date).
///
/// Each processor encapsulates:
/// 1. Pattern-based validation (filter out clearly invalid entities).
/// 2. External enrichment (geo API, Wikidata, temporal normalization).
/// 3. Intra-type relation extraction (dependency parse → typed relations).
/// 4. Cross-block resolution (coreference, timeline ordering).
///
/// Processors declare their dependencies on other entity types via <see cref="DependsOn"/>,
/// and the <see cref="EntityProcessorPipeline"/> schedules them in topological order.
/// </summary>
public interface IEntityProcessor
{
    /// <summary>
    /// The entity category this processor handles (e.g., "LOCATION", "PERSON", "ORGANIZATION", "DATE").
    /// Must match the <see cref="SemanticEntity.Category"/> value from the NLP pipeline.
    /// </summary>
    string EntityType { get; }

    /// <summary>
    /// Set of entity types that must be processed before this one.
    /// Used by <see cref="EntityProcessorPipeline"/> for topological sort.
    /// Return an empty set if this processor has no dependencies.
    /// </summary>
    IReadOnlySet<string> DependsOn { get; }

    /// <summary>
    /// Whether this processor needs cross-block resolution after all blocks are processed.
    /// Examples: LOCATION coreference chains, Temporal timeline ordering.
    /// </summary>
    bool SupportsCrossBlockResolution { get; }

    /// <summary>
    /// Pattern-based validation — filters out entities that are clearly not valid
    /// for this entity type before calling external APIs.
    /// </summary>
    /// <param name="entity">The raw entity from the NLP pipeline.</param>
    /// <param name="block">The block context (content, tokens, dep edges).</param>
    /// <returns>Valid, Invalid, or Ambiguous result.</returns>
    EntityValidationResult Validate(SemanticEntity entity, SemanticBlockResult block);

    /// <summary>
    /// Enrich the entity with external/authoritative metadata.
    /// Called after validation passes and after all DependsOn entity types are processed.
    /// </summary>
    /// <param name="entity">The entity to enrich.</param>
    /// <param name="context">Context including already-enriched parent entities and relations.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Enrichment result with canonical name, external ID, and type-specific metadata.</returns>
    Task<EntityEnrichmentResult> EnrichAsync(
        SemanticEntity entity,
        EntityContext context,
        CancellationToken ct);

    /// <summary>
    /// Extract intra-type relations from dependency parse within a single block.
    /// Called after enrichment for all entities of this type in the block.
    /// </summary>
    /// <param name="entities">All entities of this type in the current block.</param>
    /// <param name="block">The block containing dependency parse data.</param>
    /// <returns>Typed relations between entities of this type.</returns>
    IReadOnlyList<EntityRelation> ExtractRelations(
        IReadOnlyList<SemanticEntity> entities,
        SemanticBlockResult block);

    /// <summary>
    /// Perform cross-block resolution for this entity type.
    /// Only called if <see cref="SupportsCrossBlockResolution"/> is true,
    /// after all blocks have been processed individually.
    /// </summary>
    /// <param name="allEntities">All entities of this type across all blocks.</param>
    /// <param name="allBlocks">All blocks in the document.</param>
    /// <returns>Cross-block relations (e.g., coreference chains, timeline ordering).</returns>
    IReadOnlyList<EntityRelation> ResolveCrossBlock(
        IReadOnlyList<(SemanticEntity Entity, SemanticBlockResult Block)> allEntities,
        IReadOnlyList<SemanticBlockResult> allBlocks);
}
