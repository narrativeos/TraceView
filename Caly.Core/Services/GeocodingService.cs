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
using System.Text.Json;
using System.Text.Json.Serialization;
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

/// <summary>
/// Service for geocoding location names to coordinates using Nominatim API.
/// </summary>
public sealed class GeocodingService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _cacheDirectory;
    private readonly Dictionary<string, GeocodingResult> _memoryCache = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

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
                var result = JsonSerializer.Deserialize<GeocodingResult>(json, _jsonOptions);
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

        // Try Nominatim API
        try
        {
            var url = $"https://nominatim.openstreetmap.org/search?q={Uri.EscapeDataString(locationName)}&format=json&limit=1&accept-language=zh";
            
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "TraceView/1.0 (Geocoding Service)");
            
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var results = JsonSerializer.Deserialize<JsonElement[]>(json, _jsonOptions);
                
                if (results is not null && results.Length > 0)
                {
                    var first = results[0];
                    var result = new GeocodingResult
                    {
                        Name = locationName,
                        Longitude = double.Parse(first.GetProperty("lon").GetString() ?? "0"),
                        Latitude = double.Parse(first.GetProperty("lat").GetString() ?? "0"),
                        DisplayName = first.GetProperty("display_name").GetString() ?? locationName,
                        IsCached = false,
                        PlaceId = first.TryGetProperty("place_id", out var pid) ? pid.GetString() ?? string.Empty : string.Empty,
                        OsmType = first.TryGetProperty("osm_type", out var ot) ? ot.GetString() ?? string.Empty : string.Empty,
                        OsmId = first.TryGetProperty("osm_id", out var oid) ? long.Parse(oid.GetString() ?? "0") : 0,
                        Class = first.TryGetProperty("class", out var c) ? c.GetString() ?? string.Empty : string.Empty,
                        Type = first.TryGetProperty("type", out var t) ? t.GetString() ?? string.Empty : string.Empty,
                        Importance = first.TryGetProperty("importance", out var imp) ? double.Parse(imp.GetString() ?? "0") : 0,
                    };

                    // Parse boundingbox if present: [minlat, maxlat, minlon, maxlon]
                    if (first.TryGetProperty("boundingbox", out var bbox) && bbox.ValueKind == JsonValueKind.Array)
                    {
                        var elements = bbox.EnumerateArray().ToList();
                        if (elements.Count >= 4)
                        {
                            result.BoundingBoxMinLat = double.Parse(elements[0].GetString() ?? "0");
                            result.BoundingBoxMaxLat = double.Parse(elements[1].GetString() ?? "0");
                            result.BoundingBoxMinLon = double.Parse(elements[2].GetString() ?? "0");
                            result.BoundingBoxMaxLon = double.Parse(elements[3].GetString() ?? "0");
                        }
                    }
                    
                    _memoryCache[locationName] = result;
                    SaveToCache(locationName, result);
                    return result;
                }
            }
        }
        catch
        {
            // If API call fails, return a default result with the location name
            // The user can manually correct the coordinates later
        }

        // Return a placeholder result if geocoding fails
        // User can manually set coordinates
        return null;
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
            var json = JsonSerializer.Serialize(result, _jsonOptions);
            File.WriteAllText(filePath, json);
        }
        catch
        {
            // Ignore cache write errors
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}