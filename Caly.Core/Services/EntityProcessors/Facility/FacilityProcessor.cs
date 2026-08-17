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

namespace Caly.Core.Services.EntityProcessors.Facility;

/// <summary>
/// Entity processor for FACILITY entities (stations, airports, bridges, etc.).
/// Reuses GeoJSON lookup for geocoding when available.
/// </summary>
public sealed class FacilityProcessor : IEntityProcessor
{
    public string EntityType => "FACILITY";
    public IReadOnlySet<string> DependsOn { get; } = new HashSet<string>();
    public bool SupportsCrossBlockResolution => false;

    public EntityValidationResult Validate(SemanticEntity entity, SemanticBlockResult block)
    {
        var text = entity.Text;
        if (string.IsNullOrWhiteSpace(text) || text.Length <= 1)
            return EntityValidationResult.Invalid("Too short to be a facility name");

        entity.EnrichedMetadata["FACILITY:facility_type"] = new Dictionary<string, object>
        {
            ["type"] = "unknown",
        };

        return EntityValidationResult.Valid("Valid facility name");
    }

    public Task<EntityEnrichmentResult> EnrichAsync(
        SemanticEntity entity,
        EntityContext context,
        CancellationToken ct)
    {
        // Try GeoJSON lookup first (facilities may have been geocoded)
        var lookup = context.GeoJsonLookup;
        if (lookup != null && lookup.TryGetValue(entity.Text, out var feature) && feature.Status == "success")
        {
            var canonicalName = ExtractCanonicalName(feature.DisplayName);
            return Task.FromResult(new EntityEnrichmentResult
            {
                Source = "locations.geojson",
                EnrichedText = canonicalName,
                ExternalId = !string.IsNullOrEmpty(feature.OsmType) && feature.OsmId > 0
                    ? $"{feature.OsmType}/{feature.OsmId}"
                    : null,
                Confidence = feature.Confidence,
                Metadata = new Dictionary<string, object>
                {
                    ["latitude"] = feature.Latitude ?? 0.0,
                    ["longitude"] = feature.Longitude ?? 0.0,
                    ["display_name"] = feature.DisplayName,
                    ["canonical_name"] = canonicalName,
                    ["facility_type"] = feature.Type ?? "unknown",
                }
            });
        }

        // No geocoding data available — return validation-only result
        return Task.FromResult(new EntityEnrichmentResult
        {
            Source = "FacilityValidator",
            EnrichedText = entity.Text,
            Confidence = 0.60,
            Metadata = new Dictionary<string, object>
            {
                ["facility_type"] = "unknown",
                ["original_text"] = entity.Text,
            }
        });
    }

    public IReadOnlyList<EntityRelation> ExtractRelations(
        IReadOnlyList<SemanticEntity> entities,
        SemanticBlockResult block)
    {
        return Array.Empty<EntityRelation>();
    }

    public IReadOnlyList<EntityRelation> ResolveCrossBlock(
        IReadOnlyList<(SemanticEntity Entity, SemanticBlockResult Block)> allEntities,
        IReadOnlyList<SemanticBlockResult> allBlocks)
    {
        return Array.Empty<EntityRelation>();
    }

    private static string ExtractCanonicalName(string displayName)
    {
        if (string.IsNullOrEmpty(displayName))
            return displayName;

        var parts = displayName.Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (!string.IsNullOrEmpty(trimmed))
                return trimmed;
        }
        return displayName.Trim();
    }
}
