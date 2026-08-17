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
/// Two-stage enrichment:
/// 1. Fast path: pre-geocoded data from locations.geojson (via EntityContext.GeoJsonLookup)
/// 2. Fallback: live Nominatim API via GeocodingService when GeoJSON data is unavailable
///
/// This ensures locations are still enriched even when the GeoJSON pre-generation
/// has not been run or the specific location was not geocoded.
/// </summary>
public sealed class LocationProcessor : IEntityProcessor, IDisposable
{
    private readonly GeocodingService? _geocodingService;

    public LocationProcessor(GeocodingService? geocodingService = null)
    {
        _geocodingService = geocodingService;
    }

    public void Dispose()
    {
        _geocodingService?.Dispose();
    }
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
        // Stage 1: Fast path — pre-geocoded GeoJSON lookup
        var lookup = context.GeoJsonLookup;
        if (lookup != null && lookup.TryGetValue(entity.Text, out var feature) && feature.Status == "success")
        {
            return BuildResultFromGeoJsonFeature(feature, entity, context.Block);
        }

        // Stage 2: Fallback — live Nominatim API via GeocodingService
        if (_geocodingService != null)
        {
            try
            {
                var geocodeResult = await _geocodingService.GeocodeAsync(entity.Text, ct).ConfigureAwait(false);
                if (geocodeResult != null)
                {
                    return BuildResultFromGeocoding(geocodeResult, entity);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Fallback API call failed — continue to return empty result below
            }
        }

        // Stage 3: No data available — return result with a user-friendly message
        return new EntityEnrichmentResult
        {
            Source = "none",
            Confidence = 0.0,
            EnrichedText = entity.Text,
            Metadata = new Dictionary<string, object>
            {
                ["error"] = lookup is null
                    ? "Geocoding data unavailable"
                    : "Location not found in geocoding data"
            }
        };
    }

    /// <summary>
    /// Builds an enrichment result from a pre-parsed GeoJSON feature.
    /// Uses the canonical name (not the full address chain) as EnrichedText,
    /// and stores the full display name in metadata for reference.
    /// Applies context-aware confidence adjustment to reduce false matches.
    /// </summary>
    private static EntityEnrichmentResult BuildResultFromGeoJsonFeature(GeoJsonFeature feature, SemanticEntity entity, SemanticBlockResult block)
    {
        var canonicalName = ExtractCanonicalName(feature.DisplayName);

        // Context-aware confidence adjustment
        var adjustedConfidence = feature.Confidence;
        if (!IsLikelyLocationEntity(entity.Text))
        {
            adjustedConfidence *= 0.3;
        }
        else if (canonicalName != entity.Text && !entity.Text.Contains(canonicalName) && !canonicalName.Contains(entity.Text))
        {
            adjustedConfidence *= 0.5;
        }

        var result = new EntityEnrichmentResult
        {
            Source = "locations.geojson",
            EnrichedText = canonicalName,
            ExternalId = !string.IsNullOrEmpty(feature.OsmType) && feature.OsmId > 0
                ? $"{feature.OsmType}/{feature.OsmId}"
                : null,
            Confidence = adjustedConfidence,
            Metadata = new Dictionary<string, object>
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
                ["canonical_name"] = canonicalName,
                ["match_type"] = feature.MatchType,
                ["query_text"] = feature.QueryText
            }
        };

        if (feature.Bbox is { Length: 4 })
        {
            result.Metadata["bbox_min_lat"] = feature.Bbox[0];
            result.Metadata["bbox_max_lat"] = feature.Bbox[1];
            result.Metadata["bbox_min_lon"] = feature.Bbox[2];
            result.Metadata["bbox_max_lon"] = feature.Bbox[3];
        }

        return result;
    }

    /// <summary>
    /// Builds an enrichment result from a live GeocodingService API call.
    /// Uses the canonical name from display_name rather than the full address chain.
    /// </summary>
    private static EntityEnrichmentResult BuildResultFromGeocoding(GeocodingResult geocodeResult, SemanticEntity entity)
    {
        var canonicalName = ExtractCanonicalName(geocodeResult.DisplayName);

        var adjustedConfidence = geocodeResult.Confidence;
        if (!IsLikelyLocationEntity(entity.Text))
        {
            adjustedConfidence *= 0.3;
        }

        var result = new EntityEnrichmentResult
        {
            Source = "Nominatim",
            EnrichedText = canonicalName,
            ExternalId = !string.IsNullOrEmpty(geocodeResult.OsmType) && geocodeResult.OsmId > 0
                ? $"{geocodeResult.OsmType}/{geocodeResult.OsmId}"
                : null,
            Confidence = adjustedConfidence,
            Metadata = new Dictionary<string, object>
            {
                ["latitude"] = geocodeResult.Latitude,
                ["longitude"] = geocodeResult.Longitude,
                ["osm_type"] = geocodeResult.OsmType,
                ["osm_id"] = geocodeResult.OsmId,
                ["category"] = geocodeResult.Category,
                ["type"] = geocodeResult.Type,
                ["addresstype"] = geocodeResult.Addresstype,
                ["place_rank"] = geocodeResult.PlaceRank,
                ["importance"] = geocodeResult.Importance,
                ["display_name"] = geocodeResult.DisplayName,
                ["canonical_name"] = canonicalName,
                ["match_type"] = geocodeResult.MatchType,
                ["query_text"] = geocodeResult.QueryText
            }
        };

        if (geocodeResult.BoundingBoxMinLat.HasValue && geocodeResult.BoundingBoxMaxLat.HasValue &&
            geocodeResult.BoundingBoxMinLon.HasValue && geocodeResult.BoundingBoxMaxLon.HasValue)
        {
            result.Metadata["bbox_min_lat"] = geocodeResult.BoundingBoxMinLat.Value;
            result.Metadata["bbox_max_lat"] = geocodeResult.BoundingBoxMaxLat.Value;
            result.Metadata["bbox_min_lon"] = geocodeResult.BoundingBoxMinLon.Value;
            result.Metadata["bbox_max_lon"] = geocodeResult.BoundingBoxMaxLon.Value;
        }

        return result;
    }

    /// <summary>
    /// Extracts the canonical (short) name from a full display name.
    /// E.g., "五四大街, 皇城根北街社区, 景山街道, ..." → "五四大街"
    /// </summary>
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

    /// <summary>
    /// Checks if the entity text is likely a genuine location entity
    /// rather than a sentence fragment, literary expression, or non-entity text.
    /// </summary>
    private static bool IsLikelyLocationEntity(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        // Filter out text containing underscores or mixed alphanumeric patterns
        if (text.Contains('_'))
            return false;

        // Filter out text with newlines (segmentation artifacts)
        if (text.Contains('\n') || text.Contains('\r'))
            return false;

        // Filter out text that contains common non-location indicators
        var nonLocationPatterns = new[]
        {
            "这趟", "下一场", "中国高铁看", "更让", "让旅客",
            "出行更", "美好 京", "印记", "文脉", "笔下"
        };

        foreach (var pattern in nonLocationPatterns)
        {
            if (text.Contains(pattern))
                return false;
        }

        return true;
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
