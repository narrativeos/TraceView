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
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Caly.Core.Services;

/// <summary>
/// Represents a geocoding result with coordinates (from Nominatim API).
/// </summary>
public class GeocodingResult
{
    public string Name { get; set; } = string.Empty;
    public double Longitude { get; set; }
    public double Latitude { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public bool IsCached { get; set; }
    // Additional Nominatim fields
    public string PlaceId { get; set; } = string.Empty;
    public string OsmType { get; set; } = string.Empty;      // node, way, relation
    public long OsmId { get; set; }
    public string Class { get; set; } = string.Empty;        // boundary, place, highway, etc.
    public string Type { get; set; } = string.Empty;         // administrative, city, town, etc.
    public double Importance { get; set; }
    public double? BoundingBoxMinLat { get; set; }
    public double? BoundingBoxMaxLat { get; set; }
    public double? BoundingBoxMinLon { get; set; }
    public double? BoundingBoxMaxLon { get; set; }
}

public sealed class GeocodingService : IDisposable
{
    private readonly HttpClient _httpClient;

    public GeocodingService()
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
    }

    /// <summary>
    /// Geocode a single location name to coordinates by calling the geo search API.
    /// Always fetches fresh data from the API - no caching.
    /// </summary>
    public async Task<GeocodingResult?> GeocodeAsync(string locationName, CancellationToken cancellationToken = default)
    {
        // Call geo search API directly - no caching
        try
        {
            var encoded = System.Net.WebUtility.UrlEncode(locationName).ToLowerInvariant();
            var url = $"http://192.168.1.100:8088/search?q={encoded}&format=json&limit=1&accept-language=zh";

            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken);

                // Parse API response using string extraction (AOT-safe, no reflection)
                var result = ParseApiResult(json, locationName);
                if (result is not null)
                {
                    return result;
                }
            }
        }
        catch (Exception ex)
        {
            // Log the actual error for debugging
            System.Diagnostics.Debug.WriteLine($"Geocoding failed for '{locationName}': {ex.Message}");
        }

        // Return null if geocoding fails - no fallback
        return null;
    }

    /// <summary>
    /// Extract a string value for a given JSON property name.
    /// </summary>
    void ExtractJsonString(string json, string propertyName, out string? value)
    {
        var keyPattern = $"\"{propertyName}\"";
        var idx = json.IndexOf(keyPattern, StringComparison.Ordinal);
        if (idx < 0)
        {
            value = null;
            return;
        }

        // Find the colon after the key
        idx = json.IndexOf(':', idx + keyPattern.Length);
        if (idx < 0)
        {
            value = null;
            return;
        }

        // Skip whitespace and find the value start
        idx++;
        while (idx < json.Length && (json[idx] == ' ' || json[idx] == '\t' || json[idx] == '\n' || json[idx] == '\r'))
            idx++;

        if (idx >= json.Length)
        {
            value = null;
            return;
        }

        if (json[idx] == '"')
        {
            // String value
            var start = idx + 1;
            var end = start;
            while (end < json.Length && json[end] != '"')
            {
                if (json[end] == '\\') end++; // skip escaped chars
                end++;
            }
            value = json.Substring(start, end - start);
        }
        else
        {
            // Numeric or boolean value
            var start = idx;
            var end = idx;
            while (end < json.Length && json[end] != ',' && json[end] != '}')
                end++;
            value = json.Substring(start, end - start).Trim();
        }
    }

    /// <summary>
    /// Parse the API response JSON (Nominatim format) using string extraction (AOT-safe).
    /// </summary>
    GeocodingResult? ParseApiResult(string json, string name)
    {
        try
        {
            // The API returns a JSON array: [{"lon":"...","lat":"...","display_name":"...",...}]
            // We need to extract the first object's values
            if (!json.Contains("{"))
                return null;

            var result = new GeocodingResult { Name = name, IsCached = false };

            // Extract "lon" value
            ExtractJsonString(json, "lon", out var lonStr);
            // Extract "lat" value
            ExtractJsonString(json, "lat", out var latStr);

            if (!double.TryParse(lonStr, out var lon) || !double.TryParse(latStr, out var lat))
                return null; // Invalid coordinates means no useful result

            result.Longitude = lon;
            result.Latitude = lat;

            // Extract display_name
            ExtractJsonString(json, "display_name", out var displayName);
            result.DisplayName = displayName ?? name;

            // Extract optional fields
            ExtractJsonString(json, "place_id", out var placeId);
            result.PlaceId = placeId ?? string.Empty;

            ExtractJsonString(json, "osm_type", out var osmType);
            result.OsmType = osmType ?? string.Empty;

            ExtractJsonString(json, "osm_id", out var osmId);
            result.OsmId = long.TryParse(osmId, out var oid) ? oid : 0;

            // Note: "class" is a reserved word in JSON from the API, extract it carefully
            ExtractJsonString(json, "\"class\"", out var cls);
            result.Class = cls ?? string.Empty;

            ExtractJsonString(json, "type", out var type);
            result.Type = type ?? string.Empty;

            ExtractJsonString(json, "importance", out var importance);
            result.Importance = double.TryParse(importance, out var imp) ? imp : 0;

            // Extract boundingbox array if present: ["minlat","maxlat","minlon","maxlon"]
            ExtractJsonString(json, "boundingbox", out var bboxStr);
            if (!string.IsNullOrEmpty(bboxStr))
            {
                // Parse the array string - it comes as a string like '["39.76","40.11","119.61","119.95"]'
                // Remove brackets and quotes
                var inner = bboxStr.Trim('[', ']').Replace("\"", "");
                var parts = inner.Split(',');
                if (parts.Length >= 4)
                {
                    result.BoundingBoxMinLat = double.TryParse(parts[0].Trim(), out var minLat) ? minLat : null;
                    result.BoundingBoxMaxLat = double.TryParse(parts[1].Trim(), out var maxLat) ? maxLat : null;
                    result.BoundingBoxMinLon = double.TryParse(parts[2].Trim(), out var minLon) ? minLon : null;
                    result.BoundingBoxMaxLon = double.TryParse(parts[3].Trim(), out var maxLon) ? maxLon : null;
                }
            }

            return result;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Geocode multiple location names sequentially with per-location callbacks.
    /// </summary>
    public async Task<Dictionary<string, GeocodingResult>> GeocodeManyAsync(
        IEnumerable<string> locationNames,
        Action<string>? onProcessing = null,
        Action<GeocodingResult>? onSuccess = null,
        Action<string, string>? onFailed = null,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, GeocodingResult>();
        var distinctNames = locationNames.Distinct().ToList();

        // Run on background thread to avoid blocking UI
        await Task.Run(async () =>
        {
            for (int i = 0; i < distinctNames.Count; i++)
            {
                var name = distinctNames[i];
                cancellationToken.ThrowIfCancellationRequested();

                onProcessing?.Invoke(name);

                var result = await GeocodeAsync(name, cancellationToken).ConfigureAwait(false);
                if (result is not null)
                {
                    results[name] = result;
                    onSuccess?.Invoke(result);
                }
                else
                {
                    onFailed?.Invoke(name, "Geocoding failed (location not found)");
                }

                // Yield to allow other threads (including UI) to process
                await Task.Yield();
            }
        }, cancellationToken).ConfigureAwait(false);

        return results;
    }

    /// <summary>
    /// Geocode multiple location names sequentially with progress reporting (legacy signature).
    /// </summary>
    public async Task<Dictionary<string, GeocodingResult>> GeocodeManyAsync(
        IEnumerable<string> locationNames,
        Action<string, int, int>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        return await GeocodeManyAsync(
            locationNames,
            onProcessing: (name) => progressCallback?.Invoke(name, locationNames.Distinct().Count(), 0),
            onSuccess: null,
            onFailed: null,
            cancellationToken);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}