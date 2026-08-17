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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Caly.Core.Models;

namespace Caly.Core.Services;

/// <summary>
/// Service for generating GeoJSON files from semantic location entities.
/// </summary>
public sealed class GeoJsonService
{
    private readonly GeocodingService _geocodingService;

    public GeoJsonService()
    {
        _geocodingService = new GeocodingService();
    }

    /// <summary>
    /// Generate a GeoJSON file from semantic location entities (with per-location callbacks).
    /// </summary>
    /// <param name="semanticResults">Semantic analysis results.</param>
    /// <param name="outputDir">Output directory for the GeoJSON file.</param>
    /// <param name="onProcessing">Callback when a location starts processing.</param>
    /// <param name="onSuccess">Callback when a location succeeds (with all results).</param>
    /// <param name="onFailed">Callback when a location fails (with failure type for smart retry classification).</param>
    /// <param name="locationOrder">Optional: explicit order of location names to process (matches UI display order). If null, uses semantic results order.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<string?> GenerateGeoJsonAsync(
        SemanticResultFile? semanticResults,
        string outputDir,
        Action<string>? onProcessing = null,
        GeocodingService.GeocodingSuccessCallback? onSuccess = null,
        GeocodingService.GeocodingFailedCallback? onFailed = null,
        List<string>? locationOrder = null,
        CancellationToken cancellationToken = default)
    {
        if (semanticResults is null || semanticResults.Blocks.Count == 0)
            return null;

        // Collect all unique location entities (preserve entity info for syntactic role + source block ids)
        var locationByName = new Dictionary<string, (SemanticEntity Entity, List<string> SourceBlockIds)>();
        foreach (var block in semanticResults.Blocks)
        {
            foreach (var entity in block.LocationEntities)
            {
                if (!locationByName.ContainsKey(entity.Text))
                {
                    locationByName[entity.Text] = (entity, new List<string>(block.SourceBlockIds));
                }
            }
        }

        if (locationByName.Count == 0)
            return null;

        // Use explicit order if provided (to match UI display order), otherwise use dictionary order
        var locationNames = locationOrder ?? locationByName.Keys.ToList();

        // Phase 3: Build location relation collection from semantic results
        var relationCollection = BuildRelationCollection(semanticResults);

        // Phase 3: Determine enhancement strategies for each location
        var strategies = new Dictionary<string, GeocodingStrategy>();
        var parentResults = new Dictionary<string, List<GeocodingResult>>();
        foreach (var locationName in locationNames)
        {
            if (locationByName.TryGetValue(locationName, out var info))
            {
                strategies[locationName] = GeocodingEnhancementStrategy.DecideStrategy(
                    info.Entity, relationCollection);
            }
            else
            {
                strategies[locationName] = new GeocodingStrategy();
            }
        }

        // Track failed locations with their error info
        var failedLocations = new Dictionary<string, (string error, string failureType)>();

        // Geocode all locations with per-location callbacks, using enhancement strategies.
        // SpatialBias locations wait for their parent; others process in sequence.
        var geocodingResults = new Dictionary<string, List<GeocodingResult>>();

        for (int i = 0; i < locationNames.Count; i++)
        {
            var locationName = locationNames[i];
            cancellationToken.ThrowIfCancellationRequested();

            var strategy = strategies.GetValueOrDefault(locationName, new GeocodingStrategy());

            onProcessing?.Invoke(locationName);

            // Resolve parent bbox if needed (SpatialBias)
            GeocodingService.GeocodingBbox? parentBbox = null;
            if (strategy.QueryEnhancement == QueryEnhancement.SpatialBias &&
                !string.IsNullOrWhiteSpace(strategy.ContextLocationName) &&
                geocodingResults.TryGetValue(strategy.ContextLocationName, out var parentRes))
            {
                parentBbox = GeocodingService.GeocodingBbox.FromResult(parentRes.FirstOrDefault());
                System.Diagnostics.Debug.WriteLine(
                    $"[Geo] SpatialBias for '{locationName}': using parent '{strategy.ContextLocationName}' bbox");
            }

            // Process with enhancement
            var attempt = await _geocodingService.GeocodeWithEnhancementAsync(
                locationName, strategy, parentBbox, cancellationToken);

            if (attempt.IsSuccess)
            {
                geocodingResults[locationName] = attempt.Results;
                onSuccess?.Invoke(locationName, attempt.Results);
            }
            else
            {
                failedLocations[locationName] = (attempt.ErrorMessage ?? "Geocoding failed",
                    attempt.FailureType.ToString());
                onFailed?.Invoke(locationName, attempt.ErrorMessage ?? "Geocoding failed", attempt.FailureType);
            }
        }

        // Generate GeoJSON FeatureCollection with ALL locations (success + failed)
        var features = new List<Dictionary<string, object>>();

        foreach (var locationName in locationNames)
        {
            if (geocodingResults.TryGetValue(locationName, out var results))
            {
                // Successful geocoding - save ALL results
                var bestResult = results.FirstOrDefault();
                if (bestResult != null)
                {
                    var (locEntity, sourceBlockIds) = locationByName.TryGetValue(locationName, out var info) ? info : (null, new List<string>());
                    
                    // Build all_results array to save all geocoding results
                    var allResultsArray = new object[results.Count];
                    for (int i = 0; i < results.Count; i++)
                    {
                        var r = results[i];
                        var resultDict = new Dictionary<string, object>
                        {
                            { "name", r.Name },
                            { "latitude", r.Latitude },
                            { "longitude", r.Longitude },
                            { "display_name", r.DisplayName },
                            { "is_cached", r.IsCached },
                            { "place_id", r.PlaceId },
                            { "osm_type", r.OsmType },
                            { "osm_id", r.OsmId },
                            { "category", r.Category },
                            { "type", r.Type },
                            { "addresstype", r.Addresstype },
                            { "place_rank", r.PlaceRank },
                            { "address_rank", r.AddressRank },
                            { "importance", r.Importance },
                            { "confidence", r.Confidence },
                            { "match_type", r.MatchType },
                            { "matched_name_tag", r.MatchedNameTag },
                            { "matched_name", r.MatchedName },
                            { "match_by_homophone", r.MatchByHomophone },
                            { "query_text", !string.IsNullOrEmpty(r.QueryText) ? r.QueryText : locationName }
                        };
                        if (r.BoundingBoxMinLat.HasValue)
                        {
                            resultDict["bbox_min_lat"] = r.BoundingBoxMinLat.Value;
                            resultDict["bbox_max_lat"] = r.BoundingBoxMaxLat.Value;
                            resultDict["bbox_min_lon"] = r.BoundingBoxMinLon.Value;
                            resultDict["bbox_max_lon"] = r.BoundingBoxMaxLon.Value;
                        }
                        allResultsArray[i] = resultDict;
                    }
                    
                    // Successful geocoding (properties use best result)
                    var strategy = strategies.GetValueOrDefault(locationName, new GeocodingStrategy());
                    // Use the actual query that hit Nominatim from GeocodingResult.QueryText
                    var actualQueryText = !string.IsNullOrEmpty(bestResult.QueryText)
                        ? bestResult.QueryText
                        : locationName;

                    var properties = new Dictionary<string, object>
                    {
                        { "name", locationName },
                        { "display_name", bestResult.DisplayName },
                        { "status", "success" },
                        { "syntactic_role", locEntity?.SyntacticRole ?? "" },
                        { "syntactic_role_display", locEntity != null ? LocationSyntacticRole.ToDisplay(locEntity.SyntacticRole) : "" },
                        { "governing_verb", locEntity?.GoverningVerb ?? "" },
                        { "source_block_ids", sourceBlockIds },
                        { "is_cached", bestResult.IsCached },
                        { "place_id", bestResult.PlaceId },
                        { "osm_type", bestResult.OsmType },
                        { "osm_id", bestResult.OsmId },
                        { "category", bestResult.Category },
                        { "type", bestResult.Type },
                        { "addresstype", bestResult.Addresstype },
                        { "place_rank", bestResult.PlaceRank },
                        { "address_rank", bestResult.AddressRank },
                        { "importance", bestResult.Importance },
                        { "confidence", bestResult.Confidence },
                        { "match_type", bestResult.MatchType },
                        { "matched_name_tag", bestResult.MatchedNameTag },
                        { "matched_name", bestResult.MatchedName },
                        { "match_by_homophone", bestResult.MatchByHomophone },
                        // Phase 3: Enhanced geocoding metadata
                        { "query_text", actualQueryText },
                        { "parent_location", strategy.ContextLocationName ?? "" },
                        { "query_enhancement", strategy.QueryEnhancement.ToString() },
                        { "result_filtering", strategy.ResultFiltering.ToString() },
                        { "all_results", allResultsArray }
                    };

                if (bestResult.BoundingBoxMinLat.HasValue && bestResult.BoundingBoxMaxLat.HasValue && 
                    bestResult.BoundingBoxMinLon.HasValue && bestResult.BoundingBoxMaxLon.HasValue)
                {
                    properties["bbox"] = new object[]
                    {
                        bestResult.BoundingBoxMinLat.Value,
                        bestResult.BoundingBoxMaxLat.Value,
                        bestResult.BoundingBoxMinLon.Value,
                        bestResult.BoundingBoxMaxLon.Value
                    };
                }

                var geometry = new Dictionary<string, object>
                {
                    { "type", "Point" },
                    { "coordinates", new[] { bestResult.Longitude, bestResult.Latitude } }
                };

                var feature = new Dictionary<string, object>
                {
                    { "type", "Feature" },
                    { "geometry", geometry },
                    { "properties", properties }
                };

                features.Add(feature);
                }
            }
            else if (failedLocations.TryGetValue(locationName, out var failure))
            {
                var (locEntity2, sourceBlockIds2) = locationByName.TryGetValue(locationName, out var info2) ? info2 : (null, new List<string>());
                // Failed geocoding - include with null geometry
                var properties = new Dictionary<string, object>
                {
                    { "name", locationName },
                    { "display_name", "" },
                    { "status", "failed" },
                    { "error", failure.error },
                    { "failure_type", failure.failureType },
                    { "syntactic_role", locEntity2?.SyntacticRole ?? "" },
                    { "syntactic_role_display", locEntity2 != null ? LocationSyntacticRole.ToDisplay(locEntity2.SyntacticRole) : "" },
                    { "governing_verb", locEntity2?.GoverningVerb ?? "" },
                    { "source_block_ids", sourceBlockIds2 },
                    { "is_cached", false },
                    { "place_id", "" },
                    { "osm_type", "" },
                    { "osm_id", 0 },
                    { "class", "" },
                    { "type", "" },
                    { "importance", 0.0 },
                    { "confidence", 0.0 },
                    { "match_type", "" },
                    { "matched_name_tag", "" },
                    { "matched_name", "" }
                };

                var feature = new Dictionary<string, object>
                {
                    { "type", "Feature" },
                    { "geometry", null },
                    { "properties", properties }
                };

                features.Add(feature);
            }
            else
            {
                var (locEntity3, sourceBlockIds3) = locationByName.TryGetValue(locationName, out var info3) ? info3 : (null, new List<string>());
                // Location not processed at all (shouldn't happen, but handle gracefully)
                var properties = new Dictionary<string, object>
                {
                    { "name", locationName },
                    { "display_name", "" },
                    { "status", "not_processed" },
                    { "error", "Location was not processed" },
                    { "failure_type", "Unknown" },
                    { "syntactic_role", locEntity3?.SyntacticRole ?? "" },
                    { "syntactic_role_display", locEntity3 != null ? LocationSyntacticRole.ToDisplay(locEntity3.SyntacticRole) : "" },
                    { "governing_verb", locEntity3?.GoverningVerb ?? "" },
                    { "source_block_ids", sourceBlockIds3 },
                    { "is_cached", false },
                    { "place_id", "" },
                    { "osm_type", "" },
                    { "osm_id", 0 },
                    { "class", "" },
                    { "type", "" },
                    { "importance", 0.0 },
                    { "confidence", 0.0 },
                    { "match_type", "" },
                    { "matched_name_tag", "" },
                    { "matched_name", "" }
                };

                var feature = new Dictionary<string, object>
                {
                    { "type", "Feature" },
                    { "geometry", null },
                    { "properties", properties }
                };

                features.Add(feature);
            }
        }

        var geoJson = new Dictionary<string, object>
        {
            { "type", "FeatureCollection" },
            { "features", features }
        };

        // Save to file (use manual JSON building to avoid AOT serialization issues)
        Directory.CreateDirectory(outputDir);
        var filePath = Path.Combine(outputDir, "locations.geojson");
        var json = BuildJson(geoJson);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);

        return filePath;
    }

    // Syntactic role style definitions: role -> (fillColor, strokeColor, displayName)
    static (string fillColor, string strokeColor, string displayName) GetRoleStyle(string role)
    {
        return role switch
        {
            LocationSyntacticRole.Adverbial => ("#4CAF50", "#2E7D32", "状语 (Adverbial)"),
            LocationSyntacticRole.Subject => ("#2196F3", "#1565C0", "主语 (Subject)"),
            LocationSyntacticRole.Object => ("#F44336", "#C62828", "宾语 (Object)"),
            LocationSyntacticRole.Attributive => ("#FF9800", "#EF6C00", "定语 (Attributive)"),
            LocationSyntacticRole.Predicative => ("#9C27B0", "#7B1FA2", "表语 (Predicative)"),
            _ => ("#9E9E9E", "#616161", "未知 (Unknown)")
        };
    }

    static string GetRoleLayerId(string role)
    {
        return role switch
        {
            LocationSyntacticRole.Adverbial => "layer-adverbial",
            LocationSyntacticRole.Subject => "layer-subject",
            LocationSyntacticRole.Object => "layer-object",
            LocationSyntacticRole.Attributive => "layer-attributive",
            LocationSyntacticRole.Predicative => "layer-predicative",
            _ => "layer-unknown"
        };
    }

    /// <summary>
    /// Generate a GeoLibre-compatible JSON string with embedded GeoJSON data.
    /// Features are grouped into separate layers by syntactic role for independent filtering/styling.
    /// Each feature includes location name label and source block content for context.
    /// Returns the JSON string content (does not write to file).
    /// Prioritizes reading from cached GeoJSON file to avoid re-calling the geocoding API.
    /// </summary>
    /// <param name="semanticResults">Semantic analysis results.</param>
    /// <param name="geoJsonPath">Optional path to cached GeoJSON file. If provided and exists, reads from cache instead of calling API.</param>
    /// <param name="onProgress">Optional progress callback showing current status.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="popoTreeRoot">Optional Popo document tree root for building GeoStory chapters.</param>
    /// <param name="documentName">Optional document name used as the GeoStory title.</param>
    public async Task<string?> GenerateGeoLibreJsonStringAsync(
        SemanticResultFile? semanticResults,
        string? geoJsonPath = null,
        Action<string>? onProgress = null,
        CancellationToken cancellationToken = default,
        AnalysisTreeNode? popoTreeRoot = null,
        string? documentName = null)
    {
        if (semanticResults is null || semanticResults.Blocks.Count == 0)
            return null;

        // Collect all unique location entities (preserve entity info for syntactic role + block content + source block ids)
        var locationByName = new Dictionary<string, (SemanticEntity Entity, string BlockContent, List<string> SourceBlockIds)>();
        foreach (var block in semanticResults.Blocks)
        {
            foreach (var entity in block.LocationEntities)
            {
                if (!locationByName.ContainsKey(entity.Text))
                {
                    // Truncate block content to 200 chars for label purposes
                    var content = string.IsNullOrEmpty(block.Content) ? "" :
                        block.Content.Length > 200 ? block.Content.Substring(0, 200) + "..." : block.Content;
                    locationByName[entity.Text] = (entity, content, new List<string>(block.SourceBlockIds));
                }
            }
        }

        if (locationByName.Count == 0)
            return null;

        var locationNames = locationByName.Keys.ToList();
        var geocodingResults = new Dictionary<string, List<GeocodingResult>>();

        // Try to read from cached GeoJSON file first (fast, no API calls needed)
        bool cacheLoaded = false;
        if (!string.IsNullOrEmpty(geoJsonPath) && File.Exists(geoJsonPath))
        {
            cacheLoaded = TryLoadFromGeoJsonCache(geoJsonPath, locationByName, locationNames, ref geocodingResults, onProgress);
            System.Diagnostics.Debug.WriteLine($"[GeoLibre] Cache loaded: {cacheLoaded}, got {geocodingResults.Count} results from {locationNames.Count} locations");
        }

        // Only call API if cache was not available or did not cover all locations
        if (!cacheLoaded)
        {
            onProgress?.Invoke("调用地理编码 API...");
            System.Diagnostics.Debug.WriteLine($"[GeoLibre] No cache available, calling geocoding API for {locationNames.Count} locations");
            
            // Geocode all locations using the confidence-based parallel method
            geocodingResults = await _geocodingService.GeocodeManyWithConfidenceAsync(
                locationNames,
                onProcessing: (name) => {
                    onProgress?.Invoke($"地理编码: {name}");
                },
                onSuccess: (name, results) => {
                    onProgress?.Invoke($"成功: {name}");
                },
                onFailed: (name, error, failureType) => {
                    // Failed locations are ignored in GeoLibre output (only successful locations are included)
                    System.Diagnostics.Debug.WriteLine($"[GeoLibre] Geocoding failed for '{name}': {error} ({failureType})");
                    onProgress?.Invoke($"失败: {name}");
                },
                cancellationToken: cancellationToken);
        }

        // Calculate bounding box for map view center (use best result from each location)
        var bestResults = geocodingResults.Values.Select(r => r.FirstOrDefault()).Where(r => r != null).ToList();
        var longitudes = bestResults.Select(r => r!.Longitude).ToList();
        var latitudes = bestResults.Select(r => r!.Latitude).ToList();

        var minLon = longitudes.Min();
        var maxLon = longitudes.Max();
        var minLat = latitudes.Min();
        var maxLat = latitudes.Max();
        var centerLon = (minLon + maxLon) / 2;
        var centerLat = (minLat + maxLat) / 2;

        // Calculate an appropriate zoom level based on the bounding box size
        var zoom = CalculateZoomLevel(minLon, maxLon, minLat, maxLat);

        // Group geocoding results by syntactic role (use best result from each location)
        // Use (queryName, result) to preserve the original query for entity info lookup.
        var roles = new Dictionary<string, List<(string QueryName, GeocodingResult Result)>>();
        foreach (var kvp in geocodingResults)
        {
            var result = kvp.Value.FirstOrDefault();
            if (result == null) continue;
            // Use the dictionary key (original query name) for entity lookup, not result.Name (OSM name)
            var (entity, _, _) = locationByName.TryGetValue(kvp.Key, out var info) ? info : (null, "", new List<string>());
            var role = entity?.SyntacticRole ?? LocationSyntacticRole.Unknown;

            if (!roles.ContainsKey(role))
                roles[role] = new List<(string QueryName, GeocodingResult Result)>();
            roles[role].Add((kvp.Key, result));
        }

        // Build a separate layer for each syntactic role that has data
        var layers = new List<Dictionary<string, object>>();

        // Process roles in a consistent order
        var roleOrder = new[]
        {
            LocationSyntacticRole.Adverbial,
            LocationSyntacticRole.Subject,
            LocationSyntacticRole.Object,
            LocationSyntacticRole.Attributive,
            LocationSyntacticRole.Predicative,
            LocationSyntacticRole.Unknown
        };

        foreach (var role in roleOrder)
        {
            if (!roles.ContainsKey(role))
                continue;

            var results = roles[role];
            var (fillColor, strokeColor, displayName) = GetRoleStyle(role);
            var layerId = GetRoleLayerId(role);

            // Generate GeoJSON features for this role (only successful locations)
            var features = new List<Dictionary<string, object>>();

            foreach (var (queryName, result) in results)
            {
                // Use original query name for entity info lookup (not result.Name which is set to OSM name)
                var (entity, blockContent, sourceBlockIds) = locationByName.TryGetValue(queryName, out var info) ? info : (null, "", new List<string>());

                var properties = new Dictionary<string, object>
                {
                    { "name", result.Name },
                    { "display_name", result.DisplayName },
                    { "status", "success" },
                    { "syntactic_role", entity?.SyntacticRole ?? "" },
                    { "syntactic_role_display", entity != null ? LocationSyntacticRole.ToDisplay(entity.SyntacticRole) : "" },
                    { "governing_verb", entity?.GoverningVerb ?? "" },
                    { "spatial_role", entity?.SpatialRole ?? "" },
                    { "verb_spatial_class", entity?.VerbSpatialClass ?? "" },
                    { "source_block_ids", sourceBlockIds },
                    { "source_block_content", blockContent },
                    { "is_cached", result.IsCached },
                    { "place_id", result.PlaceId },
                    { "osm_type", result.OsmType },
                    { "osm_id", result.OsmId },
                    { "category", result.Category },
                    { "type", result.Type },
                    { "addresstype", result.Addresstype },
                    { "place_rank", result.PlaceRank },
                    { "address_rank", result.AddressRank },
                    { "importance", result.Importance },
                    { "confidence", result.Confidence },
                    { "match_type", result.MatchType },
                    { "matched_name_tag", result.MatchedNameTag },
                    { "matched_name", result.MatchedName }
                };

                if (result.BoundingBoxMinLat.HasValue && result.BoundingBoxMaxLat.HasValue &&
                    result.BoundingBoxMinLon.HasValue && result.BoundingBoxMaxLon.HasValue)
                {
                    properties["bbox"] = new object[]
                    {
                        result.BoundingBoxMinLat.Value,
                        result.BoundingBoxMaxLat.Value,
                        result.BoundingBoxMinLon.Value,
                        result.BoundingBoxMaxLon.Value
                    };
                }

                var geometry = new Dictionary<string, object>
                {
                    { "type", "Point" },
                    { "coordinates", new[] { result.Longitude, result.Latitude } }
                };

                var feature = new Dictionary<string, object>
                {
                    { "type", "Feature" },
                    { "geometry", geometry },
                    { "properties", properties }
                };

                features.Add(feature);
            }

            if (features.Count == 0)
                continue;

            var geojsonData = new Dictionary<string, object>
            {
                { "type", "FeatureCollection" },
                { "features", features }
            };

            // Build labels configuration per GeoLibre LabelStyle spec
            var labels = new Dictionary<string, object>
            {
                { "enabled", true },
                { "field", "name" },
                { "expression", "" },
                { "placement", "point" },
                { "size", 12 },
                { "color", "#111827" },
                { "haloColor", "#ffffff" },
                { "haloWidth", 1.5 },
                { "minZoom", 0 },
                { "maxZoom", 24 },
                { "allowOverlap", false },
                { "anchor", "top" },
                { "offsetX", 0 },
                { "offsetY", 0 },
                { "rotation", 0 },
                { "maxWidth", 20 },
                { "transform", "none" },
                { "dedupe", "unique" }
            };

            var layer = new Dictionary<string, object>
            {
                { "id", layerId },
                { "name", displayName },
                { "type", "geojson" },
                { "source", new Dictionary<string, object>
                    {
                        { "type", "geojson" }
                    }
                },
                { "visible", true },
                { "opacity", 1.0 },
                { "style", new Dictionary<string, object>
                    {
                        { "minZoom", 0 },
                        { "maxZoom", 24 },
                        { "fillColor", fillColor },
                        { "strokeColor", strokeColor },
                        { "strokeWidth", 2 },
                        { "strokeWidthUnit", "pixels" },
                        { "fillOpacity", 0.7 },
                        { "circleRadius", 7 },
                        { "labels", labels }
                    }
                },
                { "metadata", new Dictionary<string, object>() },
                { "geojson", geojsonData }
            };

            layers.Add(layer);
        }

        if (layers.Count == 0)
            return null;

        var geoLibreJson = new Dictionary<string, object>
        {
            { "version", "0.2.0" },
            { "name", "Document Locations" },
            { "mapView", new Dictionary<string, object>
                {
                    { "center", new[] { centerLon, centerLat } },
                    { "zoom", zoom },
                    { "bearing", 0.0 },
                    { "pitch", 0.0 },
                    { "bbox", new[] { minLon, minLat, maxLon, maxLat } }
                }
            },
            { "basemapStyleUrl", "https://tiles.openfreemap.org/styles/positron" },
            { "basemapVisible", true },
            { "basemapOpacity", 1.0 },
            { "layers", layers }
        };

        // Build GeoStory (story map) from Popo tree + verified geocoded features
        if (popoTreeRoot is not null && geocodingResults.Count > 0)
        {
            var geoFeatures = new List<GeoStoryBuilder.GeoFeature>();
            foreach (var kvp in geocodingResults)
            {
                var result = kvp.Value.FirstOrDefault();
                if (result == null) continue;

                // Use original query name (kvp.Key) for entity lookup
                var (entity, blockContent, sourceBlockIds) = locationByName.TryGetValue(kvp.Key, out var info)
                    ? info : (null, "", new List<string>());

                geoFeatures.Add(new GeoStoryBuilder.GeoFeature
                {
                    Name = result.Name,
                    Longitude = result.Longitude,
                    Latitude = result.Latitude,
                    SyntacticRole = entity?.SyntacticRole ?? "Unknown",
                    SpatialRole = entity?.SpatialRole ?? "",
                    VerbSpatialClass = entity?.VerbSpatialClass ?? "",
                    SourceBlockIds = sourceBlockIds ?? new List<string>(),
                    SourceBlockContent = blockContent ?? "",
                    BboxMinLat = result.BoundingBoxMinLat,
                    BboxMaxLat = result.BoundingBoxMaxLat,
                    BboxMinLon = result.BoundingBoxMinLon,
                    BboxMaxLon = result.BoundingBoxMaxLon,
                    OsmCategory = result.Category ?? "",
                    OsmType = result.Type ?? "",
                    PlaceRank = result.PlaceRank
                });
            }

            var storyBuilder = new GeoStoryBuilder();
            var storyConfig = storyBuilder.Build(popoTreeRoot, geoFeatures,
                documentName ?? "Document Story");

            var storyDict = GeoStoryBuilder.GeoStoryConfigToDict(storyConfig);
            if (storyDict is not null)
            {
                geoLibreJson["storymap"] = storyDict;
            }
        }

        // Build Knowledge Graph — prefer enriched data if available
        Dictionary<string, object>? kgData = null;

        var semanticDir = System.IO.Path.GetDirectoryName(geoJsonPath) ?? "";
        var enrichedPath = System.IO.Path.Combine(semanticDir ?? "", "enriched_result.json");

        if (System.IO.File.Exists(enrichedPath))
        {
            try
            {
                var enrichedJson = await System.IO.File.ReadAllTextAsync(enrichedPath);
                var enrichedResult = System.Text.Json.JsonSerializer.Deserialize<Caly.Core.Models.EnrichedResultFile>(
                    enrichedJson,
                    Caly.Core.Services.SourceGenerationContext.Default.EnrichedResultFile);
                if (enrichedResult != null)
                {
                    kgData = BuildEnrichedKnowledgeGraph(enrichedResult, geocodingResults, semanticResults);
                    System.Diagnostics.Debug.WriteLine($"[GeoJsonService] Built KG from enriched_result.json ({enrichedResult.Blocks.Count} blocks)");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GeoJsonService] Failed to read enriched_result.json: {ex.Message}, falling back");
            }
        }

        // Fallback: use original semantic results
        if (kgData == null)
        {
            kgData = BuildKnowledgeGraph(semanticResults, geocodingResults, locationByName);
        }

        // ── Phase: Augment KG with term discovery results (if available) ──
        var termPath = System.IO.Path.Combine(semanticDir ?? "", "term_result.json");
        if (System.IO.File.Exists(termPath) && kgData != null)
        {
            try
            {
                var termJson = await System.IO.File.ReadAllTextAsync(termPath);
                var options = new System.Text.Json.JsonSerializerOptions
                {
                    TypeInfoResolver = Caly.Core.Services.SourceGenerationContext.Default,
                    PropertyNameCaseInsensitive = true
                };
                var termResult = System.Text.Json.JsonSerializer.Deserialize(
                    termJson, typeof(Caly.Core.Models.TermResultFile), options) as Caly.Core.Models.TermResultFile;
                if (termResult != null)
                {
                    EnrichKgWithTerms(kgData, termResult);
                    System.Diagnostics.Debug.WriteLine($"[GeoJsonService] Augmented KG with {termResult.Terms.Count} terms from term_result.json");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GeoJsonService] Failed to load term_result.json: {ex.Message}");
            }
        }

        if (kgData is not null)
        {
            geoLibreJson["knowledgeGraph"] = kgData;
        }

        return BuildJson(geoLibreJson);
    }

    /// <summary>
    /// Builds a LocationRelationCollection from semantic results,
    /// combining intra-block relations and cross-block coreferences.
    /// </summary>
    private static LocationRelationCollection BuildRelationCollection(SemanticResultFile semanticResults)
    {
        var collection = new LocationRelationCollection();

        // Add intra-block relations from each block
        foreach (var block in semanticResults.Blocks)
        {
            if (block.LocationRelations.Count > 0)
            {
                foreach (var rel in block.LocationRelations)
                {
                    if (rel.SourceBlockIds.Count == 0)
                        rel.SourceBlockIds = new List<string>(block.SourceBlockIds);
                }
                collection.Relations.AddRange(block.LocationRelations);
            }
        }

        // Add cross-block coreference relations
        if (semanticResults.CrossBlockRelations.Count > 0)
        {
            collection.Relations.AddRange(semanticResults.CrossBlockRelations);
        }

        System.Diagnostics.Debug.WriteLine(
            $"[GeoJson] Built relation collection: {collection.Relations.Count} total relations ({collection.Summary})");

        return collection;
    }

    /// <summary>
    /// Builds a Knowledge Graph from semantic analysis results according to GeoLibre spec.
    /// </summary>
    Dictionary<string, object>? BuildKnowledgeGraph(
        SemanticResultFile semanticResults,
        Dictionary<string, List<GeocodingResult>> geocodingResults,
        Dictionary<string, (SemanticEntity Entity, string BlockContent, List<string> SourceBlockIds)> locationByName)
    {
        // 1. Collect all entities, deduplicate by category|normalized
        // Key: "CATEGORY|normalized", Value: node data
        var entityMap = new Dictionary<string, Dictionary<string, object>>();
        // Track mentions per node: key -> list of mention dicts
        var mentionsMap = new Dictionary<string, List<Dictionary<string, object>>>();

        foreach (var block in semanticResults.Blocks)
        {
            var blockId = block.SourceBlockIds.FirstOrDefault() ?? "";

            foreach (var entity in block.Entities)
            {
                // Skip LOCATION entities — they are built separately from optimized locations.geojson data
                if (entity.Category == "LOCATION")
                    continue;

                // Skip noise: pure numeric/symbol labels, LaTeX artifacts, single-char labels
                if (IsKgNoiseLabel(entity.Text))
                    continue;

                var key = $"{entity.Category}|{entity.Normalized}";
                if (!entityMap.ContainsKey(key))
                {
                    var newNode = new Dictionary<string, object>
                    {
                        { "id", $"node_{entity.Category}_{entity.Normalized}" },
                        { "type", MapToKgCategory(entity.Category) },
                        { "label", entity.Text },
                        { "normalized", entity.Normalized },
                        { "category", MapToKgCategory(entity.Category) },
                        { "frequency", 0 },
                        { "geoRefs", new List<Dictionary<string, object>>() },
                        { "mentions", new List<Dictionary<string, object>>() }
                    };
                    entityMap[key] = newNode;
                    mentionsMap[key] = new List<Dictionary<string, object>>();
                }

                var node = entityMap[key];
                var mentions = mentionsMap[key];

                // Only add mention if this blockId hasn't been added yet
                if (!mentions.Any(m => (string)m["blockId"] == blockId))
                {
                    // Update frequency
                    node["frequency"] = (int)node["frequency"] + 1;

                    var mention = new Dictionary<string, object>
                    {
                        { "blockId", blockId },
                        { "span", new[] { entity.Span[0], entity.Span[1] } },
                        { "confidence", entity.Confidence }
                    };
                    if (!string.IsNullOrEmpty(entity.SyntacticRole))
                        mention["syntacticRole"] = entity.SyntacticRole;
                    if (!string.IsNullOrEmpty(entity.GoverningVerb))
                        mention["governingVerb"] = entity.GoverningVerb;

                    mentions.Add(mention);
                }
            }
        }

        // Sync mentions from mentionsMap back to nodes
        foreach (var kvp in mentionsMap)
        {
            var node = entityMap[kvp.Key];
            var mentionsList = (List<Dictionary<string, object>>)node["mentions"];
            mentionsList.Clear();
            mentionsList.AddRange(kvp.Value);
        }

        // 1.5. Build LOCATION nodes from optimized locations (geocodingResults keys),
        // with mentions back-referenced from semantic entities for span-level detail.
        foreach (var locName in geocodingResults.Keys)
        {
            var locKey = $"LOCATION|{locName}";
            if (entityMap.ContainsKey(locKey))
                continue;

            var bestResult = geocodingResults[locName].FirstOrDefault();
            var label = bestResult?.DisplayName ?? locName;
            var normalized = locName;

            var locNode = new Dictionary<string, object>
            {
                { "id", $"node_LOCATION_{normalized}" },
                { "type", "LOCATION" },
                { "label", label },
                { "normalized", normalized },
                { "category", "LOCATION" },
                { "frequency", 0 },
                { "geoRefs", new List<Dictionary<string, object>>() },
                { "mentions", new List<Dictionary<string, object>>() }
            };
            entityMap[locKey] = locNode;
            var locMentions = new List<Dictionary<string, object>>();
            mentionsMap[locKey] = locMentions;

            // Back-reference semantic entities (Category == LOCATION) across all blocks
            // to collect mentions with span, confidence, and syntactic role detail.
            foreach (var block in semanticResults.Blocks)
            {
                var blockId = block.SourceBlockIds.FirstOrDefault() ?? "";

                foreach (var entity in block.Entities)
                {
                    if (entity.Category != "LOCATION")
                        continue;

                    // Match by text or normalized name
                    if (entity.Text == locName || entity.Normalized == locName)
                    {
                        if (!locMentions.Any(m => (string)m["blockId"] == blockId))
                        {
                            locNode["frequency"] = (int)locNode["frequency"] + 1;

                            var mention = new Dictionary<string, object>
                            {
                                { "blockId", blockId },
                                { "span", new[] { entity.Span[0], entity.Span[1] } },
                                { "confidence", entity.Confidence }
                            };
                            if (!string.IsNullOrEmpty(entity.SyntacticRole))
                                mention["syntacticRole"] = entity.SyntacticRole;
                            if (!string.IsNullOrEmpty(entity.GoverningVerb))
                                mention["governingVerb"] = entity.GoverningVerb;

                            locMentions.Add(mention);
                        }
                    }
                }
            }
        }

        // 2. Build geoRefs for LOCATION nodes
        foreach (var kvp in entityMap)
        {
            var node = kvp.Value;
            var category = (string)node["category"];
            if (category != "LOCATION")
                continue;

            var normalized = (string)node["normalized"];
            var geoRefs = (List<Dictionary<string, object>>)node["geoRefs"];

            // Try to find geocoding results for this location
            if (geocodingResults.TryGetValue(normalized, out var results) ||
                geocodingResults.TryGetValue((string)node["label"], out results))
            {
                var best = results.FirstOrDefault();
                if (best != null)
                {
                    // Derive source block IDs from the node's own mentions
                    // (mentions were built by back-referencing semantic entities)
                    var mentions = (List<Dictionary<string, object>>)node["mentions"];
                    var sourceBlockIds = mentions
                        .Select(m => (string)m["blockId"])
                        .Distinct()
                        .ToList();

                    var geoRef = new Dictionary<string, object>
                    {
                        { "sourceBlockIds", sourceBlockIds },
                        { "location", new Dictionary<string, object>
                            {
                                { "lng", best.Longitude },
                                { "lat", best.Latitude }
                            }
                        }
                    };

                    if (!string.IsNullOrEmpty(best.OsmType))
                    {
                        geoRef["osm"] = new Dictionary<string, object>
                        {
                            { "osmType", best.OsmType },
                            { "osmId", best.OsmId },
                            { "placeId", best.PlaceId },
                            { "displayName", best.DisplayName },
                            { "categoryName", best.Category },
                            { "featureType", best.Type }
                        };
                    }

                    geoRefs.Add(geoRef);
                }
            }
        }

        // 3. Build nodes array
        var nodes = entityMap.Values.ToList();

        // 4. Build a lookup: entity text -> node id (for matching relations)
        var textToNodeId = new Dictionary<string, string>();
        foreach (var kvp in entityMap)
        {
            var node = kvp.Value;
            var label = (string)node["label"];
            var normalized = (string)node["normalized"];
            var nodeId = (string)node["id"];
            if (!textToNodeId.ContainsKey(label))
                textToNodeId[label] = nodeId;
            if (!textToNodeId.ContainsKey(normalized))
                textToNodeId[normalized] = nodeId;
        }

        // 5. Build edges from relations and dependency parsing
        var edges = new List<Dictionary<string, object>>();
        int edgeCounter = 1;

        foreach (var block in semanticResults.Blocks)
        {
            var blockId = block.SourceBlockIds.FirstOrDefault() ?? "";

            // Process semantic relations
            foreach (var rel in block.Relations)
            {
                // Find nodes for subject and object
                if (textToNodeId.TryGetValue(rel.Subject, out var subjId) &&
                    textToNodeId.TryGetValue(rel.ObjectText, out var objId))
                {
                    var edgeType = MapPredicateToKgEdgeType(rel.Predicate);
                    var (src, tgt) = DetermineEdgeDirection(edgeType, subjId, objId);

                    var edge = new Dictionary<string, object>
                    {
                        { "id", $"edge_sem_{String.Format("{0:D3}", edgeCounter++)}" },
                        { "sourceId", src },
                        { "targetId", tgt },
                        { "type", edgeType },
                        { "label", $"{rel.PredicateVerb ?? rel.Predicate} → {rel.ObjectText}" },
                        { "confidence", rel.Confidence },
                        { "provenance", new Dictionary<string, object>
                            {
                                { "blockId", blockId },
                                { "evidence", rel.Evidence },
                                { "source", rel.Source },
                                { "confidence", rel.Confidence }
                            }
                        }
                    };
                    if (!string.IsNullOrEmpty(rel.PredicateVerb))
                        edge["predicateVerb"] = rel.PredicateVerb;

                    edges.Add(edge);
                }
            }

            // Process dependency edges
            if (block.DepTokens.Count > 0 && block.DepEdges.Count > 0)
            {
                foreach (var depEdge in block.DepEdges)
                {
                    var kgType = MapDepRelToKgEdgeType(depEdge.Rel);
                    if (kgType == null)
                        continue;
                    if (depEdge.Head == -1)
                        continue; // Skip root

                    if (depEdge.Child < 0 || depEdge.Child >= block.DepTokens.Count)
                        continue;
                    if (depEdge.Head < 0 || depEdge.Head >= block.DepTokens.Count)
                        continue;

                    var childToken = block.DepTokens[depEdge.Child];
                    var headToken = block.DepTokens[depEdge.Head];

                    // Skip punctuation and particles
                    if (IsSkipPos(childToken.Pos) || IsSkipPos(headToken.Pos))
                        continue;

                    if (!textToNodeId.TryGetValue(childToken.Text, out var childId))
                        continue;
                    if (!textToNodeId.TryGetValue(headToken.Text, out var headId))
                        continue;

                    // Skip self-loops
                    if (childId == headId)
                        continue;

                    var (src, tgt) = DetermineEdgeDirection(kgType, childId, headId);

                    var edge = new Dictionary<string, object>
                    {
                        { "id", $"edge_dep_{String.Format("{0:D3}", edgeCounter++)}" },
                        { "sourceId", src },
                        { "targetId", tgt },
                        { "type", kgType },
                        { "label", depEdge.Rel },
                        { "confidence", 0.9 },
                        { "provenance", new Dictionary<string, object>
                            {
                                { "blockId", blockId },
                                { "evidence", $"{childToken.Text} --[{depEdge.Rel}]--> {headToken.Text}" },
                                { "source", $"dep/{depEdge.Rel}" },
                                { "confidence", 0.9 }
                            }
                        }
                    };

                    edges.Add(edge);
                }
            }
        }

        // Deduplicate edges by sourceId+targetId+type
        var seenEdges = new HashSet<string>();
        var uniqueEdges = new List<Dictionary<string, object>>();
        foreach (var edge in edges)
        {
            var key = $"{edge["sourceId"]}|{edge["targetId"]}|{edge["type"]}";
            if (!seenEdges.Contains(key))
            {
                seenEdges.Add(key);
                uniqueEdges.Add(edge);
            }
        }

        if (nodes.Count == 0 && uniqueEdges.Count == 0)
            return null;

        // 6. Build visualization configuration
        var visualization = new Dictionary<string, object>
        {
            { "layout", "force" },
            { "showGeoNodes", true },
            { "nodeColors", new Dictionary<string, object>
                {
                    { "LOCATION", "#4CAF50" },
                    { "ORGANIZATION", "#2196F3" },
                    { "PERSON", "#FF9800" },
                    { "EVENT", "#9C27B0" },
                    { "CONCEPT", "#607D8B" },
                    { "ARTIFACT", "#795548" },
                    { "TIME", "#00BCD4" }
                }
            },
            { "edgeWidthByConfidence", true },
            { "poiHighlight", new Dictionary<string, object>
                {
                    { "highlightNodes", true },
                    { "highlightEdges", true },
                    { "highlightBlocks", false }
                }
            }
        };

        // 7. Assemble the final knowledge graph structure
        return new Dictionary<string, object>
        {
            { "title", "知识图谱" },
            { "description", "基于叙事文本的实体、句法依存与语义关系知识图谱" },
            { "source", semanticResults.Source },
            { "timestamp", semanticResults.Timestamp },
            { "nodes", nodes },
            { "edges", uniqueEdges },
            { "visualization", visualization }
        };
    }


    /// <summary>
    /// Builds a Knowledge Graph from enriched entity data (enriched_result.json).
    /// Nodes include enrichment metadata (external IDs, validation status, confidence).
    /// Edges include type-specific relations (Alias, Before, Subsidiary, etc.).
    /// </summary>
    Dictionary<string, object>? BuildEnrichedKnowledgeGraph(
        Caly.Core.Models.EnrichedResultFile enrichedResult,
        Dictionary<string, List<GeocodingResult>> geocodingResults,
        SemanticResultFile semanticResults)
    {
        var nodes = new List<Dictionary<string, object>>();
        var edges = new List<Dictionary<string, object>>();
        var nodeIndex = new Dictionary<string, Dictionary<string, object>>(); // id -> node
        var textToNodeId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int edgeCounter = 1;

        // ── Phase 1: Build nodes from enriched entities ──
        foreach (var block in enrichedResult.Blocks)
        {
            foreach (var entry in block.Entities)
            {
                // Skip invalid entities
                if (entry.ValidationStatus == "invalid")
                    continue;

                // Skip noise: pure numeric/symbol labels, LaTeX artifacts, single-char labels
                if (IsKgNoiseLabel(entry.Text))
                    continue;

                var nodeId = $"node_{entry.Category}_{entry.Normalized}";
                if (!nodeIndex.ContainsKey(nodeId))
                {
                    var node = new Dictionary<string, object>
                    {
                        { "id", nodeId },
                        { "type", MapToKgCategory(entry.Category) },
                        { "label", !string.IsNullOrEmpty(entry.EnrichedText) ? entry.EnrichedText : entry.Text },
                        { "normalized", entry.Normalized },
                        { "category", MapToKgCategory(entry.Category) },
                        { "frequency", 1 },
                        { "mentions", new List<Dictionary<string, object>>
                            {
                                new Dictionary<string, object>
                                {
                                    { "blockIds", block.BlockIds },
                                    { "text", entry.Text },
                                    { "confidence", entry.EnrichmentConfidence > 0 ? entry.EnrichmentConfidence : 0.5 }
                                }
                            }
                        }
                    };

                    // Enrichment metadata
                    if (!string.IsNullOrEmpty(entry.ExternalId))
                        node["externalId"] = entry.ExternalId;
                    if (!string.IsNullOrEmpty(entry.EnrichmentSource))
                        node["enrichmentSource"] = entry.EnrichmentSource;
                    if (entry.EnrichmentConfidence > 0)
                        node["enrichmentConfidence"] = entry.EnrichmentConfidence;
                    node["validationStatus"] = entry.ValidationStatus;
                    if (!string.IsNullOrEmpty(entry.ValidationReason))
                        node["validationReason"] = entry.ValidationReason;

                    // Type-specific metadata
                    if (entry.Metadata.Count > 0)
                    {
                        var meta = new Dictionary<string, object>();
                        foreach (var m in entry.Metadata)
                        {
                            var parts = m.Split('=', 2);
                            if (parts.Length == 2)
                                meta[parts[0]] = parts[1];
                        }
                        node["metadata"] = meta;
                    }

                    // LOCATION-specific: try metadata coords first, then geocodingResults
                    if (entry.Category == "LOCATION")
                    {
                        double? lat = null, lon = null;

                        // Try metadata first
                        if (node.TryGetValue("metadata", out var metaObj) && metaObj is Dictionary<string, object> meta)
                        {
                            if (meta.TryGetValue("latitude", out var latObj) && meta.TryGetValue("longitude", out var lonObj))
                            {
                                lat = double.Parse(latObj.ToString()!, System.Globalization.CultureInfo.InvariantCulture);
                                lon = double.Parse(lonObj.ToString()!, System.Globalization.CultureInfo.InvariantCulture);
                            }
                        }

                        // Fallback: try geocodingResults by entity text or enriched text
                        if (lat == null || lon == null)
                        {
                            var lookupName = !string.IsNullOrEmpty(entry.EnrichedText) ? entry.EnrichedText : entry.Text;
                            if (geocodingResults.TryGetValue(lookupName, out var geoResults) ||
                                geocodingResults.TryGetValue(entry.Text, out geoResults) ||
                                geocodingResults.TryGetValue(entry.Normalized, out geoResults))
                            {
                                var best = geoResults.FirstOrDefault();
                                if (best != null)
                                {
                                    lat = best.Latitude;
                                    lon = best.Longitude;
                                }
                            }
                        }

                        if (lat.HasValue && lon.HasValue)
                        {
                            node["geoRefs"] = new List<Dictionary<string, object>>
                            {
                                new Dictionary<string, object>
                                {
                                    { "sourceBlockIds", block.BlockIds },
                                    { "location", new Dictionary<string, object>
                                        {
                                            { "lat", lat.Value },
                                            { "lng", lon.Value }
                                        }
                                    }
                                }
                            };
                        }
                    }

                    nodes.Add(node);
                    nodeIndex[nodeId] = node;

                    // Register text→nodeId for dep edge matching
                    var nodeLabel = (string)node["label"];
                    var nodeNormalized = (string)node["normalized"];
                    if (!textToNodeId.ContainsKey(nodeLabel))
                        textToNodeId[nodeLabel] = nodeId;
                    if (!string.IsNullOrEmpty(nodeNormalized) && !textToNodeId.ContainsKey(nodeNormalized))
                        textToNodeId[nodeNormalized] = nodeId;
                }
                else
                {
                    // Update existing node frequency
                    var existing = nodeIndex[nodeId];
                    existing["frequency"] = (int)existing["frequency"] + 1;
                    // Merge mentions
                    var mentions = (List<Dictionary<string, object>>)existing["mentions"];
                    if (!mentions.Any(m => m.TryGetValue("blockIds", out var b) && b is List<int> bl && bl.SequenceEqual(block.BlockIds)))
                    {
                        mentions.Add(new Dictionary<string, object>
                        {
                            { "blockIds", block.BlockIds },
                            { "text", entry.Text }
                        });
                    }

                    // Also register text→nodeId for this entry
                    var nodeLabel = (string)existing["label"];
                    if (!textToNodeId.ContainsKey(nodeLabel))
                        textToNodeId[nodeLabel] = nodeId;
                }
            }
        }

        // ── Phase 2: Build edges from enriched relations ──
        foreach (var block in enrichedResult.Blocks)
        {
            foreach (var rel in block.Relations)
            {
                var srcId = FindNodeId(nodeIndex, rel.SourceEntityId);
                var tgtId = FindNodeId(nodeIndex, rel.TargetEntityId);

                if (srcId == null || tgtId == null || srcId == tgtId)
                    continue;

                var edgeType = MapEnrichedRelationToKgType(rel.RelationType, rel.EntityCategory);
                var edge = new Dictionary<string, object>
                {
                    { "id", $"edge_enr_{edgeCounter:D3}" },
                    { "sourceId", srcId },
                    { "targetId", tgtId },
                    { "type", edgeType },
                    { "label", $"{rel.EntityCategory}:{rel.RelationType}" },
                    { "confidence", rel.Confidence },
                    { "provenance", new Dictionary<string, object>
                        {
                            { "blockIds", block.BlockIds },
                            { "evidence", rel.Evidence },
                            { "source", $"enriched/{rel.EntityCategory}" },
                            { "confidence", rel.Confidence }
                        }
                    }
                };
                edges.Add(edge);
                edgeCounter++;
            }
        }

        // ── Phase 2.5: Add NLP block-level relations from semantic results ──
        // These are relations extracted by the NLP pipeline (IS_A, COMPOSED_OF, HAS_PROPERTY, etc.)
        // and stored in each semantic block's Relations list.
        foreach (var block in semanticResults.Blocks)
        {
            foreach (var rel in block.Relations)
            {
                // Find KG nodes for subject and object by text lookup
                var subjId = FindNodeId(nodeIndex, rel.Subject);
                var objId = FindNodeId(nodeIndex, rel.ObjectText);

                if (subjId == null || objId == null || subjId == objId)
                    continue;

                var edgeType = MapPredicateToKgEdgeType(rel.Predicate);
                // Override with more specific type for common predicates
                if (rel.Predicate == "HAS_PROPERTY" || rel.Predicate == "PROPERTY_OF")
                    edgeType = "HAS_PROPERTY";
                else if (rel.Predicate == "COMPOSED_OF" || rel.Predicate == "PART_OF")
                    edgeType = "COMPOSED_OF";
                else if (rel.Predicate == "IS_A")
                    edgeType = "IS_A";

                var edge = new Dictionary<string, object>
                {
                    { "id", $"edge_nlp_{edgeCounter:D3}" },
                    { "sourceId", subjId },
                    { "targetId", objId },
                    { "type", edgeType },
                    { "label", $"{rel.Predicate}: {rel.Subject} → {rel.ObjectText}" },
                    { "confidence", rel.Confidence > 0 ? rel.Confidence : 0.75 },
                    { "provenance", new Dictionary<string, object>
                        {
                            { "evidence", rel.Evidence ?? $"{rel.Subject} {rel.PredicateVerb} {rel.ObjectText}" },
                            { "source", $"nlp/{rel.Predicate}" },
                            { "confidence", rel.Confidence }
                        }
                    }
                };
                edges.Add(edge);
                edgeCounter++;
            }
        }

        // ── Phase 2.6: Add entity attribute edges from semantic results ──
        // Entity attributes are key-value pairs like "韧性=高" attached to entities.
        foreach (var block in semanticResults.Blocks)
        {
            foreach (var entity in block.Entities)
            {
                if (entity.Attributes == null || entity.Attributes.Count == 0)
                    continue;

                var entityId = FindNodeId(nodeIndex, entity.Text);
                if (entityId == null)
                    continue;

                foreach (var attr in entity.Attributes)
                {
                    // Attribute key → target node; if no value, key is the target
                    var attrText = !string.IsNullOrEmpty(attr.Value) ? attr.Value : attr.Key;
                    var attrId = FindNodeId(nodeIndex, attrText);
                    if (attrId == null || attrId == entityId)
                        continue;

                    var edge = new Dictionary<string, object>
                    {
                        { "id", $"edge_attr_{edgeCounter:D3}" },
                        { "sourceId", entityId },
                        { "targetId", attrId },
                        { "type", "HAS_PROPERTY" },
                        { "label", $"{attr.Key}={attr.Value}" },
                        { "confidence", attr.Confidence > 0 ? attr.Confidence : 0.75 },
                        { "provenance", new Dictionary<string, object>
                            {
                                { "evidence", $"{entity.Text}.{attr.Key} = {attr.Value}" },
                                { "source", "nlp/attribute" },
                                { "confidence", attr.Confidence }
                            }
                        }
                    };
                    edges.Add(edge);
                    edgeCounter++;
                }
            }
        }

        // ── Phase 3: Add cross-block relations ──
        foreach (var rel in enrichedResult.CrossBlockRelations)
        {
            var srcId = FindNodeId(nodeIndex, rel.SourceEntityId);
            var tgtId = FindNodeId(nodeIndex, rel.TargetEntityId);

            if (srcId == null || tgtId == null || srcId == tgtId)
                continue;

            var edgeType = MapEnrichedRelationToKgType(rel.RelationType, rel.EntityCategory);
            var edge = new Dictionary<string, object>
            {
                { "id", $"edge_xblk_{edgeCounter:D3}" },
                { "sourceId", srcId },
                { "targetId", tgtId },
                { "type", edgeType },
                { "label", $"cross:{rel.RelationType}" },
                { "confidence", rel.Confidence },
                { "provenance", new Dictionary<string, object>
                    {
                        { "evidence", rel.Evidence },
                        { "source", "cross_block_coreference" },
                        { "confidence", rel.Confidence }
                    }
                }
            };
            edges.Add(edge);
            edgeCounter++;
        }

        // ── Phase 3.5: Add dependency edges from semantic results ──
        // This supplements explicit enriched relations with syntactic dependency edges
        // (nn, conj, assmod, etc.) that the non-enriched KG builder already captures.
        //
        // Two-level matching:
        //   1. Exact match: dep token text == node label/normalized (fast, precise)
        //   2. Substring match: dep token text is substring of a node label (relaxed, cached)
        var substringMatchCache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var block in semanticResults.Blocks)
        {
            if (block.DepTokens.Count == 0 || block.DepEdges.Count == 0)
                continue;

            foreach (var depEdge in block.DepEdges)
            {
                var kgType = MapDepRelToKgEdgeType(depEdge.Rel);
                if (kgType == null)
                    continue;
                if (depEdge.Head == -1)
                    continue; // Skip root

                if (depEdge.Child < 0 || depEdge.Child >= block.DepTokens.Count)
                    continue;
                if (depEdge.Head < 0 || depEdge.Head >= block.DepTokens.Count)
                    continue;

                var childToken = block.DepTokens[depEdge.Child];
                var headToken = block.DepTokens[depEdge.Head];

                // Skip punctuation and particles
                if (IsSkipPos(childToken.Pos) || IsSkipPos(headToken.Pos))
                    continue;

                // ── Level 1: Exact match ──
                string? childId = FindNodeByTokenExactOrSubstring(childToken.Text, textToNodeId, nodeIndex, substringMatchCache);
                string? headId = FindNodeByTokenExactOrSubstring(headToken.Text, textToNodeId, nodeIndex, substringMatchCache);

                if (childId == null || headId == null)
                    continue;

                // Skip self-loops
                if (childId == headId)
                    continue;

                var (src, tgt) = DetermineEdgeDirection(kgType, childId, headId);

                var depKvEdge = new Dictionary<string, object>
                {
                    { "id", $"edge_dep_{edgeCounter:D3}" },
                    { "sourceId", src },
                    { "targetId", tgt },
                    { "type", kgType },
                    { "label", depEdge.Rel },
                    { "confidence", 0.85 },
                    { "provenance", new Dictionary<string, object>
                        {
                            { "blockId", block.SourceBlockIds.FirstOrDefault() ?? "" },
                            { "evidence", $"{childToken.Text} --[{depEdge.Rel}]--> {headToken.Text}" },
                            { "source", $"dep/{depEdge.Rel}" },
                            { "confidence", 0.85 }
                        }
                    }
                };

                edges.Add(depKvEdge);
                edgeCounter++;
            }
        }

        // ── Phase 3.6: Add co-occurrence edges ──
        // Nodes that appear in the same text block together get a weak COOCCUR edge.
        // This connects otherwise isolated nodes that share context but lack explicit
        // NLP relations or dependency edges.
        var blockNodeGroups = new Dictionary<string, HashSet<string>>(); // blockKey → set of nodeIds
        foreach (var block in semanticResults.Blocks)
        {
            var blockKey = block.SourceBlockIds.FirstOrDefault() ?? "";
            if (string.IsNullOrEmpty(blockKey)) continue;

            var nodeIdsInBlock = new HashSet<string>();
            foreach (var entity in block.Entities)
            {
                var nid = FindNodeId(nodeIndex, entity.Text);
                if (nid != null)
                    nodeIdsInBlock.Add(nid);
            }

            if (nodeIdsInBlock.Count < 2)
                continue;

            if (!blockNodeGroups.ContainsKey(blockKey))
                blockNodeGroups[blockKey] = nodeIdsInBlock;
            else
                blockNodeGroups[blockKey].UnionWith(nodeIdsInBlock);
        }

        // Create co-occurrence edges (limit to avoid quadratic explosion)
        foreach (var kvp in blockNodeGroups)
        {
            var nodeIds = kvp.Value.ToList();
            if (nodeIds.Count < 2 || nodeIds.Count > 50) // Skip tiny or huge blocks
                continue;

            // Only connect each node to its nearest 3 neighbors (sorted by id)
            nodeIds.Sort();
            for (int i = 0; i < nodeIds.Count; i++)
            {
                for (int j = i + 1; j < System.Math.Min(i + 4, nodeIds.Count); j++)
                {
                    edges.Add(new Dictionary<string, object>
                    {
                        { "id", $"edge_cooc_{edgeCounter:D3}" },
                        { "sourceId", nodeIds[i] },
                        { "targetId", nodeIds[j] },
                        { "type", "COOCCUR" },
                        { "label", "共现" },
                        { "confidence", 0.3 },
                        { "provenance", new Dictionary<string, object>
                            {
                                { "blockId", kvp.Key },
                                { "source", "cooccurrence" },
                                { "confidence", 0.3 }
                            }
                        }
                    });
                    edgeCounter++;
                }
            }
        }

        // ── Phase 4: Deduplicate edges ──
        var seenEdges = new HashSet<string>();
        var uniqueEdges = new List<Dictionary<string, object>>();
        foreach (var edge in edges)
        {
            var key = $"{edge["sourceId"]}|{edge["targetId"]}|{edge["type"]}";
            if (!seenEdges.Contains(key))
            {
                seenEdges.Add(key);
                uniqueEdges.Add(edge);
            }
        }

        if (nodes.Count == 0)
            return null;

        // ── Phase 5: Assemble ──
        return new Dictionary<string, object>
        {
            { "title", "知识图谱" },
            { "description", "基于实体处理器验证和富化的知识图谱，含 Wikidata、Nominatim、年号表等外部数据" },
            { "source", $"enriched_result.json v{enrichedResult.Version}" },
            { "timestamp", enrichedResult.Timestamp },
            { "nodes", nodes },
            { "edges", uniqueEdges },
            { "visualization", new Dictionary<string, object>
                {
                    { "layout", "force" },
                    { "showGeoNodes", true },
                    { "nodeColors", new Dictionary<string, object>
                        {
                            { "LOCATION", "#4CAF50" },
                            { "ORGANIZATION", "#2196F3" },
                            { "PERSON", "#FF9800" },
                            { "EVENT", "#9C27B0" },
                            { "CONCEPT", "#607D8B" },
                            { "ARTIFACT", "#795548" },
                            { "TIME", "#00BCD4" }
                        }
                    },
                    { "edgeWidthByConfidence", true },
                    { "poiHighlight", new Dictionary<string, object>
                        {
                            { "highlightNodes", true },
                            { "highlightEdges", true },
                            { "highlightBlocks", false }
                        }
                    }
                }
            }
        };
    }

    /// <summary>
    /// Augments an existing knowledge graph dictionary with term nodes and HYPERNYM/HYPONYM edges
    /// from term discovery results. Called after the main KG is built.
    /// </summary>
    static void EnrichKgWithTerms(Dictionary<string, object> kgData, Caly.Core.Models.TermResultFile termResult)
    {
        if (!kgData.TryGetValue("nodes", out var nodesObj) || nodesObj is not List<Dictionary<string, object>> nodes)
            return;
        if (!kgData.TryGetValue("edges", out var edgesObj) || edgesObj is not List<Dictionary<string, object>> edges)
            return;

        // Update node colors to include TERM
        if (kgData.TryGetValue("visualization", out var visObj) && visObj is Dictionary<string, object> vis)
        {
            if (vis.TryGetValue("nodeColors", out var colorsObj) && colorsObj is Dictionary<string, object> colors)
            {
                colors["TERM"] = "#E91E63"; // Pink for terms
            }
        }

        var existingLabels = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            if (node.TryGetValue("label", out var label) && label is string l)
                existingLabels.Add(l);
        }

        int edgeCounter = edges.Count + 1;

        // Add top-scoring terms as TERM nodes (limit to top 50 to avoid clutter)
        var topTerms = termResult.Terms
            .OrderByDescending(t => t.Score)
            .Take(50)
            .ToList();

        foreach (var term in topTerms)
        {
            var label = term.Text;
            if (existingLabels.Contains(label)) continue;

            var node = new Dictionary<string, object>
            {
                { "id", $"node_TERM_{label}" },
                { "type", "TERM" },
                { "label", label },
                { "normalized", label },
                { "category", "TERM" },
                { "frequency", term.Frequency },
                { "importanceScore", term.Score },
                { "mentions", new List<Dictionary<string, object>>() }
            };

            if (!string.IsNullOrEmpty(term.Definition))
                node["definition"] = term.Definition;
            if (!string.IsNullOrEmpty(term.Category))
                node["termCategory"] = term.Category;

            nodes.Add(node);
            existingLabels.Add(label);
        }

        // Add HYPERNYM / SYNONYM edges
        var labelToId = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            if (node.TryGetValue("label", out var label) && label is string l &&
                node.TryGetValue("id", out var id) && id is string i)
            {
                labelToId[l] = i;
            }
        }

        foreach (var rel in termResult.Relations)
        {
            if (!labelToId.TryGetValue(rel.Source, out var srcId) ||
                !labelToId.TryGetValue(rel.Target, out var tgtId))
                continue;
            if (srcId == tgtId) continue;

            var kgType = rel.Type switch
            {
                "HYPERNYM" => "HYPERNYM",
                "HYPONYM" => "HYPONYM",
                "SYNONYM" => "SYNONYM",
                "COMPOSED_OF" => "COMPOSED_OF",
                "RELATED_TERM" => "RELATED_TERM",
                _ => "SEMANTIC"
            };

            var edge = new Dictionary<string, object>
            {
                { "id", $"edge_term_{edgeCounter++:D3}" },
                { "sourceId", srcId },
                { "targetId", tgtId },
                { "type", kgType },
                { "label", $"{rel.Type}: {rel.Source} → {rel.Target}" },
                { "confidence", rel.Confidence },
                { "provenance", new Dictionary<string, object>
                    {
                        { "evidence", rel.Evidence ?? "" },
                        { "source", "term_discovery" },
                        { "confidence", rel.Confidence }
                    }
                }
            };
            edges.Add(edge);
        }
    }

    /// <summary>
    /// Maps enriched relation types to knowledge graph edge types.
    /// </summary>
    static string MapEnrichedRelationToKgType(string relationType, string entityCategory)
    {
        return relationType switch
        {
            "ParentChild" => "PARENT_CHILD",
            "Ownership" => "OWNERSHIP",
            "Parallel" => "PARALLEL",
            "Coreference" => "COREFERENCE",
            "Alias" => "ALIAS",
            "Family" => "FAMILY",
            "Colleague" => "COLLEAGUE",
            "Title" => "TITLE",
            "FullName" => "FULL_NAME",
            "Abbreviation" => "ABBREVIATION",
            "Subsidiary" => "SUBSIDIARY",
            "Sibling" => "SIBLING",
            "Before" => "BEFORE",
            "After" => "AFTER",
            "Contains" => "CONTAINS",
            "ContainedBy" => "CONTAINED_BY",
            "Equivalent" => "EQUIVALENT",
            "Overlaps" => "OVERLAPS",
            _ => $"ENRICHED_{entityCategory}"
        };
    }

    /// <summary>
    /// Find a node by matching text against node label, normalized, or externalId.
    /// </summary>
    static string? FindNodeId(Dictionary<string, Dictionary<string, object>> nodeIndex, string text)
    {
        if (string.IsNullOrEmpty(text)) return null;

        // Direct match by label
        foreach (var kvp in nodeIndex)
        {
            if (string.Equals((string)kvp.Value["label"], text, StringComparison.OrdinalIgnoreCase))
                return kvp.Key;
        }

        // Match by normalized
        foreach (var kvp in nodeIndex)
        {
            if (string.Equals((string)kvp.Value["normalized"], text, StringComparison.OrdinalIgnoreCase))
                return kvp.Key;
        }

        // Match by externalId
        foreach (var kvp in nodeIndex)
        {
            if (kvp.Value.TryGetValue("externalId", out var eid) &&
                string.Equals((string)eid, text, StringComparison.OrdinalIgnoreCase))
                return kvp.Key;
        }

        return null;
    }
    /// <summary>
    /// Maps HanLP entity categories to GeoLibre KGNodeCategory values.
    /// </summary>
    static string MapToKgCategory(string category)
    {
        return category switch
        {
            "LOCATION" => "LOCATION",
            "ORGANIZATION" => "ORGANIZATION",
            "PERSON" => "PERSON",
            "DATE" => "TIME",
            "TIME" => "TIME",
            "FACILITY" => "ARTIFACT",
            "EVENT" => "EVENT",
            _ => "CONCEPT"
        };
    }

    /// <summary>
    /// Maps HanLP relation predicates to GeoLibre KGEdgeType values.
    /// </summary>
    static string MapPredicateToKgEdgeType(string predicate)
    {
        return predicate switch
        {
            "LOCATED_AT" => "LOCATION_AT",
            "TIME_AT" => "TIME_AT",
            "AGENT" => "AGENT",
            "PATIENT" => "PATIENT",
            "SEMANTIC" => "SEMANTIC",
            _ => "SEMANTIC"
        };
    }

    /// <summary>
    /// Maps dependency parsing relations to GeoLibre KGEdgeType values.
    /// Returns null if the relation should be skipped.
    /// </summary>
    static string? MapDepRelToKgEdgeType(string rel)
    {
        return rel switch
        {
            "nsubj" => "AGENT",
            "dobj" => "PATIENT",
            "iobj" => "PATIENT",
            "amod" => "MODIFIER",
            "nn" => "MODIFIER",
            "rcmod" => "MODIFIER",
            "assmod" => "MODIFIER",
            "conj" => "COORDINATION",
            "loc" => "LOCATION_AT",
            "lobj" => "LOCATION_AT",
            "pobj" => "MODIFIER",
            _ => null
        };
    }

    /// <summary>
    /// Determines edge direction based on edge type.
    /// Some relations should go child->head, others head->child.
    /// </summary>
    static (string source, string target) DetermineEdgeDirection(string edgeType, string childId, string headId)
    {
        return edgeType switch
        {
            "AGENT" => (childId, headId),        // child is agent of head
            "PATIENT" => (headId, childId),       // head acts on child (patient)
            "LOCATION_AT" => (headId, childId),   // head is located at child
            "TIME_AT" => (headId, childId),       // head happens at child time
            "MODIFIER" => (childId, headId),      // child modifies head
            "COORDINATION" => (childId, headId),  // child coordinated with head
            "SEMANTIC" => (childId, headId),      // default direction
            _ => (childId, headId)
        };
    }

    /// <summary>
    /// Checks if a POS tag should be skipped for dependency edges.
    /// </summary>
    static bool IsSkipPos(string pos)
    {
        return pos == "PU" || pos == "DEC" || pos == "P" || pos == "CC" || pos == "WRD";
    }

    /// <summary>
    /// Tries to load geocoding results from a cached GeoJSON file.
    /// Returns true if the cache was loaded successfully.
    /// </summary>
    bool TryLoadFromGeoJsonCache(
        string geoJsonPath,
        Dictionary<string, (SemanticEntity Entity, string BlockContent, List<string> SourceBlockIds)> locationByName,
        List<string> locationNames,
        ref Dictionary<string, List<GeocodingResult>> geocodingResults,
        Action<string>? onProgress)
    {
        try
        {
            var content = File.ReadAllText(geoJsonPath);
            using (var doc = System.Text.Json.JsonDocument.Parse(content))
            {
                var root = doc.RootElement;
                if (!root.TryGetProperty("features", out System.Text.Json.JsonElement features) ||
                    features.ValueKind != System.Text.Json.JsonValueKind.Array)
                {
                    return false;
                }

                int count = 0;
                int total = features.GetArrayLength();
                
                foreach (var feature in features.EnumerateArray())
                {
                    if (feature.ValueKind != System.Text.Json.JsonValueKind.Object)
                        continue;

                    string? name = null;
                    double? lat = null;
                    double? lon = null;
                    string? displayName = null;
                    string? placeId = null;
                    string? osmType = null;
                    long osmId = 0;
                    string? category = null;
                    string? type = null;
                    string? addresstype = null;
                    int placeRank = 0;
                    int addressRank = 0;
                    double importance = 0;
                    double confidence = 0;
                    string? matchType = null;
                    string? matchedNameTag = null;
                    string? matchedName = null;
                    bool matchByHomophone = false;
                    double? bboxMinLat = null;
                    double? bboxMaxLat = null;
                    double? bboxMinLon = null;
                    double? bboxMaxLon = null;

                    // Parse properties
                    if (feature.TryGetProperty("properties", out System.Text.Json.JsonElement props) &&
                        props.ValueKind == System.Text.Json.JsonValueKind.Object)
                    {
                        // Get status - skip failed entries
                        if (props.TryGetProperty("status", out System.Text.Json.JsonElement statusEl) &&
                            statusEl.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            var status = statusEl.GetString();
                            if (status != "success")
                                continue; // Skip failed/not_processed locations
                        }
                        else
                        {
                            // No status field, check if geometry is null
                            if (feature.TryGetProperty("geometry", out System.Text.Json.JsonElement geom))
                            {
                                if (geom.ValueKind == System.Text.Json.JsonValueKind.Null)
                                    continue;
                            }
                        }

                        if (props.TryGetProperty("name", out System.Text.Json.JsonElement nameEl) && nameEl.ValueKind == System.Text.Json.JsonValueKind.String)
                            name = nameEl.GetString();
                        if (props.TryGetProperty("display_name", out System.Text.Json.JsonElement dnEl) && dnEl.ValueKind == System.Text.Json.JsonValueKind.String)
                            displayName = dnEl.GetString();
                        if (props.TryGetProperty("place_id", out System.Text.Json.JsonElement piEl) && piEl.ValueKind == System.Text.Json.JsonValueKind.String)
                            placeId = piEl.GetString();
                        if (props.TryGetProperty("osm_type", out System.Text.Json.JsonElement otEl) && otEl.ValueKind == System.Text.Json.JsonValueKind.String)
                            osmType = otEl.GetString();
                        if (props.TryGetProperty("osm_id", out System.Text.Json.JsonElement oidEl) && oidEl.ValueKind == System.Text.Json.JsonValueKind.Number)
                            osmId = oidEl.GetInt64();
                        if (props.TryGetProperty("category", out System.Text.Json.JsonElement cEl) && cEl.ValueKind == System.Text.Json.JsonValueKind.String)
                            category = cEl.GetString();
                        if (props.TryGetProperty("type", out System.Text.Json.JsonElement tEl) && tEl.ValueKind == System.Text.Json.JsonValueKind.String)
                            type = tEl.GetString();
                        if (props.TryGetProperty("addresstype", out System.Text.Json.JsonElement atEl) && atEl.ValueKind == System.Text.Json.JsonValueKind.String)
                            addresstype = atEl.GetString();
                        if (props.TryGetProperty("place_rank", out System.Text.Json.JsonElement prEl) && prEl.ValueKind == System.Text.Json.JsonValueKind.Number)
                            placeRank = prEl.GetInt32();
                        if (props.TryGetProperty("address_rank", out System.Text.Json.JsonElement arEl) && arEl.ValueKind == System.Text.Json.JsonValueKind.Number)
                            addressRank = arEl.GetInt32();
                        if (props.TryGetProperty("importance", out System.Text.Json.JsonElement impEl) && impEl.ValueKind == System.Text.Json.JsonValueKind.Number)
                            importance = impEl.GetDouble();
                        if (props.TryGetProperty("confidence", out System.Text.Json.JsonElement confEl) && confEl.ValueKind == System.Text.Json.JsonValueKind.Number)
                            confidence = confEl.GetDouble();
                        if (props.TryGetProperty("match_type", out System.Text.Json.JsonElement mtEl) && mtEl.ValueKind == System.Text.Json.JsonValueKind.String)
                            matchType = mtEl.GetString();
                        if (props.TryGetProperty("matched_name_tag", out System.Text.Json.JsonElement mntEl) && mntEl.ValueKind == System.Text.Json.JsonValueKind.String)
                            matchedNameTag = mntEl.GetString();
                        if (props.TryGetProperty("matched_name", out System.Text.Json.JsonElement mnEl) && mnEl.ValueKind == System.Text.Json.JsonValueKind.String)
                            matchedName = mnEl.GetString();
                        if (props.TryGetProperty("match_by_homophone", out System.Text.Json.JsonElement mbhEl))
                        {
                            if (mbhEl.ValueKind == System.Text.Json.JsonValueKind.True)
                                matchByHomophone = true;
                        }
                        
                        // Parse bounding box if present
                        if (props.TryGetProperty("bbox", out System.Text.Json.JsonElement bboxEl) &&
                            bboxEl.ValueKind == System.Text.Json.JsonValueKind.Array &&
                            bboxEl.GetArrayLength() >= 4)
                        {
                            bboxMinLat = bboxEl[0].GetDouble();
                            bboxMaxLat = bboxEl[1].GetDouble();
                            bboxMinLon = bboxEl[2].GetDouble();
                            bboxMaxLon = bboxEl[3].GetDouble();
                        }
                    }

                    // Parse geometry coordinates [lon, lat]
                    if (feature.TryGetProperty("geometry", out System.Text.Json.JsonElement geom2) &&
                        geom2.ValueKind == System.Text.Json.JsonValueKind.Object &&
                        geom2.TryGetProperty("coordinates", out System.Text.Json.JsonElement coords) &&
                        coords.ValueKind == System.Text.Json.JsonValueKind.Array &&
                        coords.GetArrayLength() >= 2)
                    {
                        lon = coords[0].GetDouble();
                        lat = coords[1].GetDouble();
                    }

                    if (string.IsNullOrEmpty(name) || !lat.HasValue || !lon.HasValue)
                        continue;

                    var result = new GeocodingResult
                    {
                        Name = name,
                        Latitude = lat.Value,
                        Longitude = lon.Value,
                        DisplayName = displayName ?? name,
                        IsCached = true,
                        PlaceId = placeId ?? "",
                        OsmType = osmType ?? "",
                        OsmId = osmId,
                        Category = category ?? "",
                        Type = type ?? "",
                        Addresstype = addresstype ?? "",
                        PlaceRank = placeRank,
                        AddressRank = addressRank,
                        Importance = importance,
                        Confidence = confidence,
                        MatchType = matchType ?? "",
                        MatchedNameTag = matchedNameTag ?? "",
                        MatchedName = matchedName ?? "",
                        MatchByHomophone = matchByHomophone
                    };

                    if (bboxMinLat.HasValue)
                    {
                        result.BoundingBoxMinLat = bboxMinLat;
                        result.BoundingBoxMaxLat = bboxMaxLat;
                        result.BoundingBoxMinLon = bboxMinLon;
                        result.BoundingBoxMaxLon = bboxMaxLon;
                    }

                    // Also try to load all_results from cache
                    var allResults = new List<GeocodingResult> { result };
                    if (props.ValueKind == System.Text.Json.JsonValueKind.Object &&
                        props.TryGetProperty("all_results", out System.Text.Json.JsonElement allResultsEl) &&
                        allResultsEl.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        allResults = new List<GeocodingResult>();
                        foreach (var r in allResultsEl.EnumerateArray())
                        {
                            if (r.ValueKind != System.Text.Json.JsonValueKind.Object)
                                continue;
                            
                            var rName = (r.TryGetProperty("name", out System.Text.Json.JsonElement rn) && rn.ValueKind == System.Text.Json.JsonValueKind.String) ? rn.GetString() : name;
                            var rLat = r.TryGetProperty("latitude", out System.Text.Json.JsonElement rl) && rl.ValueKind == System.Text.Json.JsonValueKind.Number ? (double?)rl.GetDouble() : null;
                            var rLon = r.TryGetProperty("longitude", out System.Text.Json.JsonElement rlo) && rlo.ValueKind == System.Text.Json.JsonValueKind.Number ? (double?)rlo.GetDouble() : null;
                            
                            if (!rLat.HasValue || !rLon.HasValue)
                                continue;

                            var rDisplayName = (r.TryGetProperty("display_name", out System.Text.Json.JsonElement rdn) && rdn.ValueKind == System.Text.Json.JsonValueKind.String) ? rdn.GetString() : rName;
                            var rPlaceId = (r.TryGetProperty("place_id", out System.Text.Json.JsonElement rpi) && rpi.ValueKind == System.Text.Json.JsonValueKind.String) ? rpi.GetString() : "";
                            var rOsmType = (r.TryGetProperty("osm_type", out System.Text.Json.JsonElement rot) && rot.ValueKind == System.Text.Json.JsonValueKind.String) ? rot.GetString() : "";
                            var rOsmId = r.TryGetProperty("osm_id", out System.Text.Json.JsonElement roid) && roid.ValueKind == System.Text.Json.JsonValueKind.Number ? roid.GetInt64() : 0L;
                            var rCategory = (r.TryGetProperty("category", out System.Text.Json.JsonElement rc) && rc.ValueKind == System.Text.Json.JsonValueKind.String) ? rc.GetString() : "";
                            var rType = (r.TryGetProperty("type", out System.Text.Json.JsonElement rt) && rt.ValueKind == System.Text.Json.JsonValueKind.String) ? rt.GetString() : "";
                            var rAddresstype = (r.TryGetProperty("addresstype", out System.Text.Json.JsonElement rat) && rat.ValueKind == System.Text.Json.JsonValueKind.String) ? rat.GetString() : "";
                            var rPlaceRank = r.TryGetProperty("place_rank", out System.Text.Json.JsonElement rpr) && rpr.ValueKind == System.Text.Json.JsonValueKind.Number ? rpr.GetInt32() : 0;
                            var rAddressRank = r.TryGetProperty("address_rank", out System.Text.Json.JsonElement rar) && rar.ValueKind == System.Text.Json.JsonValueKind.Number ? rar.GetInt32() : 0;
                            var rImportance = r.TryGetProperty("importance", out System.Text.Json.JsonElement ri) && ri.ValueKind == System.Text.Json.JsonValueKind.Number ? ri.GetDouble() : 0.0;
                            var rConfidence = r.TryGetProperty("confidence", out System.Text.Json.JsonElement rcf) && rcf.ValueKind == System.Text.Json.JsonValueKind.Number ? rcf.GetDouble() : 0.0;
                            var rMatchType = (r.TryGetProperty("match_type", out System.Text.Json.JsonElement rmt) && rmt.ValueKind == System.Text.Json.JsonValueKind.String) ? rmt.GetString() : "";
                            var rMatchedNameTag = (r.TryGetProperty("matched_name_tag", out System.Text.Json.JsonElement rmnt) && rmnt.ValueKind == System.Text.Json.JsonValueKind.String) ? rmnt.GetString() : "";
                            var rMatchedName = (r.TryGetProperty("matched_name", out System.Text.Json.JsonElement rmn) && rmn.ValueKind == System.Text.Json.JsonValueKind.String) ? rmn.GetString() : "";
                            var rMatchByHomophone = false;
                            if (r.TryGetProperty("match_by_homophone", out System.Text.Json.JsonElement rmh))
                            {
                                if (rmh.ValueKind == System.Text.Json.JsonValueKind.True) rMatchByHomophone = true;
                            }

                            var cachedResult = new GeocodingResult
                            {
                                Name = rName,
                                Latitude = rLat.Value,
                                Longitude = rLon.Value,
                                DisplayName = rDisplayName,
                                IsCached = true,
                                PlaceId = rPlaceId,
                                OsmType = rOsmType,
                                OsmId = rOsmId,
                                Category = rCategory,
                                Type = rType,
                                Addresstype = rAddresstype,
                                PlaceRank = rPlaceRank,
                                AddressRank = rAddressRank,
                                Importance = rImportance,
                                Confidence = rConfidence,
                                MatchType = rMatchType,
                                MatchedNameTag = rMatchedNameTag,
                                MatchedName = rMatchedName,
                                MatchByHomophone = rMatchByHomophone
                            };

                            if (r.TryGetProperty("bbox_min_lat", out System.Text.Json.JsonElement bml) && bml.ValueKind == System.Text.Json.JsonValueKind.Number)
                            {
                                cachedResult.BoundingBoxMinLat = bml.GetDouble();
                                cachedResult.BoundingBoxMaxLat = r.TryGetProperty("bbox_max_lat", out System.Text.Json.JsonElement bmal) && bmal.ValueKind == System.Text.Json.JsonValueKind.Number ? (double?)bmal.GetDouble() : null;
                                cachedResult.BoundingBoxMinLon = r.TryGetProperty("bbox_min_lon", out System.Text.Json.JsonElement bmlo) && bmlo.ValueKind == System.Text.Json.JsonValueKind.Number ? (double?)bmlo.GetDouble() : null;
                                cachedResult.BoundingBoxMaxLon = r.TryGetProperty("bbox_max_lon", out System.Text.Json.JsonElement bma) && bma.ValueKind == System.Text.Json.JsonValueKind.Number ? (double?)bma.GetDouble() : null;
                            }

                            allResults.Add(cachedResult);
                        }
                    }

                    if (allResults.Count > 0)
                    {
                        geocodingResults[name] = allResults;
                        count++;
                        onProgress?.Invoke($"从缓存加载: {count}/{total} ({name})");
                    }
                }
            }

            return geocodingResults.Count > 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GeoLibre] Failed to load from cache: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Generate a GeoLibre-compatible JSON file with embedded GeoJSON data.
    /// This format can be loaded directly by GeoLibre via ?url= parameter.
    /// </summary>
    public async Task<string?> GenerateGeoLibreJsonAsync(
        SemanticResultFile? semanticResults,
        string outputDir,
        string? geoJsonPath = null,
        Action<string>? onProgress = null,
        CancellationToken cancellationToken = default,
        AnalysisTreeNode? popoTreeRoot = null,
        string? documentName = null)
    {
        var json = await GenerateGeoLibreJsonStringAsync(semanticResults, geoJsonPath, onProgress, cancellationToken, popoTreeRoot, documentName);
        if (json is null)
            return null;

        // Save to file
        Directory.CreateDirectory(outputDir);
        var filePath = Path.Combine(outputDir, "locations.geolibre.json");
        await File.WriteAllTextAsync(filePath, json, cancellationToken);

        return filePath;
    }

    /// <summary>
    /// Build a JSON string from a dictionary structure (AOT-safe, no reflection).
    /// </summary>
    string BuildJson(object obj)
    {
        var sb = new StringBuilder();
        BuildJsonRecursive(sb, obj, 0);
        return sb.ToString();
    }

    void BuildJsonRecursive(StringBuilder sb, object? value, int indent)
    {
        var pad = new string(' ', indent * 2);

        if (value is null)
        {
            sb.Append("null");
        }
        else if (value is string s)
        {
            sb.Append('"').Append(EscapeJson(s)).Append('"');
        }
        else if (value is bool b)
        {
            sb.Append(b ? "true" : "false");
        }
        else if (value is double d)
        {
            sb.Append(d.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        else if (value is float f)
        {
            sb.Append(f.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        else if (value is int || value is long || value is short || value is byte)
        {
            sb.Append(Convert.ChangeType(value, typeof(long)).ToString());
        }
        else if (value is IDictionary<string, object> dict)
        {
            sb.Append("{\n");
            var items = dict.ToList();
            for (int idx = 0; idx < items.Count; idx++)
            {
                var kvp = items[idx];
                sb.Append(pad).Append(' ').Append('"').Append(EscapeJson(kvp.Key)).Append("\": ");
                BuildJsonRecursive(sb, kvp.Value, indent + 2);
                if (idx < items.Count - 1)
                    sb.Append(',');
                sb.Append('\n');
            }
            sb.Append(pad).Append('}');
        }
        else if (value is object[] arr)
        {
            sb.Append("[");
            for (int idx = 0; idx < arr.Length; idx++)
            {
                if (idx > 0)
                    sb.Append(", ");
                BuildJsonRecursive(sb, arr[idx], indent);
            }
            sb.Append("]");
        }
        else if (value is System.Collections.IList list)
        {
            sb.Append("[");
            for (int idx = 0; idx < list.Count; idx++)
            {
                if (idx > 0)
                    sb.Append(", ");
                BuildJsonRecursive(sb, list[idx], indent);
            }
            sb.Append("]");
        }
        else
        {
            sb.Append('"').Append(EscapeJson(value.ToString() ?? "")).Append('"');
        }
    }

    static string EscapeJson(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    /// <summary>
    /// Calculate an appropriate zoom level based on the bounding box of the points.
    /// Uses a simple heuristic based on the maximum dimension in degrees.
    /// Zoom 0 = full world, zoom ~18 = city level, zoom ~20 = building level.
    /// </summary>
    static double CalculateZoomLevel(double minLon, double maxLon, double minLat, double maxLat)
    {
        var lonSpan = maxLon - minLon;
        var latSpan = maxLat - minLat;
        var maxSpan = Math.Max(lonSpan, latSpan);

        // Edge case: all points are the same
        if (maxSpan < 0.0001)
            return 18.0; // Default to city-level zoom

        // The world is 360 degrees wide. At zoom level 0, the full world is visible.
        // Each zoom level doubles the magnification.
        // Formula: zoom = log2(360 / maxSpan) gives us the zoom where the bounding box
        // fills the viewport horizontally/vertically.
        // We subtract a small amount (0.5) to provide some padding around the points.
        var zoom = Math.Log(360.0 / maxSpan) / Math.Log(2.0) - 0.5;

        // Clamp to valid range (MapLibre typically supports 0-20)
        return Math.Max(1.0, Math.Min(20.0, zoom));
    }

    /// <summary>
    /// Filters out labels that are clearly not meaningful KG nodes:
    /// pure numbers/symbols, LaTeX artifacts, single-char non-CJK labels,
    /// and single-char CJK characters that are common noise (function words,
    /// measure words, segmentation artifacts).
    /// </summary>
    static bool IsKgNoiseLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return true;

        // Single character that isn't a CJK ideograph → noise
        if (label.Length == 1)
        {
            var c = label[0];
            // Allow CJK characters (U+4E00–U+9FFF, U+3400–U+4DBF)
            if ((c >= 0x4E00 && c <= 0x9FFF) || (c >= 0x3400 && c <= 0x4DBF))
            {
                // But reject common CJK noise chars: function words, measure words,
                // segmentation artifacts, and generic terms that are never standalone entities
                if (IsSingleCharCjkNoise(c))
                    return true;
                return false;
            }
            // Allow CJK compatibility ideographs
            if (c >= 0xF900 && c <= 0xFAFF)
                return false;
            return true; // Single non-CJK char is noise
        }

        // LaTeX artifacts
        if (label.StartsWith("\\") || label.Contains("\\frac") || label.Contains("\\mathrm") ||
            label.Contains("\\mathbf") || label.Contains("\\begin") || label.Contains("\\array") ||
            label.Contains("\\bigcirc") || label.Contains("\\mu") || label.Contains("\\sigma"))
            return true;
        if (label == "frac" || label == "mathrm" || label == "array" || label == "begin" ||
            label == "mathbf" || label == "textrm" || label == "bigcirc" || label == "cdot")
            return true;

        // ── P1: HTML/XML fragment detection ──
        if (label.StartsWith("</") || label.EndsWith("/>"))
            return true;
        if (label == "</sup" || label == "</sub" || label == "<sup>" || label == "<sub>")
            return true;

        // ── P1: Year range detection ──
        if (System.Text.RegularExpressions.Regex.IsMatch(label, @"^\d{4}[—\-–]\d{4}$"))
            return true;

        // ── P1: Single ASCII char ──
        if (label.Length == 1 && label[0] >= 'A' && label[0] <= 'z')
            return true;

        // ── P1: Short standard codes ──
        if (label.Length <= 6 && System.Text.RegularExpressions.Regex.IsMatch(label, @"^[A-Z]{2,5}[\s/]?[A-Z]?$"))
            return true;

        // Pure numeric/symbol patterns (with optional dashes, dots, brackets)
        if (System.Text.RegularExpressions.Regex.IsMatch(label, @"^[\d\s\-\.\+/\\\(\)\[\]\{\},;:'""\\|@#$%^&*=~`±×÷≤≥<>]+$"))
            return true;

        return false;
    }

    /// <summary>
    /// Two-level token-to-node matching: exact match first, then substring match (cached).
    /// Substring match requires the token to be ≥ half the length of the matched label
    /// to avoid false positives from short tokens (e.g., "上海" matching "上海尧治..." is OK,
    /// but "图" matching "图3-1" is not).
    /// </summary>
    static string? FindNodeByTokenExactOrSubstring(
        string tokenText,
        Dictionary<string, string> textToNodeId,
        Dictionary<string, Dictionary<string, object>> nodeIndex,
        Dictionary<string, string?> cache)
    {
        // Check cache
        if (cache.TryGetValue(tokenText, out var cached))
            return cached;

        // Level 1: Exact match
        if (textToNodeId.TryGetValue(tokenText, out var exactId))
        {
            cache[tokenText] = exactId;
            return exactId;
        }

        // Level 2: Substring match (token must be ≥ 2 chars AND ≥ half the label length)
        if (tokenText.Length >= 2)
        {
            foreach (var kvp in nodeIndex)
            {
                var label = (string)kvp.Value["label"];
                if (label.Length >= tokenText.Length * 2)
                    continue; // Token too short relative to label — skip to avoid false positives
                if (label.Contains(tokenText, StringComparison.OrdinalIgnoreCase))
                {
                    cache[tokenText] = kvp.Key;
                    return kvp.Key;
                }
            }
        }

        cache[tokenText] = null;
        return null;
    }

    /// <summary>
    /// Get the GeoLibre URL to load the generated JSON file.
    /// </summary>
    public string GetGeoLibreUrl(string geoLibreBaseUrl, string geoJsonFilePath)
    {
        // Use file:// URI for local file
        var fileUri = new Uri(geoJsonFilePath).AbsoluteUri;
        var baseUrl = geoLibreBaseUrl.TrimEnd('/');
        
        return $"{baseUrl}?url={Uri.EscapeDataString(fileUri)}";
    }

    /// <summary>
    /// Returns true if a single CJK character is common noise that should never
    /// appear as a standalone KG node. Covers: function words, measure words,
    /// cardinal directions, generic geographic terms, and known segmentation artifacts.
    /// </summary>
    static bool IsSingleCharCjkNoise(char c)
    {
        // This set covers chars observed as segmentation noise in real data,
        // plus common function/measure words that HanLP may mis-tag as entities.
        // We intentionally do NOT include:
        //   - Province abbreviations (京, 沪, 粤...)
        //   - Common surnames (王, 李, 张...)
        //   - Material/element names (铁, 钢, 金...)
        // as those can be legitimate standalone entities.
        return c switch
        {
            // Function words / particles
            '的' or '了' or '是' or '在' or '和' or '与' or '或' => true,
            '但' or '而' or '则' or '也' or '就' or '要' or '会' => true,
            '能' or '可' or '以' or '被' or '把' or '从' or '对' => true,
            '向' or '往' or '到' or '于' or '着' or '过' or '得' => true,
            '之' or '其' or '所' or '者' or '此' or '该' or '各' => true,
            '又' or '再' or '还' or '已' or '将' or '未' or '非' => true,
            
            // Measure words / classifiers
            '个' or '只' or '条' or '件' or '种' or '次' or '回' => true,
            '遍' or '趟' or '张' or '本' or '支' or '块' or '片' => true,
            '句' or '位' or '双' or '台' or '辆' or '艘' or '架' => true,
            '号' or '余' or '章' or '节' or '页' or '段' or '行' => true,
            '册' or '篇' or '份' or '类' or '款' or '项' => true,

            // CJK numerals (standalone almost never meaningful entities)
            '零' or '一' or '二' or '三' or '四' or '五' => true,
            '六' or '七' or '八' or '九' or '十' or '百' => true,
            '千' or '万' or '亿' or '两' or '几' or '半' => true,
            
            // Cardinal directions (too ambiguous alone)
            '东' or '南' or '西' or '北' or '中' or '上' or '下' => true,
            '左' or '右' or '前' or '后' or '内' or '外' or '里' => true,
            
            // Generic geographic terms (need qualifier to be a location)
            '河' or '山' or '海' or '江' or '湖' or '岛' or '湾' => true,
            '港' or '城' or '镇' or '村' or '庄' or '市' or '县' => true,
            '区' or '省' or '路' or '街' or '桥' or '门' or '关' => true,
            '岭' or '峰' or '谷' or '原' or '漠' or '林' or '泉' => true,
            '溪' or '潭' or '池' or '寺' or '庙' or '塔' or '楼' => true,
            
            // Known segmentation noise from real data
            '柱' or '际' or '杆' or '草' or '方' or '间' or '处' => true,
            '系' or '杠' or '模' or '途' or '层' or '端' or '侧' => true,
            '边' or '角' or '底' or '顶' or '头' or '尾' or '面' => true,
            
            _ => false
        };
    }
}