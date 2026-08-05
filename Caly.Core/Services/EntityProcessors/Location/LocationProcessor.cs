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

namespace Caly.Core.Services.EntityProcessors.Location;

/// <summary>
/// Location entity processor — implements <see cref="IEntityProcessor"/> for the "LOCATION" entity type.
///
/// Uses pre-geocoded data from locations.geojson (via EntityContext.GeoJsonLookup)
/// instead of re-calling the Nominatim API. This avoids redundant geocoding and ensures
/// consistency with the Geo column display.
/// </summary>
public sealed class LocationProcessor : IEntityProcessor
{
    /// <inheritdoc/>
    public string EntityType => "LOCATION";

    /// <inheritdoc/>
    public IReadOnlySet<string> DependsOn { get; } = new HashSet<string>();

    /// <inheritdoc/>
    public bool SupportsCrossBlockResolution => true;

    /// <inheritdoc/>
    public EntityValidationResult Validate(SemanticEntity entity, SemanticBlockResult block)
    {
        var (result, reason) = Utilities.LocationValidator.Validate(entity.Text);
        var locationType = Utilities.LocationValidator.ClassifyType(entity.Text);

        // Store location type in entity metadata
        entity.EnrichedMetadata["LOCATION:location_type"] = new Dictionary<string, object>
        {
            ["type"] = locationType.ToString(),
            ["display"] = Utilities.LocationValidator.ToDisplay(locationType)
        };

        return result switch
        {
            Utilities.LocationValidationResult.Valid => EntityValidationResult.Valid(reason),
            Utilities.LocationValidationResult.Ambiguous => EntityValidationResult.Ambiguous(reason ?? "Ambiguous location name"),
            Utilities.LocationValidationResult.Invalid => EntityValidationResult.Invalid(reason ?? "Not a valid location name"),
            _ => EntityValidationResult.Invalid("Unknown validation result")
        };
    }

    /// <inheritdoc/>
    public async Task<EntityEnrichmentResult> EnrichAsync(
        SemanticEntity entity,
        EntityContext context,
        CancellationToken ct)
    {
        var result = new EntityEnrichmentResult { Source = "locations.geojson" };

        try
        {
            // Look up geocoded data from pre-parsed locations.geojson
            var lookup = context.GeoJsonLookup;
            if (lookup is null || !lookup.TryGetValue(entity.Text, out var feature))
            {
                // No GeoJSON data available or no match for this location
                result.Confidence = 0.0;
                result.Metadata["error"] = lookup is null
                    ? "locations.geojson not available — run Geo generation first"
                    : $"No geocoding data for '{entity.Text}' in locations.geojson";
                return result;
            }

            if (feature.Status != "success")
            {
                // Geocoding was attempted but failed
                result.Confidence = 0.0;
                result.Metadata["error"] = $"Geocoding failed: status={feature.Status}";
                return result;
            }

            // Build enrichment result from GeoJSON feature data
            result.EnrichedText = feature.DisplayName;
            result.ExternalId = !string.IsNullOrEmpty(feature.OsmType) && feature.OsmId > 0
                ? $"{feature.OsmType}/{feature.OsmId}"
                : null;
            result.Confidence = feature.Confidence;

            result.Metadata = new Dictionary<string, object>
            {
                ["latitude"] = feature.Latitude ?? 0.0,
                ["longitude"] = feature.Longitude ?? 0.0,
                ["osm_type"] = feature.OsmType,
                ["osm_id"] = feature.OsmId,
                ["category"] = feature.Category,
                ["type"] = feature.Type,
                ["addresstype"] = feature.Addresstype,
                ["place_rank"] = feature.PlaceRank,
                ["importance"] = feature.Importance,
                ["display_name"] = feature.DisplayName,
                ["match_type"] = feature.MatchType,
                ["query_text"] = feature.QueryText
            };

            if (feature.Bbox is { Length: 4 })
            {
                result.Metadata["bbox_min_lat"] = feature.Bbox[0];
                result.Metadata["bbox_max_lat"] = feature.Bbox[1];
                result.Metadata["bbox_min_lon"] = feature.Bbox[2];
                result.Metadata["bbox_max_lon"] = feature.Bbox[3];
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            result.Confidence = 0.0;
            result.Metadata["error"] = ex.Message;
        }

        return result;
    }

    /// <inheritdoc/>
    public IReadOnlyList<EntityRelation> ExtractRelations(
        IReadOnlyList<SemanticEntity> entities,
        SemanticBlockResult block)
    {
        // Convert block's dep data to the format expected by LocationRelationExtractor.
        // The block stores SemanticDepToken/SemanticDepEdge, but the extractor needs HanLPToken/HanLPDepEdge.
        // We convert between the two formats.

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

        var locationRelations = LocationRelationExtractor.ExtractRelations(
            hanlpTokens,
            hanlpDeps,
            block.Entities,
            block.Content);

        // Convert LocationRelation → EntityRelation
        return locationRelations.Select(lr => new EntityRelation
        {
            RelationType = lr.RelationType.ToString(),
            SourceEntityId = lr.SourceLocation,
            TargetEntityId = lr.TargetLocation,
            Confidence = lr.Confidence,
            Evidence = lr.Evidence
        }).ToList();
    }

    /// <inheritdoc/>
    public IReadOnlyList<EntityRelation> ResolveCrossBlock(
        IReadOnlyList<(SemanticEntity Entity, SemanticBlockResult Block)> allEntities,
        IReadOnlyList<SemanticBlockResult> allBlocks)
    {
        var locationRelations = LocationRelationExtractor.ResolveCrossBlockCoreferences(allBlocks.ToList());

        return locationRelations.Select(lr => new EntityRelation
        {
            RelationType = lr.RelationType.ToString(),
            SourceEntityId = lr.SourceLocation,
            TargetEntityId = lr.TargetLocation,
            Confidence = lr.Confidence,
            Evidence = lr.Evidence
        }).ToList();
    }
}
