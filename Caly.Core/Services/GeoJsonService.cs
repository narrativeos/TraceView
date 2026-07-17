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
    public async Task<string?> GenerateGeoJsonAsync(
        SemanticResultFile? semanticResults,
        string outputDir,
        Action<string>? onProcessing = null,
        Action<GeocodingResult>? onSuccess = null,
        Action<string, string>? onFailed = null,
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

        var locationNames = locationByName.Keys.ToList();

        // Geocode all locations with per-location callbacks
        var geocodingResults = await _geocodingService.GeocodeManyAsync(
            locationNames, onProcessing, onSuccess, onFailed, cancellationToken);

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
                { "importance", result.Importance }
            };

            if (result.BoundingBoxMinLat.HasValue)
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

    /// <summary>
    /// Generate a GeoLibre-compatible JSON string with embedded GeoJSON data.
    /// Returns the JSON string content (does not write to file).
    /// </summary>
    public async Task<string?> GenerateGeoLibreJsonStringAsync(
        SemanticResultFile? semanticResults,
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

        var locationNames = locationByName.Keys.ToList();

        // Geocode all locations
        var geocodingResults = await _geocodingService.GeocodeManyAsync(locationNames, null, cancellationToken);

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

        // Generate GeoJSON features
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
                { "governing_verb", entity?.GoverningVerb ?? "" }
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

        // Create GeoLibre-compatible JSON structure per official spec:
        // https://geolibre.app/project-format/
        // The geojson data goes in the layer's "geojson" field (not inside source.data)
        var geojsonData = new Dictionary<string, object>
        {
            { "type", "FeatureCollection" },
            { "features", features }
        };

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
            { "layers", new[]
                {
                    new Dictionary<string, object>
                    {
                        { "id", "locations-layer" },
                        { "name", "Locations" },
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
                                { "fillColor", "#3b82f6" },
                                { "strokeColor", "#1e40af" },
                                { "strokeWidth", 2 },
                                { "strokeWidthUnit", "pixels" },
                                { "fillOpacity", 0.6 },
                                { "circleRadius", 6 }
                            }
                        },
                        { "metadata", new Dictionary<string, object>() },
                        { "geojson", geojsonData }
                    }
                }
            }
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
        CancellationToken cancellationToken = default)
    {
        var json = await GenerateGeoLibreJsonStringAsync(semanticResults, cancellationToken);
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