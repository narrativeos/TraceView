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
using System.Text.Json;
using System.Text.Json.Serialization;
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
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

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

        // Collect all unique location entities
        var locationNames = new List<string>();
        foreach (var block in semanticResults.Blocks)
        {
            foreach (var entity in block.LocationEntities)
            {
                if (!locationNames.Contains(entity.Text))
                {
                    locationNames.Add(entity.Text);
                }
            }
        }

        if (locationNames.Count == 0)
            return null;

        // Geocode all locations with per-location callbacks
        var geocodingResults = await _geocodingService.GeocodeManyAsync(
            locationNames, onProcessing, onSuccess, onFailed, cancellationToken);

        if (geocodingResults.Count == 0)
            return null;

        // Generate GeoJSON FeatureCollection
        var features = new List<Dictionary<string, object>>();

        foreach (var result in geocodingResults.Values)
        {
            var properties = new Dictionary<string, object>
            {
                { "name", result.Name },
                { "display_name", result.DisplayName },
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

        // Save to file
        Directory.CreateDirectory(outputDir);
        var filePath = Path.Combine(outputDir, "locations.geojson");
        var json = JsonSerializer.Serialize(geoJson, _jsonOptions);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);

        return filePath;
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
        if (semanticResults is null || semanticResults.Blocks.Count == 0)
            return null;

        // Collect all unique location entities
        var locationNames = new List<string>();
        foreach (var block in semanticResults.Blocks)
        {
            foreach (var entity in block.LocationEntities)
            {
                if (!locationNames.Contains(entity.Text))
                {
                    locationNames.Add(entity.Text);
                }
            }
        }

        if (locationNames.Count == 0)
            return null;

        // Geocode all locations (GenerateGeoLibreJsonAsync doesn't report progress)
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

        // Generate GeoJSON features
        var features = new List<Dictionary<string, object>>();

        foreach (var result in geocodingResults.Values)
        {
            var properties = new Dictionary<string, object>
            {
                { "name", result.Name },
                { "display_name", result.DisplayName }
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

        // Create GeoLibre-compatible JSON structure
        var geoLibreJson = new Dictionary<string, object>
        {
            { "version", "0.1.0" },
            { "name", "Document Locations" },
            { "mapView", new Dictionary<string, object>
                {
                    { "center", new[] { centerLon, centerLat } },
                    { "zoom", 10.0 },
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
                                { "type", "geojson" },
                                { "data", new Dictionary<string, object>
                                    {
                                        { "type", "FeatureCollection" },
                                        { "features", features }
                                    }
                                }
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
                                { "fillOpacity", 0.8 },
                                { "circleRadius", 8 }
                            }
                        }
                    }
                }
            }
        };

        // Save to file
        Directory.CreateDirectory(outputDir);
        var filePath = Path.Combine(outputDir, "locations.geolibre.json");
        var json = JsonSerializer.Serialize(geoLibreJson, _jsonOptions);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);

        return filePath;
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