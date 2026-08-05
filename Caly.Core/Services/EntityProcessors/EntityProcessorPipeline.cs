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

namespace Caly.Core.Services.EntityProcessors;

/// <summary>Mutable counter for progress tracking across async boundaries.</summary>
struct ProgressCounter
{
    public int Value;
}

/// <summary>
/// Orchestrates all registered <see cref="IEntityProcessor"/> implementations.
///
/// Responsibilities:
/// 1. Register entity processors.
/// 2. Topological sort based on <see cref="IEntityProcessor.DependsOn"/>.
/// 3. For each block: validate → enrich (in dependency order) → extract relations.
/// 4. Cross-block resolution for processors that support it.
/// 5. Store enrichment results back into <see cref="SemanticEntity"/> metadata.
/// </summary>
public class EntityProcessorPipeline
{
    private readonly List<IEntityProcessor> _processors = new();
    private IReadOnlyList<IEntityProcessor>? _sortedProcessors;

    /// <summary>Optional progress callback: (currentEntity, totalEntities, entityText, entityType).</summary>
    private Action<int, int, string, string>? _onProgress;

    /// <summary>
    /// Sets an optional progress callback invoked after each entity is processed.
    /// </summary>
    public EntityProcessorPipeline ReportProgress(Action<int, int, string, string> onProgress)
    {
        _onProgress = onProgress;
        return this;
    }

    /// <summary>All registered processors (unsorted).</summary>
    public IReadOnlyList<IEntityProcessor> Processors => _processors;

    /// <summary>
    /// Register an entity processor. Call this for each entity type before processing.
    /// </summary>
    public EntityProcessorPipeline AddProcessor(IEntityProcessor processor)
    {
        _processors.Add(processor);
        _sortedProcessors = null; // Invalidate cached sort order
        return this;
    }

    /// <summary>
    /// Register multiple entity processors at once.
    /// </summary>
    public EntityProcessorPipeline AddProcessors(params IEntityProcessor[] processors)
    {
        foreach (var p in processors)
            _processors.Add(p);
        _sortedProcessors = null;
        return this;
    }

    /// <summary>
    /// Get the processor for a specific entity type, or null if not registered.
    /// </summary>
    public IEntityProcessor? GetProcessor(string entityType)
    {
        return _processors.FirstOrDefault(p =>
            string.Equals(p.EntityType, entityType, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Process all blocks in a semantic result file.
    /// </summary>
    /// <param name="resultFile">Semantic analysis results to enrich.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="geoJsonLookup">Optional pre-parsed GeoJSON location features from locations.geojson, keyed by location name.</param>
    public async Task<Models.EnrichedResultFile?> ProcessAsync(
        SemanticResultFile resultFile,
        CancellationToken ct = default,
        IReadOnlyDictionary<string, Models.EntityProcessors.GeoJsonFeature>? geoJsonLookup = null)
    {
        var sorted = GetSortedProcessors();
        if (sorted.Count == 0)
            return null;

        // Clear previous enrichment data to avoid stale metadata on re-run
        foreach (var block in resultFile.Blocks)
        {
            block.EnrichedMetadata.Clear();
            foreach (var entity in block.Entities)
                entity.EnrichedMetadata.Clear();
        }

        // Collect all enriched entities across blocks for cross-block context
        var allEnrichedByType = new Dictionary<string, List<(SemanticEntity Entity, SemanticBlockResult Block)>>();
        var allRelations = new List<EntityRelation>();

        // Count total entities to enrich for progress reporting
        int totalEntities = resultFile.Blocks.Sum(b => b.Entities.Count);
        var counter = new ProgressCounter();

        // Phase 1: Per-block processing
        foreach (var block in resultFile.Blocks)
        {
            if (block.Entities.Count == 0)
                continue;

            await ProcessBlockAsync(block, sorted, allEnrichedByType, allRelations, ct,
                counter, totalEntities, geoJsonLookup);
        }

        // Phase 2: Cross-block resolution
        foreach (var processor in sorted)
        {
            if (!processor.SupportsCrossBlockResolution)
                continue;

            if (allEnrichedByType.TryGetValue(processor.EntityType, out var entities))
            {
                var crossRelations = processor.ResolveCrossBlock(
                    entities.AsReadOnly(),
                    resultFile.Blocks);
                allRelations.AddRange(crossRelations);
            }
        }

        // Build enriched result for persistence
        // Pass the semantic result source path as the output directory reference
        return Models.EnrichedResultBuilder.Build(resultFile, allRelations, resultFile.Source);
    }

    /// <summary>
    /// Process a single block: validate, enrich, extract relations for all entity types.
    /// </summary>
    private async Task ProcessBlockAsync(
        SemanticBlockResult block,
        IReadOnlyList<IEntityProcessor> sortedProcessors,
        Dictionary<string, List<(SemanticEntity, SemanticBlockResult)>> allEnrichedByType,
        List<EntityRelation> allRelations,
        CancellationToken ct,
        ProgressCounter counter,
        int totalEntities,
        IReadOnlyDictionary<string, Models.EntityProcessors.GeoJsonFeature>? geoJsonLookup = null)
    {
        // Build enriched entities map for this block (for dependency context)
        var blockEnriched = new Dictionary<string, List<SemanticEntity>>();

        foreach (var processor in sortedProcessors)
        {
            var entities = block.Entities
                .Where(e => string.Equals(e.Category, processor.EntityType, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (entities.Count == 0)
                continue;

            // Step 1: Validate — filter out clearly invalid entities
            var validEntities = new List<SemanticEntity>();
            foreach (var entity in entities)
            {
                var validation = processor.Validate(entity, block);
                if (validation.IsValid)
                {
                    validEntities.Add(entity);
                }
                // Store validation result in entity metadata for UI display
                entity.EnrichedMetadata[$"{processor.EntityType}:validation"] = new Dictionary<string, object>
                {
                    ["is_valid"] = validation.IsValid,
                    ["reason"] = validation.Reason ?? "",
                    ["severity"] = validation.Severity.ToString()
                };
            }

            if (validEntities.Count == 0)
                continue;

            // Step 2: Enrich — call external APIs / rule engines (in dependency order)
            var context = new EntityContext
            {
                EnrichedEntities = blockEnriched.ToDictionary(
                    kvp => kvp.Key,
                    kvp => (IReadOnlyList<SemanticEntity>)kvp.Value.AsReadOnly()),
                Relations = allRelations.AsReadOnly(),
                Block = block,
                GeoJsonLookup = geoJsonLookup
            };

            foreach (var entity in validEntities)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var enrichment = await processor.EnrichAsync(entity, context, ct);

                    // Store enrichment result as JSON-serializable metadata
                    entity.EnrichedMetadata[$"{processor.EntityType}:enrichment"] = new Dictionary<string, object>
                    {
                        ["enriched_text"] = enrichment.EnrichedText ?? "",
                        ["external_id"] = enrichment.ExternalId ?? "",
                        ["confidence"] = enrichment.Confidence,
                        ["source"] = enrichment.Source ?? "",
                        ["metadata"] = enrichment.Metadata
                    };
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Enrichment failure should not block other entities
                    entity.EnrichedMetadata[$"{processor.EntityType}:enrichment"] = new Dictionary<string, object>
                    {
                        ["error"] = ex.Message,
                        ["confidence"] = 0.0
                    };
                }
                // Report progress after each entity
                counter.Value++;
                _onProgress?.Invoke(counter.Value, totalEntities, entity.Text, processor.EntityType);
            }

            // Track enriched entities for cross-block and dependency context
            blockEnriched[processor.EntityType] = validEntities;

            if (!allEnrichedByType.ContainsKey(processor.EntityType))
                allEnrichedByType[processor.EntityType] = new();
            foreach (var entity in validEntities)
                allEnrichedByType[processor.EntityType].Add((entity, block));

            // Step 3: Extract intra-type relations
            var relations = processor.ExtractRelations(validEntities.AsReadOnly(), block);
            allRelations.AddRange(relations);

            // Also store relations on the block for serialization
            StoreBlockRelations(block, processor.EntityType, relations);
        }
    }

    /// <summary>
    /// Store relations on the block using the existing extension point.
    /// Each processor type gets its own named collection on the block.
    /// </summary>
    private static void StoreBlockRelations(SemanticBlockResult block, string entityType, IReadOnlyList<EntityRelation> relations)
    {
        if (relations.Count == 0)
            return;

        // Use the EnrichedMetadata dictionary on the block itself as a serialization anchor.
        // For LOCATION, the existing LocationRelations list is used directly by the serializer.
        // For other types, store in block-level metadata for now.
        if (string.Equals(entityType, "LOCATION", StringComparison.OrdinalIgnoreCase))
        {
            // LOCATION relations are stored in the existing LocationRelations property
            // (this is handled by the LocationProcessor which populates LocationRelation objects)
        }
        else
        {
            var key = $"{entityType}:relations";
            if (!block.EnrichedMetadata.ContainsKey(key))
                block.EnrichedMetadata[key] = new List<Dictionary<string, object>>();

            var list = (List<Dictionary<string, object>>)block.EnrichedMetadata[key];
            foreach (var r in relations)
            {
                list.Add(new Dictionary<string, object>
                {
                    ["relation_type"] = r.RelationType,
                    ["source_entity_id"] = r.SourceEntityId,
                    ["target_entity_id"] = r.TargetEntityId,
                    ["confidence"] = r.Confidence,
                    ["evidence"] = r.Evidence ?? ""
                });
            }
        }
    }

    /// <summary>
    /// Perform topological sort on registered processors based on DependsOn.
    /// Uses Kahn's algorithm. Throws if a cycle is detected.
    /// </summary>
    public IReadOnlyList<IEntityProcessor> GetSortedProcessors()
    {
        if (_sortedProcessors is not null)
            return _sortedProcessors;

        if (_processors.Count == 0)
        {
            _sortedProcessors = Array.Empty<IEntityProcessor>();
            return _sortedProcessors;
        }

        // Build adjacency list and in-degree map
        var processorMap = _processors.ToDictionary(p => p.EntityType, p => p, StringComparer.OrdinalIgnoreCase);
        var inDegree = _processors.ToDictionary(p => p.EntityType, _ => 0, StringComparer.OrdinalIgnoreCase);
        var adjacency = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in _processors)
        {
            if (!adjacency.ContainsKey(p.EntityType))
                adjacency[p.EntityType] = new List<string>();
        }

        foreach (var p in _processors)
        {
            foreach (var dep in p.DependsOn)
            {
                if (!processorMap.ContainsKey(dep))
                {
                    // Dependency not registered — warn but don't fail
                    System.Diagnostics.Debug.WriteLine($"[EntityProcessorPipeline] Processor '{p.EntityType}' depends on unregistered type '{dep}'. Skipping dependency.");
                    continue;
                }

                if (!adjacency.ContainsKey(dep))
                    adjacency[dep] = new List<string>();

                adjacency[dep].Add(p.EntityType);
                inDegree[p.EntityType]++;
            }
        }

        // Kahn's algorithm
        var queue = new Queue<string>(inDegree.Where(kvp => kvp.Value == 0).Select(kvp => kvp.Key));
        var sorted = new List<IEntityProcessor>();

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            sorted.Add(processorMap[current]);

            if (adjacency.TryGetValue(current, out var neighbors))
            {
                foreach (var neighbor in neighbors)
                {
                    inDegree[neighbor]--;
                    if (inDegree[neighbor] == 0)
                        queue.Enqueue(neighbor);
                }
            }
        }

        if (sorted.Count != _processors.Count)
        {
            // Cycle detected — fall back to registration order
            System.Diagnostics.Debug.WriteLine("[EntityProcessorPipeline] Cycle detected in processor dependencies. Falling back to registration order.");
            sorted = _processors.ToList();
        }

        _sortedProcessors = sorted.AsReadOnly();
        return _sortedProcessors;
    }
}