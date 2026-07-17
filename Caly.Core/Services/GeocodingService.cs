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
    private readonly string _cacheDirectory;
    private readonly Dictionary<string, GeocodingResult> _memoryCache = new();

    // Built-in coordinate map for common Chinese cities (offline fallback)
    private static readonly Dictionary<string, (double Longitude, double Latitude)> CommonCities = new()
    {
        { "北京", (116.4074, 39.9042) },
        { "上海", (121.4737, 31.2304) },
        { "广州", (113.2644, 23.1291) },
        { "深圳", (114.0579, 22.5431) },
        { "成都", (104.0657, 30.5728) },
        { "杭州", (120.1551, 30.2741) },
        { "南京", (118.7969, 32.0603) },
        { "武汉", (114.3054, 30.5931) },
        { "西安", (108.9401, 34.3416) },
        { "重庆", (106.5504, 29.5630) },
        { "天津", (117.2009, 39.0922) },
        { "苏州", (120.6194, 31.2989) },
        { "郑州", (113.6253, 34.7466) },
        { "长沙", (112.9388, 28.2282) },
        { "青岛", (120.3826, 36.0671) },
        { "大连", (121.6144, 38.9140) },
        { "厦门", (118.0894, 24.4798) },
        { "沈阳", (123.4315, 41.8054) },
        { "哈尔滨", (126.6429, 45.7570) },
        { "济南", (117.0208, 36.6683) },
        { "合肥", (117.2272, 31.8206) },
        { "南昌", (115.8579, 28.6829) },
        { "昆明", (102.8329, 24.8801) },
        { "福州", (119.2965, 26.0745) },
        { "贵阳", (106.6302, 26.6470) },
        { "长春", (125.3235, 43.8171) },
        { "石家庄", (114.5149, 38.0428) },
        { "太原", (112.5488, 37.8706) },
        { "南宁", (108.3665, 22.8170) },
        { "兰州", (103.8340, 36.0611) },
        { "乌鲁木齐", (87.6168, 43.7928) },
        { "拉萨", (91.1409, 29.6456) },
        { "海口", (110.3497, 20.0458) },
        { "三亚", (109.5117, 18.2528) },
        { "无锡", (120.3093, 31.4913) },
        { "宁波", (121.5483, 29.8683) },
        { "温州", (120.6993, 28.0006) },
        { "常州", (119.9700, 31.8122) },
        { "徐州", (117.1836, 34.2005) },
        { "烟台", (121.4479, 37.4628) },
        { "潍坊", (119.1090, 36.7207) },
        { "淄博", (118.0545, 36.7960) },
        { "临沂", (118.3524, 35.1041) },
        { "保定", (115.4672, 38.8739) },
        { "唐山", (118.0719, 39.6243) },
        { "秦皇岛", (119.5930, 39.9373) },
        { "邯郸", (114.5210, 36.6111) },
    };

    public GeocodingService()
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
        _cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".TraceView", "geocoding_cache");
        Directory.CreateDirectory(_cacheDirectory);
    }

    /// <summary>
    /// Geocode a single location name to coordinates.
    /// </summary>
    public async Task<GeocodingResult?> GeocodeAsync(string locationName, CancellationToken cancellationToken = default)
    {
        // Check memory cache first
        if (_memoryCache.TryGetValue(locationName, out var cached))
        {
            cached.IsCached = true;
            return cached;
        }

        // Check file cache
        var cachedFile = GetCacheFilePath(locationName);
        if (File.Exists(cachedFile))
        {
            try
            {
                var json = await File.ReadAllTextAsync(cachedFile, cancellationToken);
                // Use simple string parsing for cached results to avoid AOT issues
                var result = ParseCachedJson(json, locationName);
                if (result is not null)
                {
                    result.IsCached = true;
                    _memoryCache[locationName] = result;
                    return result;
                }
            }
            catch
            {
                // Ignore cache read errors
            }
        }

        // Try offline common cities first
        if (CommonCities.TryGetValue(locationName, out var coords))
        {
            var result = new GeocodingResult
            {
                Name = locationName,
                Longitude = coords.Longitude,
                Latitude = coords.Latitude,
                DisplayName = locationName,
                IsCached = true
            };
            _memoryCache[locationName] = result;
            SaveToCache(locationName, result);
            return result;
        }

        // Try geo search API
        // Note: The backend server requires lowercase URL encoding, so we convert to lowercase
        // We use string-based JSON parsing to avoid AOT serialization issues
        try
        {
            var encoded = System.Net.WebUtility.UrlEncode(locationName).ToLowerInvariant();
            var url = $"http://192.168.1.100:8088/search?q={encoded}&format=json&limit=1&accept-language=zh";
            
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                
                // Parse API response using string extraction (AOT-safe, no reflection)
                var result = ParseApiResult(json, locationName);
                if (result is not null)
                {
                    _memoryCache[locationName] = result;
                    SaveToCache(locationName, result);
                    return result;
                }
            }
        }
        catch (Exception ex)
        {
            // Log the actual error for debugging
            System.Diagnostics.Debug.WriteLine($"Geocoding failed for '{locationName}': {ex.Message}");
        }

        // Return a placeholder result if geocoding fails
        // User can manually set coordinates
        return null;
    }

    /// <summary>
    /// Parse a cached GeocodingResult JSON without reflection (AOT-safe).
    /// </summary>
    GeocodingResult? ParseCachedJson(string json, string name)
    {
        // Simple manual parsing to avoid AOT issues with JsonElement
        // This parses the JSON we wrote ourselves in SaveToCache
        try
        {
            var result = new GeocodingResult { Name = name };
            
            // Use string-based extraction for key values
            ExtractJsonString(json, "Longitude", out var lonStr);
            ExtractJsonString(json, "Latitude", out var latStr);
            ExtractJsonString(json, "DisplayName", out var displayName);
            ExtractJsonString(json, "PlaceId", out var placeId);
            ExtractJsonString(json, "OsmType", out var osmType);
            ExtractJsonString(json, "OsmId", out var osmId);
            ExtractJsonString(json, "Class", out var cls);
            ExtractJsonString(json, "Type", out var type);
            ExtractJsonString(json, "Importance", out var importance);
            
            result.Longitude = double.TryParse(lonStr, out var lon) ? lon : 0;
            result.Latitude = double.TryParse(latStr, out var lat) ? lat : 0;
            result.DisplayName = displayName ?? name;
            result.PlaceId = placeId ?? string.Empty;
            result.OsmType = osmType ?? string.Empty;
            result.OsmId = long.TryParse(osmId, out var oid) ? oid : 0;
            result.Class = cls ?? string.Empty;
            result.Type = type ?? string.Empty;
            result.Importance = double.TryParse(importance, out var imp) ? imp : 0;
            
            return result;
        }
        catch
        {
            return null;
        }
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

    /// <summary>
    /// Manually set coordinates for a location (for manual correction).
    /// </summary>
    public void SetManualCoordinates(string locationName, double longitude, double latitude)
    {
        var result = new GeocodingResult
        {
            Name = locationName,
            Longitude = longitude,
            Latitude = latitude,
            DisplayName = locationName,
            IsCached = true
        };
        _memoryCache[locationName] = result;
        SaveToCache(locationName, result);
    }

    private string GetCacheFilePath(string locationName)
    {
        var safeName = string.Join("_", locationName.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_cacheDirectory, safeName + ".json");
    }

    private void SaveToCache(string locationName, GeocodingResult result)
    {
        try
        {
            var filePath = GetCacheFilePath(locationName);
            // Write a simple JSON manually to avoid AOT issues
            var json = $"{{\"Name\":\"{EscapeJson(result.Name)}\",\"Longitude\":{result.Longitude},\"Latitude\":{result.Latitude},\"DisplayName\":\"{EscapeJson(result.DisplayName)}\",\"IsCached\":{result.IsCached.ToString().ToLower()},\"PlaceId\":\"{EscapeJson(result.PlaceId)}\",\"OsmType\":\"{EscapeJson(result.OsmType)}\",\"OsmId\":{result.OsmId},\"Class\":\"{EscapeJson(result.Class)}\",\"Type\":\"{EscapeJson(result.Type)}\",\"Importance\":{result.Importance}}}";
            File.WriteAllText(filePath, json);
        }
        catch
        {
            // Ignore cache write errors
        }
    }

    static string EscapeJson(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}