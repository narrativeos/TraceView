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
using Caly.Core.Models.EntityProcessors;

namespace Caly.Core.Models;

/// <summary>
/// Builds an <see cref="EnrichedResultFile"/> from the pipeline-processed entities
/// and their <see cref="EnrichedMetadata"/> dictionaries.
/// </summary>
public static class EnrichedResultBuilder
{
    /// <summary>
    /// Build enriched result from a semantic result file that has been processed
    /// by the EntityProcessorPipeline (entities now have EnrichedMetadata populated).
    /// </summary>
    public static EnrichedResultFile Build(
        SemanticResultFile semanticResult,
        List<EntityRelation> crossBlockRelations,
        string outputDir)
    {
        var enriched = new EnrichedResultFile
        {
            Version = "1.0",
            Timestamp = DateTime.UtcNow.ToString("O"),
            SourceSemanticResult = System.IO.Path.Combine(outputDir, "semantic_result.json"),
        };

        foreach (var block in semanticResult.Blocks)
        {
            // Use SourceBlockIds (string) as the primary identifier since BlockIds is often empty.
            // Try to parse as integers first; fall back to using index-based IDs.
            var blockIds = new List<int>();
            foreach (var id in block.SourceBlockIds)
            {
                // SourceBlockIds are strings like "0", "1", etc.
                if (int.TryParse(id, out var numId))
                    blockIds.Add(numId);
            }
            // If SourceBlockIds didn't yield integers, use BlockIds
            if (blockIds.Count == 0 && block.BlockIds.Count > 0)
                blockIds = block.BlockIds;

            var enrichedBlock = new EnrichedBlockResult
            {
                BlockIds = blockIds
            };

            foreach (var entity in block.Entities)
            {
                var entry = BuildEntityEntry(entity);
                if (entry != null)
                    enrichedBlock.Entities.Add(entry);
            }

            // Collect relations from block-level enriched metadata
            CollectBlockRelations(block, enrichedBlock);

            enriched.Blocks.Add(enrichedBlock);
        }

        // Add cross-block relations (one entry per relation, tagged by entity category)
        foreach (var rel in crossBlockRelations)
        {
            enriched.CrossBlockRelations.Add(new EnrichedRelation
            {
                RelationType = rel.RelationType,
                SourceEntityId = rel.SourceEntityId,
                TargetEntityId = rel.TargetEntityId,
                EntityCategory = "CROSS_BLOCK",
                Confidence = rel.Confidence,
                Evidence = rel.Evidence ?? ""
            });
        }

        return enriched;
    }

    private static EnrichedEntityEntry? BuildEntityEntry(SemanticEntity entity)
    {
        var entry = new EnrichedEntityEntry
        {
            Id = entity.Id,
            Text = entity.Text,
            Category = entity.Category,
            Normalized = entity.Normalized ?? "",
            ValidationStatus = GetValidationField(entity, "is_valid") is bool v
                ? (v ? "valid" : "invalid")
                : "unknown",
            ValidationReason = GetValidationField(entity, "reason") as string ?? "",
            EnrichedText = GetEnrichmentField(entity, "enriched_text") as string ?? entity.Text,
            ExternalId = GetEnrichmentField(entity, "external_id") as string ?? "",
            EnrichmentSource = GetEnrichmentField(entity, "source") as string ?? "",
            EnrichmentConfidence = GetEnrichmentField(entity, "confidence") is double d ? d : 0.0
        };

        // Flatten type-specific metadata from enrichment
        var enrichment = GetEnrichmentDict(entity);
        if (enrichment != null)
        {
            // Extract nested metadata dict
            if (enrichment.TryGetValue("metadata", out var metaObj) &&
                metaObj is Dictionary<string, object> metaDict)
            {
                foreach (var kvp in metaDict)
                    entry.Metadata.Add($"{kvp.Key}={kvp.Value}");
            }

            // Add top-level enrichment fields that aren't in metadata
            foreach (var kvp in enrichment)
            {
                if (kvp.Key is "metadata" or "enriched_text" or "external_id" or "source" or "confidence")
                    continue;
                if (kvp.Value is Dictionary<string, object>)
                    continue;
                entry.Metadata.Add($"{kvp.Key}={kvp.Value}");
            }
        }

        return entry;
    }

    private static void CollectBlockRelations(SemanticBlockResult block, EnrichedBlockResult enrichedBlock)
    {
        // Collect LOCATION relations (stored in dedicated list)
        foreach (var lr in block.LocationRelations)
        {
            enrichedBlock.Relations.Add(new EnrichedRelation
            {
                RelationType = lr.RelationType.ToString(),
                SourceEntityId = lr.SourceLocation,
                TargetEntityId = lr.TargetLocation,
                EntityCategory = "LOCATION",
                Confidence = lr.Confidence,
                Evidence = lr.Evidence ?? ""
            });
        }

        // Collect relations from other entity types (stored in EnrichedMetadata)
        foreach (var kvp in block.EnrichedMetadata)
        {
            if (!kvp.Key.EndsWith(":relations")) continue;
            var category = kvp.Key.Split(':')[0];

            if (kvp.Value is List<Dictionary<string, object>> relList)
            {
                foreach (var relDict in relList)
                {
                    enrichedBlock.Relations.Add(new EnrichedRelation
                    {
                        RelationType = relDict.TryGetValue("relation_type", out var rt) ? rt?.ToString() ?? "" : "",
                        SourceEntityId = relDict.TryGetValue("source_entity_id", out var sid) ? sid?.ToString() ?? "" : "",
                        TargetEntityId = relDict.TryGetValue("target_entity_id", out var tid) ? tid?.ToString() ?? "" : "",
                        EntityCategory = category,
                        Confidence = relDict.TryGetValue("confidence", out var conf) && conf is double cd ? cd : 0.0,
                        Evidence = relDict.TryGetValue("evidence", out var ev) ? ev?.ToString() ?? "" : ""
                    });
                }
            }
        }
    }

    private static object? GetValidationField(SemanticEntity entity, string field)
    {
        var key = $"{entity.Category}:validation";
        if (entity.EnrichedMetadata.TryGetValue(key, out var v) &&
            v is Dictionary<string, object> dict &&
            dict.TryGetValue(field, out var val))
            return val;
        return null;
    }

    private static object? GetEnrichmentField(SemanticEntity entity, string field)
    {
        var key = $"{entity.Category}:enrichment";
        if (entity.EnrichedMetadata.TryGetValue(key, out var v) &&
            v is Dictionary<string, object> dict &&
            dict.TryGetValue(field, out var val))
            return val;
        return null;
    }

    private static Dictionary<string, object>? GetEnrichmentDict(SemanticEntity entity)
    {
        var key = $"{entity.Category}:enrichment";
        return entity.EnrichedMetadata.TryGetValue(key, out var v) &&
               v is Dictionary<string, object> d ? d : null;
    }
}
