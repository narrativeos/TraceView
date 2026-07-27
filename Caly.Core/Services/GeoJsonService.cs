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

        // Track failed locations with their error info
        var failedLocations = new Dictionary<string, (string error, string failureType)>();

        // Geocode all locations with per-location callbacks (using confidence-based geocoding)
        var geocodingResults = await _geocodingService.GeocodeManyWithConfidenceAsync(
            locationNames, onProcessing, 
            onSuccess: (name, results) => {
                onSuccess?.Invoke(name, results);
            },
            onFailed: (name, error, failureType) => {
                failedLocations[name] = (error, failureType.ToString());
                onFailed?.Invoke(name, error, failureType);
            }, 
            cancellationToken);

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
                            { "class", r.Class },
                            { "type", r.Type },
                            { "importance", r.Importance },
                            { "confidence", r.Confidence },
                            { "match_type", r.MatchType },
                            { "matched_name_tag", r.MatchedNameTag },
                            { "matched_name", r.MatchedName },
                            { "match_by_homophone", r.MatchByHomophone }
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
                        { "class", bestResult.Class },
                        { "type", bestResult.Type },
                        { "importance", bestResult.Importance },
                        { "confidence", bestResult.Confidence },
                        { "match_type", bestResult.MatchType },
                        { "matched_name_tag", bestResult.MatchedNameTag },
                        { "matched_name", bestResult.MatchedName },
                        { "match_by_homophone", bestResult.MatchByHomophone },
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
        var roles = new Dictionary<string, List<GeocodingResult>>();
        foreach (var kvp in geocodingResults)
        {
            var result = kvp.Value.FirstOrDefault();
            if (result == null) continue;
            var (entity, _, _) = locationByName.TryGetValue(result.Name, out var info) ? info : (null, "", new List<string>());
            var role = entity?.SyntacticRole ?? LocationSyntacticRole.Unknown;

            if (!roles.ContainsKey(role))
                roles[role] = new List<GeocodingResult>();
            roles[role].Add(result);
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

            foreach (var result in results)
            {
                var (entity, blockContent, sourceBlockIds) = locationByName.TryGetValue(result.Name, out var info) ? info : (null, "", new List<string>());

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
                    { "class", result.Class },
                    { "type", result.Type },
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

                var (entity, blockContent, sourceBlockIds) = locationByName.TryGetValue(result.Name, out var info)
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
                    OsmClass = result.Class ?? "",
                    OsmType = result.Type ?? ""
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

        // Build Knowledge Graph from semantic analysis results
        var kgData = BuildKnowledgeGraph(semanticResults, geocodingResults, locationByName);
        if (kgData is not null)
        {
            geoLibreJson["knowledgeGraph"] = kgData;
        }

        return BuildJson(geoLibreJson);
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
                var key = $"{entity.Category}|{entity.Normalized}";
                if (!entityMap.ContainsKey(key))
                {
                    var newNode = new Dictionary<string, object>
                    {
                        { "id", $"node_{entity.Category}_{entity.Normalized}" },
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
                    // Collect all source block IDs for this location
                    var sourceBlockIds = new List<string>();
                    if (locationByName.TryGetValue(normalized, out var info) ||
                        locationByName.TryGetValue((string)node["label"], out info))
                    {
                        sourceBlockIds = info.SourceBlockIds;
                    }

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
                            { "className", best.Class },
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
                    string? cls = null;
                    string? type = null;
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
                        if (props.TryGetProperty("class", out System.Text.Json.JsonElement cEl) && cEl.ValueKind == System.Text.Json.JsonValueKind.String)
                            cls = cEl.GetString();
                        if (props.TryGetProperty("type", out System.Text.Json.JsonElement tEl) && tEl.ValueKind == System.Text.Json.JsonValueKind.String)
                            type = tEl.GetString();
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
                        Class = cls ?? "",
                        Type = type ?? "",
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
                            var rClass = (r.TryGetProperty("class", out System.Text.Json.JsonElement rc) && rc.ValueKind == System.Text.Json.JsonValueKind.String) ? rc.GetString() : "";
                            var rType = (r.TryGetProperty("type", out System.Text.Json.JsonElement rt) && rt.ValueKind == System.Text.Json.JsonValueKind.String) ? rt.GetString() : "";
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
                                Class = rClass,
                                Type = rType,
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
    /// Get the GeoLibre URL to load the generated JSON file.
    /// </summary>
    public string GetGeoLibreUrl(string geoLibreBaseUrl, string geoJsonFilePath)
    {
        // Use file:// URI for local file
        var fileUri = new Uri(geoJsonFilePath).AbsoluteUri;
        var baseUrl = geoLibreBaseUrl.TrimEnd('/');
        
        return $"{baseUrl}?url={Uri.EscapeDataString(fileUri)}";
    }
}