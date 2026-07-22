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
    /// <param name="onSuccess">Callback when a location succeeds.</param>
    /// <param name="onFailed">Callback when a location fails (with failure type for smart retry classification).</param>
    /// <param name="locationOrder">Optional: explicit order of location names to process (matches UI display order). If null, uses semantic results order.</param>
    /// <param name="exactMatch">Whether to require exact name match (reject phonetic matches).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<string?> GenerateGeoJsonAsync(
        SemanticResultFile? semanticResults,
        string outputDir,
        Action<string>? onProcessing = null,
        Action<GeocodingResult>? onSuccess = null,
        GeocodingService.GeocodingFailedCallback? onFailed = null,
        List<string>? locationOrder = null,
        bool exactMatch = true,
        CancellationToken cancellationToken = default)
    {
        if (semanticResults is null || semanticResults.Blocks.Count == 0)
            return null;

        // Collect all unique location entities (preserve entity info for syntactic role)
        var locationByName = new Dictionary<string, SemanticEntity>();
        foreach (var block in semanticResults.Blocks)
        {
            foreach (var entity in block.LocationEntities)
            {
                if (!locationByName.ContainsKey(entity.Text))
                {
                    locationByName[entity.Text] = entity;
                }
            }
        }

        if (locationByName.Count == 0)
            return null;

        // Use explicit order if provided (to match UI display order), otherwise use dictionary order
        var locationNames = locationOrder ?? locationByName.Keys.ToList();

        // Geocode all locations with per-location callbacks (using confidence-based geocoding)
        var geocodingResults = await _geocodingService.GeocodeManyWithConfidenceAsync(
            locationNames, onProcessing, onSuccess, onFailed, exactMatch, cancellationToken);

        if (geocodingResults.Count == 0)
            return null;

        // Generate GeoJSON FeatureCollection
        var features = new List<Dictionary<string, object>>();

        foreach (var kvp in geocodingResults)
        {
            var result = kvp.Value;
            var entity = locationByName.TryGetValue(result.Name, out var e) ? e : null;

            var properties = new Dictionary<string, object>
            {
                { "name", result.Name },
                { "display_name", result.DisplayName },
                { "syntactic_role", entity?.SyntacticRole ?? "" },
                { "syntactic_role_display", entity != null ? LocationSyntacticRole.ToDisplay(entity.SyntacticRole) : "" },
                { "governing_verb", entity?.GoverningVerb ?? "" },
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
    /// </summary>
    public async Task<string?> GenerateGeoLibreJsonStringAsync(
        SemanticResultFile? semanticResults,
        bool exactMatch = true,
        CancellationToken cancellationToken = default)
    {
        if (semanticResults is null || semanticResults.Blocks.Count == 0)
            return null;

        // Collect all unique location entities (preserve entity info for syntactic role + block content)
        var locationByName = new Dictionary<string, (SemanticEntity Entity, string BlockContent)>();
        foreach (var block in semanticResults.Blocks)
        {
            foreach (var entity in block.LocationEntities)
            {
                if (!locationByName.ContainsKey(entity.Text))
                {
                    // Truncate block content to 200 chars for label purposes
                    var content = string.IsNullOrEmpty(block.Content) ? "" :
                        block.Content.Length > 200 ? block.Content.Substring(0, 200) + "..." : block.Content;
                    locationByName[entity.Text] = (entity, content);
                }
            }
        }

        if (locationByName.Count == 0)
            return null;

        var locationNames = locationByName.Keys.ToList();

        // Geocode all locations using the confidence-based parallel method
        var geocodingResults = await _geocodingService.GeocodeManyWithConfidenceAsync(
            locationNames, exactMatch: exactMatch, cancellationToken: cancellationToken);

        if (geocodingResults.Count == 0)
            return null;

        // Calculate bounding box for map view center
        var longitudes = geocodingResults.Values.Select(r => r.Longitude).ToList();
        var latitudes = geocodingResults.Values.Select(r => r.Latitude).ToList();

        var minLon = longitudes.Min();
        var maxLon = longitudes.Max();
        var minLat = latitudes.Min();
        var maxLat = latitudes.Max();
        var centerLon = (minLon + maxLon) / 2;
        var centerLat = (minLat + maxLat) / 2;

        // Calculate an appropriate zoom level based on the bounding box size
        var zoom = CalculateZoomLevel(minLon, maxLon, minLat, maxLat);

        // Group geocoding results by syntactic role
        var roles = new Dictionary<string, List<GeocodingResult>>();
        foreach (var kvp in geocodingResults)
        {
            var result = kvp.Value;
            var (entity, _) = locationByName.TryGetValue(result.Name, out var info) ? info : (null, "");
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

            // Generate GeoJSON features for this role
            var features = new List<Dictionary<string, object>>();

            foreach (var result in results)
            {
                var (entity, blockContent) = locationByName.TryGetValue(result.Name, out var info) ? info : (null, "");

                var properties = new Dictionary<string, object>
                {
                    { "name", result.Name },
                    { "display_name", result.DisplayName },
                    { "syntactic_role", entity?.SyntacticRole ?? "" },
                    { "syntactic_role_display", entity != null ? LocationSyntacticRole.ToDisplay(entity.SyntacticRole) : "" },
                    { "governing_verb", entity?.GoverningVerb ?? "" },
                    { "block_content", blockContent }
                };

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
            { "version", "0.1.0" },
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

        return BuildJson(geoLibreJson);
    }

    /// <summary>
    /// Generate a GeoLibre-compatible JSON file with embedded GeoJSON data.
    /// This format can be loaded directly by GeoLibre via ?url= parameter.
    /// </summary>
    public async Task<string?> GenerateGeoLibreJsonAsync(
        SemanticResultFile? semanticResults,
        string outputDir,
        bool exactMatch = true,
        CancellationToken cancellationToken = default)
    {
        var json = await GenerateGeoLibreJsonStringAsync(semanticResults, exactMatch, cancellationToken);
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