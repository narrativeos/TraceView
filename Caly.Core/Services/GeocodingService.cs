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
using Caly.Core.Models;

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
    public string Category { get; set; } = string.Empty;     // boundary, place, highway, etc. (新版 Nominatim category 字段)
    public string Type { get; set; } = string.Empty;         // administrative, city, town, etc.
    public string Addresstype { get; set; } = string.Empty;  // 新版 Nominatim addresstype（如 city, state, province）
    public int PlaceRank { get; set; }                       // 行政层级：8=省/州, 12=地级市, 14=区县, 16=城市
    public int AddressRank { get; set; }                     // 地址层级排名
    public double Importance { get; set; }
    public double Confidence { get; set; }                   // 匹配置信度评分 (0.0 - 1.0)
    // Nominatim withevidence 扩展字段
    public string MatchType { get; set; } = string.Empty;    // 匹配类型：name(标准名)/ref(简称)/alias(别名)/like(模糊)/...
    public string MatchedNameTag { get; set; } = string.Empty; // 匹配的 OSM 标签类型
    public string MatchedName { get; set; } = string.Empty;  // 实际匹配的名称
    public bool MatchByHomophone { get; set; }               // 是否通过谐音匹配（API 直接返回）
    public double? BoundingBoxMinLat { get; set; }
    public double? BoundingBoxMaxLat { get; set; }
    public double? BoundingBoxMinLon { get; set; }
    public double? BoundingBoxMaxLon { get; set; }

    /// <summary>
    /// The actual query string sent to Nominatim that produced this result.
    /// May differ from the original location name when context enhancement is applied.
    /// e.g., "北京 北三环" vs original "北三环".
    /// </summary>
    public string QueryText { get; set; } = string.Empty;
}

/// <summary>
/// Failure reason classification for geocoding.
/// </summary>
public enum GeocodingFailureType
{
    /// <summary>
    /// Unknown failure (default).
    /// </summary>
    Unknown,
    /// <summary>
    /// Transient failure - network timeout, API error. Retry may succeed.
    /// </summary>
    Transient,
    /// <summary>
    /// Location not found - the name is too vague, not a real place, or doesn't exist in OSM.
    /// </summary>
    NotFound,
    /// <summary>
    /// Ambiguous name - single character abbreviation or too broad, needs disambiguation.
    /// </summary>
    Ambiguous,
}

    /// <summary>
    /// Result of a geocoding attempt, containing success results (may be multiple) or an error message.
    /// </summary>
    public class GeocodingAttemptResult
    {
        public List<GeocodingResult> Results { get; set; } = new List<GeocodingResult>();
        public GeocodingResult? Result => Results.FirstOrDefault();
        public string? ErrorMessage { get; set; }
        public GeocodingFailureType FailureType { get; set; } = GeocodingFailureType.Unknown;
        public bool IsSuccess => Results.Count > 0;
    }

public sealed class GeocodingService : IDisposable
{
    private readonly HttpClient _httpClient;

    // 最大并发数：同时处理的地理编码请求数
    const int MaxConcurrency = 5;
    // 请求间隔：每个请求之间的最小间隔（毫秒），用于速率限制
    // Nominatim 官方建议至少 1 秒/请求，设置为 1000ms 确保本地服务稳定响应
    const int RateLimitMs = 1000;

    public GeocodingService()
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "CalyPdf/1.0 (Geocoding Client)");
    }

    /// <summary>
    /// Geocode a single location name to coordinates by calling the geo search API.
    /// Always fetches fresh data from the API - no caching.
    /// </summary>
    public async Task<GeocodingResult?> GeocodeAsync(string locationName, CancellationToken cancellationToken = default)
    {
        try
        {
            var encoded = System.Net.WebUtility.UrlEncode(locationName);
            var url = $"http://127.0.0.1:8088/search?q={encoded}&format=json&limit=1&accept-language=zh&withevidence=1&extratags=1";

            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var result = ParseApiResult(json, locationName);
                if (result is not null)
                    return result;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Geocoding failed for '{locationName}': {ex.Message}");
        }

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

        idx = json.IndexOf(':', idx + keyPattern.Length);
        if (idx < 0)
        {
            value = null;
            return;
        }

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
            var start = idx + 1;
            var end = start;
            while (end < json.Length && json[end] != '"')
            {
                if (json[end] == '\\') end++;
                end++;
            }
            value = json.Substring(start, end - start);
        }
        else
        {
            var start = idx;
            var end = idx;
            while (end < json.Length && json[end] != ',' && json[end] != '}')
                end++;
            value = json.Substring(start, end - start).Trim();
        }
    }

    /// <summary>
    /// Extract a boolean value for a given JSON property name.
    /// </summary>
    void ExtractJsonBool(string json, string propertyName, out bool value)
    {
        value = false;
        var keyPattern = $"\"{propertyName}\"";
        var idx = json.IndexOf(keyPattern, StringComparison.Ordinal);
        if (idx < 0)
            return;

        idx = json.IndexOf(':', idx + keyPattern.Length);
        if (idx < 0)
            return;

        idx++;
        while (idx < json.Length && (json[idx] == ' ' || json[idx] == '\t' || json[idx] == '\n' || json[idx] == '\r'))
            idx++;

        if (idx >= json.Length)
            return;

        // Check for "true" or "false"
        if (json[idx] == 't' && idx + 3 < json.Length && json.Substring(idx, 4) == "true")
            value = true;
        else if (json[idx] == 'f' && idx + 4 < json.Length && json.Substring(idx, 5) == "false")
            value = false;
    }

    /// <summary>
    /// Parse the API response JSON (Nominatim format with withevidence) using string extraction (AOT-safe).
    /// </summary>
    GeocodingResult? ParseApiResult(string json, string name)
    {
        try
        {
            if (!json.Contains("{"))
                return null;

            var result = new GeocodingResult { Name = name, IsCached = false };

            ExtractJsonString(json, "lon", out var lonStr);
            ExtractJsonString(json, "lat", out var latStr);

            if (!double.TryParse(lonStr, out var lon) || !double.TryParse(latStr, out var lat))
                return null;

            result.Longitude = lon;
            result.Latitude = lat;

            ExtractJsonString(json, "display_name", out var displayName);
            result.DisplayName = displayName ?? name;

            ExtractJsonString(json, "name", out var nm);
            result.Name = nm ?? name;

            ExtractJsonString(json, "place_id", out var placeId);
            result.PlaceId = placeId ?? string.Empty;

            ExtractJsonString(json, "osm_type", out var osmType);
            result.OsmType = osmType ?? string.Empty;

            ExtractJsonString(json, "osm_id", out var osmId);
            result.OsmId = long.TryParse(osmId, out var oid) ? oid : 0;

            // 解析 category 字段（新版 Nominatim，不再兼容旧版 class）
            ExtractJsonString(json, "category", out var category);
            result.Category = category ?? string.Empty;

            ExtractJsonString(json, "type", out var type);
            result.Type = type ?? string.Empty;

            // 解析新版 Nominatim addresstype 字段
            ExtractJsonString(json, "addresstype", out var addresstype);
            result.Addresstype = addresstype ?? string.Empty;

            ExtractJsonString(json, "importance", out var importance);
            result.Importance = double.TryParse(importance, out var imp) ? imp : 0;

            // 解析 place_rank 字段（Nominatim 标准字段，表示行政级别/搜索排名）
            ExtractJsonString(json, "place_rank", out var placeRankStr);
            result.PlaceRank = int.TryParse(placeRankStr, out var placeRank) ? placeRank : 0;

            ExtractJsonString(json, "address_rank", out var addressRankStr);
            result.AddressRank = int.TryParse(addressRankStr, out var addressRank) ? addressRank : 0;

            // 解析 withevidence 扩展字段
            ExtractJsonString(json, "match_type", out var matchType);
            result.MatchType = matchType ?? string.Empty;

            ExtractJsonString(json, "matched_name_tag", out var matchedNameTag);
            result.MatchedNameTag = matchedNameTag ?? string.Empty;

            ExtractJsonString(json, "matched_name", out var matchedName);
            result.MatchedName = matchedName ?? string.Empty;

            // 解析 match_by_homophone 字段（布尔值）
            ExtractJsonBool(json, "match_by_homophone", out var matchByHomophone);
            result.MatchByHomophone = matchByHomophone;

            // 根据 match_type 计算置信度
            result.Confidence = CalculateConfidenceFromMatchType(result.MatchType);

            ExtractJsonString(json, "boundingbox", out var bboxStr);
            if (!string.IsNullOrEmpty(bboxStr))
            {
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
    /// 根据 Nominatim 返回的 match_type 计算置信度。
    /// - "name": 标准名精确匹配，置信度 1.0
    /// - "ref": 简称/引用匹配，置信度 0.0（由 match_type 标注）
    /// - "alias": 别名匹配，置信度 0.0（由 match_type 标注）
    /// - "like": 模糊匹配，置信度 0.0（由 match_type 标注）
    /// - 其他/空: 置信度 0.0
    /// </summary>
    static double CalculateConfidenceFromMatchType(string matchType)
    {
        return matchType switch
        {
            "name" => 1.0,     // 标准名精确匹配
            _ => 0.0            // 其他匹配类型由 match_type 标注，置信度为 0
        };
    }

    /// <summary>
    /// Geocode multiple location names sequentially with per-location callbacks.
    /// </summary>
    public async Task<Dictionary<string, GeocodingResult>> GeocodeManyAsync(
        IEnumerable<string> locationNames,
        Action<GeocodingResult>? onSuccess = null,
        Action<string, string>? onFailed = null,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, GeocodingResult>();
        var distinctNames = locationNames.Distinct().ToList();

        await Task.Run(async () =>
        {
            for (int i = 0; i < distinctNames.Count; i++)
            {
                var name = distinctNames[i];
                cancellationToken.ThrowIfCancellationRequested();

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

                // Rate limiting: wait between requests
                if (i < distinctNames.Count - 1)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(RateLimitMs), cancellationToken).ConfigureAwait(false);
                }
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
            onSuccess: null,
            onFailed: null,
            cancellationToken);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    /// <summary>
    /// Callback for geocoding failure with detailed failure type information.
    /// </summary>
    public delegate void GeocodingFailedCallback(string name, string error, GeocodingFailureType failureType);

    /// <summary>
    /// Callback for geocoding success with multiple results.
    /// </summary>
    public delegate void GeocodingSuccessCallback(string name, List<GeocodingResult> results);

    /// <summary>
    /// Geocode multiple location names sequentially with confidence calculation.
    /// Processes locations one by one to avoid overwhelming the API.
    /// No retry logic - each location is attempted once.
    /// Returns all results (up to 5 per location).
    /// </summary>
    public async Task<Dictionary<string, List<GeocodingResult>>> GeocodeManyWithConfidenceAsync(
        IEnumerable<string> locationNames,
        Action<string>? onProcessing = null,
        GeocodingSuccessCallback? onSuccess = null,
        GeocodingFailedCallback? onFailed = null,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, List<GeocodingResult>>();
        var distinctNames = locationNames.Distinct().ToList();

        for (int i = 0; i < distinctNames.Count; i++)
        {
            var locationName = distinctNames[i];
            cancellationToken.ThrowIfCancellationRequested();

            // Mark as processing
            onProcessing?.Invoke(locationName);

            // Process single location
            var attempt = await GeocodeWithConfidenceAsync(locationName, cancellationToken).ConfigureAwait(false);

            // Result update
            if (attempt.IsSuccess)
            {
                results[locationName] = attempt.Results;
                onSuccess?.Invoke(locationName, attempt.Results);
            }
            else
            {
                onFailed?.Invoke(locationName, attempt.ErrorMessage ?? "Geocoding failed", attempt.FailureType);
            }

            // Rate limiting between requests
            await Task.Delay(TimeSpan.FromMilliseconds(RateLimitMs), cancellationToken).ConfigureAwait(false);
        }

        return results;
    }

    /// <summary>
    /// 判断一个地名是否为单字简称（如：京、沪、湘、粤等），需要消歧。
    /// 委托给 LocationValidator。
    /// </summary>
    static bool IsAmbiguousAbbreviation(string name)
    {
        return Utilities.LocationValidator.IsAmbiguousAbbreviation(name);
    }

    /// <summary>
    /// 判断一个地名是否可能是无效名称（非地名）。
    /// 委托给 LocationValidator。
    /// </summary>
    static bool IsLikelyNotLocation(string name)
    {
        return Utilities.LocationValidator.IsLikelyNotLocation(name);
    }

    /// <summary>
    /// 多结果地理编码（用于置信度计算）。
    /// 使用 Nominatim 的 withevidence 参数，直接从 API 获取 match_type/matched_name 等匹配证据。
    /// 返回置信度最高的结果，包含详细错误信息和失败类型。
    /// </summary>
    public async Task<GeocodingAttemptResult> GeocodeWithConfidenceAsync(
        string locationName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var encoded = System.Net.WebUtility.UrlEncode(locationName);
            var url = $"http://127.0.0.1:8088/search?q={encoded}&format=json&limit=5&accept-language=zh&withevidence=1&extratags=1";

            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new GeocodingAttemptResult
                {
                    ErrorMessage = $"API 错误: HTTP {(int)response.StatusCode}",
                    FailureType = GeocodingFailureType.Transient
                };
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            // 解析所有结果
            var allResults = ParseAllApiResults(json, locationName);
            
            // 调试日志：输出原始解析结果数量
            System.Diagnostics.Debug.WriteLine(
                $"[Geo] ParseAllApiResults '{locationName}': parsed {allResults.Count} results from API");

            if (allResults.Count == 0)
            {
                // 分类失败类型
                var failureType = ClassifyFailureType(locationName);
                var errorMessage = GetFailureMessage(locationName, failureType);

                return new GeocodingAttemptResult
                {
                    ErrorMessage = errorMessage,
                    FailureType = failureType
                };
            }

            // 返回所有结果（按置信度排序）
            var sorted = allResults.OrderByDescending(r => r.Confidence).ToList();
            foreach (var r in sorted)
                r.QueryText = locationName;
            return new GeocodingAttemptResult { Results = sorted };
        }
        catch (OperationCanceledException)
        {
            return new GeocodingAttemptResult
            {
                ErrorMessage = "请求已取消",
                FailureType = GeocodingFailureType.Transient
            };
        }
        catch (System.TimeoutException)
        {
            return new GeocodingAttemptResult
            {
                ErrorMessage = "网络错误: 请求超时 (10秒)",
                FailureType = GeocodingFailureType.Transient
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Geocoding with confidence failed for '{locationName}': {ex.Message}");
            return new GeocodingAttemptResult
            {
                ErrorMessage = $"网络错误: {ex.Message}",
                FailureType = GeocodingFailureType.Transient
            };
        }
    }

    /// <summary>
    /// 根据地名特征分类失败类型。
    /// </summary>
    static GeocodingFailureType ClassifyFailureType(string name)
    {
        // 判断是否为无效名称
        if (IsLikelyNotLocation(name))
            return GeocodingFailureType.NotFound;

        // 判断是否为单字简称
        if (IsAmbiguousAbbreviation(name))
            return GeocodingFailureType.Ambiguous;

        // 其他情况：可能是太泛或者确实不存在
        // 很短的名称通常太泛
        if (name.Length <= 2)
            return GeocodingFailureType.Ambiguous;

        return GeocodingFailureType.NotFound;
    }

    /// <summary>
    /// 根据失败类型生成人类可读的错误信息。
    /// </summary>
    static string GetFailureMessage(string name, GeocodingFailureType failureType)
    {
        return failureType switch
        {
            GeocodingFailureType.Transient => "网络错误，请重试",
            GeocodingFailureType.Ambiguous => $"名称歧义: '{name}' 可能是简称或方位描述，无法精确定位",
            GeocodingFailureType.NotFound => $"地点未找到: '{name}' 可能不是标准地名或不存在于OSM数据中",
            _ => $"地点未找到: '{name}'"
        };
    }

    /// <summary>
    /// 解析 API 返回的所有结果（JSON 数组格式）。
    /// </summary>
    List<GeocodingResult> ParseAllApiResults(string json, string name)
    {
        var results = new List<GeocodingResult>();

        try
        {
            if (!json.Contains("["))
                return results;

            var objects = ExtractJsonObjectsFromArray(json);
            foreach (var objJson in objects)
            {
                var result = ParseSingleResult(objJson, name);
                if (result is not null)
                    results.Add(result);
            }
        }
        catch
        {
        }

        return results;
    }

    /// <summary>
    /// 从 JSON 数组中提取每个 JSON 对象字符串。
    /// </summary>
    List<string> ExtractJsonObjectsFromArray(string json)
    {
        var objects = new List<string>();
        int depth = 0;
        int start = -1;
        bool inString = false;

        for (int i = 0; i < json.Length; i++)
        {
            char c = json[i];

            if (inString)
            {
                if (c == '\\' && i + 1 < json.Length)
                {
                    i++;
                    continue;
                }
                if (c == '"')
                    inString = false;
                continue;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (c == '{')
            {
                if (depth == 0)
                    start = i;
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0 && start >= 0)
                {
                    objects.Add(json.Substring(start, i - start + 1));
                    start = -1;
                }
            }
        }

        return objects;
    }

    /// <summary>
    /// 解析单个 JSON 对象为一个 GeocodingResult（含 withevidence 字段）。
    /// </summary>
    GeocodingResult? ParseSingleResult(string json, string name)
    {
        try
        {
            if (!json.Contains("{"))
                return null;

            var result = new GeocodingResult { Name = name, IsCached = false };

            ExtractJsonString(json, "lon", out var lonStr);
            ExtractJsonString(json, "lat", out var latStr);

            if (!double.TryParse(lonStr, out var lon) || !double.TryParse(latStr, out var lat))
                return null;

            result.Longitude = lon;
            result.Latitude = lat;

            ExtractJsonString(json, "display_name", out var displayName);
            result.DisplayName = displayName ?? name;

            ExtractJsonString(json, "name", out var nm);
            result.Name = nm ?? name;

            ExtractJsonString(json, "place_id", out var placeId);
            result.PlaceId = placeId ?? string.Empty;

            ExtractJsonString(json, "osm_type", out var osmType);
            result.OsmType = osmType ?? string.Empty;

            ExtractJsonString(json, "osm_id", out var osmId);
            result.OsmId = long.TryParse(osmId, out var oid) ? oid : 0;

            // 解析 category 字段（新版 Nominatim，不再兼容旧版 class）
            ExtractJsonString(json, "category", out var category);
            result.Category = category ?? string.Empty;

            ExtractJsonString(json, "type", out var type);
            result.Type = type ?? string.Empty;

            // 解析新版 Nominatim addresstype 字段
            ExtractJsonString(json, "addresstype", out var addresstype);
            result.Addresstype = addresstype ?? string.Empty;

            ExtractJsonString(json, "importance", out var importance);
            result.Importance = double.TryParse(importance, out var imp) ? imp : 0;

            // 解析 place_rank 字段（Nominatim 标准字段）
            ExtractJsonString(json, "place_rank", out var placeRankStr2);
            result.PlaceRank = int.TryParse(placeRankStr2, out var placeRank2) ? placeRank2 : 0;

            ExtractJsonString(json, "address_rank", out var addressRankStr);
            result.AddressRank = int.TryParse(addressRankStr, out var addressRank) ? addressRank : 0;

            // 解析 withevidence 扩展字段
            ExtractJsonString(json, "match_type", out var matchType);
            result.MatchType = matchType ?? string.Empty;

            ExtractJsonString(json, "matched_name_tag", out var matchedNameTag);
            result.MatchedNameTag = matchedNameTag ?? string.Empty;

            ExtractJsonString(json, "matched_name", out var matchedName);
            result.MatchedName = matchedName ?? string.Empty;

            // 解析 match_by_homophone 字段（布尔值）
            ExtractJsonBool(json, "match_by_homophone", out var matchByHomophone2);
            result.MatchByHomophone = matchByHomophone2;

            // 根据 match_type 计算置信度
            result.Confidence = CalculateConfidenceFromMatchType(result.MatchType);

            ExtractJsonString(json, "boundingbox", out var bboxStr);
            if (!string.IsNullOrEmpty(bboxStr))
            {
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
    /// Geocode a single location with optional context enhancement.
    /// When context is provided, builds an enhanced query string and applies
    /// post-query filtering/reranking based on the strategy.
    /// </summary>
    public async Task<GeocodingAttemptResult> GeocodeWithEnhancementAsync(
        string locationName,
        GeocodingStrategy? strategy = null,
        GeocodingBbox? parentBbox = null,
        CancellationToken cancellationToken = default)
    {
        // Build the query string based on strategy
        string queryName = locationName;
        if (strategy?.QueryEnhancement == QueryEnhancement.TextAugment ||
            strategy?.QueryEnhancement == QueryEnhancement.SpatialBias)
        {
            if (!string.IsNullOrWhiteSpace(strategy.ContextLocationName))
            {
                queryName = GeocodingEnhancementStrategy.BuildEnhancedQuery(
                    locationName, strategy.ContextLocationName);
            }
        }

        // For coreference normalization: use canonical name directly
        if (strategy?.QueryEnhancement == QueryEnhancement.TextAugment &&
            strategy.ResultFiltering == ResultFiltering.None &&
            !string.IsNullOrWhiteSpace(strategy.ContextLocationName))
        {
            queryName = strategy.ContextLocationName;
        }

        GeocodingAttemptResult result;
        try
        {
            var encoded = System.Net.WebUtility.UrlEncode(queryName);
            var url = $"http://127.0.0.1:8088/search?q={encoded}&format=json&limit=5&accept-language=zh&withevidence=1&extratags=1";

            // Add viewbox if we have parent bbox (SpatialBias)
            if (parentBbox != null && strategy?.QueryEnhancement == QueryEnhancement.SpatialBias)
            {
                url += $"&viewbox={parentBbox.MinLon},{parentBbox.MaxLat},{parentBbox.MaxLon},{parentBbox.MinLat}&bounded=1";
            }

            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new GeocodingAttemptResult
                {
                    ErrorMessage = $"API 错误: HTTP {(int)response.StatusCode}",
                    FailureType = GeocodingFailureType.Transient
                };
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var allResults = ParseAllApiResults(json, queryName);

            // Stamp the actual query text on all results
            foreach (var r in allResults)
                r.QueryText = queryName;

            if (allResults.Count == 0)
            {
                // If enhanced query failed, try fallback with original name
                if (queryName != locationName)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Geo] Enhanced query '{queryName}' returned no results, falling back to '{locationName}'");
                    var fallback = await GeocodeWithConfidenceAsync(locationName, cancellationToken);
                    // Stamp fallback results with the fallback query
                    foreach (var r in fallback.Results)
                        r.QueryText = locationName;
                    return fallback;
                }

                var failureType = ClassifyFailureType(locationName);
                return new GeocodingAttemptResult
                {
                    ErrorMessage = GetFailureMessage(locationName, failureType),
                    FailureType = failureType
                };
            }

            result = new GeocodingAttemptResult
            {
                Results = allResults.OrderByDescending(r => r.Confidence).ToList()
            };
        }
        catch (OperationCanceledException)
        {
            return new GeocodingAttemptResult
            {
                ErrorMessage = "请求已取消",
                FailureType = GeocodingFailureType.Transient
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Enhanced geocoding failed for '{locationName}': {ex.Message}");
            return new GeocodingAttemptResult
            {
                ErrorMessage = $"网络错误: {ex.Message}",
                FailureType = GeocodingFailureType.Transient
            };
        }

        // Apply post-query filtering/reranking
        if (strategy != null && strategy.ResultFiltering != ResultFiltering.None && result.IsSuccess)
        {
            result = ApplyPostFilter(result, locationName, strategy);
        }

        return result;
    }

    /// <summary>
    /// Applies post-query filtering and confidence adjustment based on the strategy.
    /// </summary>
    private static GeocodingAttemptResult ApplyPostFilter(
        GeocodingAttemptResult rawResult,
        string locationName,
        GeocodingStrategy strategy)
    {
        var contextName = strategy.ContextLocationName;
        if (string.IsNullOrWhiteSpace(contextName))
            return rawResult;

        var roleForAdjustment = strategy.ResultFiltering == ResultFiltering.StrictParentFilter
            ? LocationSyntacticRole.Attributive
            : LocationSyntacticRole.Object;

        var (matchBoost, mismatchPenalty) = GeocodingEnhancementStrategy.GetConfidenceAdjustment(roleForAdjustment);

        var filteredResults = new List<GeocodingResult>();
        var discardedCount = 0;

        foreach (var r in rawResult.Results)
        {
            bool matchesContext = MatchesParentContext(r, contextName);

            if (strategy.ResultFiltering == ResultFiltering.StrictParentFilter)
            {
                if (matchesContext)
                {
                    r.Confidence = Math.Min(1.0, r.Confidence + matchBoost);
                    filteredResults.Add(r);
                }
                else
                {
                    discardedCount++;
                    System.Diagnostics.Debug.WriteLine(
                        $"[Geo] StrictParentFilter: discarded '{r.DisplayName}' (doesn't match parent '{contextName}')");
                }
            }
            else // ParentAreaBoost
            {
                r.Confidence = Math.Max(0.0, Math.Min(1.0,
                    r.Confidence + (matchesContext ? matchBoost : mismatchPenalty)));
                filteredResults.Add(r);
            }
        }

        filteredResults = filteredResults.OrderByDescending(r => r.Confidence).ToList();

        System.Diagnostics.Debug.WriteLine(
            $"[Geo] Post-filter for '{locationName}': {rawResult.Results.Count} raw → {filteredResults.Count} filtered (discarded {discardedCount}, strategy={strategy.ResultFiltering})");

        return new GeocodingAttemptResult { Results = filteredResults };
    }

    /// <summary>
    /// Checks whether a geocoding result matches the parent context location.
    /// </summary>
    private static bool MatchesParentContext(GeocodingResult result, string parentName)
    {
        if (string.IsNullOrWhiteSpace(parentName))
            return false;

        var displayName = result.DisplayName ?? string.Empty;

        if (displayName.Contains(parentName, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrEmpty(result.MatchedName) &&
            result.MatchedName.Contains(parentName, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <summary>
    /// Bounding box for spatial filtering in geocoding queries.
    /// </summary>
    public class GeocodingBbox
    {
        public double MinLat { get; set; }
        public double MaxLat { get; set; }
        public double MinLon { get; set; }
        public double MaxLon { get; set; }

        public static GeocodingBbox? FromResult(GeocodingResult? result)
        {
            if (result?.BoundingBoxMinLat == null || result?.BoundingBoxMaxLat == null ||
                result?.BoundingBoxMinLon == null || result?.BoundingBoxMaxLon == null)
                return null;

            return new GeocodingBbox
            {
                MinLat = result.BoundingBoxMinLat.Value,
                MaxLat = result.BoundingBoxMaxLat.Value,
                MinLon = result.BoundingBoxMinLon.Value,
                MaxLon = result.BoundingBoxMaxLon.Value,
            };
        }
    }
}
