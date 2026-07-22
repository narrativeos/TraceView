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
/// Result of a geocoding attempt, containing either a success result or an error message.
/// </summary>
public class GeocodingAttemptResult
{
    public GeocodingResult? Result { get; set; }
    public string? ErrorMessage { get; set; }
    public GeocodingFailureType FailureType { get; set; } = GeocodingFailureType.Unknown;
    public bool IsSuccess => Result is not null;
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
            var url = $"http://127.0.0.1:8088/search?q={encoded}&format=json&limit=1&accept-language=zh&withevidence=1";

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

            // 解析 category (新) 或 class (旧) 字段
            ExtractJsonString(json, "category", out var category);
            ExtractJsonString(json, "class", out var cls);
            result.Class = category ?? cls ?? string.Empty;

            ExtractJsonString(json, "type", out var type);
            result.Type = type ?? string.Empty;

            ExtractJsonString(json, "importance", out var importance);
            result.Importance = double.TryParse(importance, out var imp) ? imp : 0;

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
    /// Geocode multiple location names sequentially with confidence calculation.
    /// Processes locations one by one to avoid overwhelming the API.
    /// No retry logic - each location is attempted once.
    /// </summary>
    public async Task<Dictionary<string, GeocodingResult>> GeocodeManyWithConfidenceAsync(
        IEnumerable<string> locationNames,
        Action<string>? onProcessing = null,
        Action<GeocodingResult>? onSuccess = null,
        GeocodingFailedCallback? onFailed = null,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, GeocodingResult>();
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
                results[locationName] = attempt.Result!;
                onSuccess?.Invoke(attempt.Result!);
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
    /// </summary>
    static bool IsAmbiguousAbbreviation(string name)
    {
        // 单字符地名通常是简称
        if (name.Length == 1)
            return true;

        // 常见的两字简称/方位描述
        var ambiguousPatterns = new[]
        {
            "京西", "京南", "京北", "京东",      // 北京方位
            "沪上", "沪西", "沪南", "沪北",      // 上海方位
            "渝中", "渝北", "渝南",               // 重庆方位
            "皖中", "皖南", "皖北", "皖西",      // 安徽方位
            "苏南", "苏北", "苏中",               // 江苏方位
            "浙西", "浙东",                       // 浙江方位
            "川西", "川东", "川南", "川北",      // 四川方位
            "滇西", "滇南",                       // 云南方位
            "关中", "陕北", "陕南",               // 陕西方位
            "鄂西", "鄂东",                       // 湖北方位
            "湘西", "湘南", "湘北",               // 湖南方位
            "赣南", "赣北",                       // 江西方位
            "闽西", "闽东",                       // 福建方位
            "岭南", "岭北",                       // 广东方位
            "塞外", "塞北",                       // 北方方位
            "江南", "江北", "江东", "江西",       // 泛区域
            "塞上", "关外",                       // 泛区域
        };

        return ambiguousPatterns.Contains(name);
    }

    /// <summary>
    /// 判断一个地名是否可能是无效名称（非地名）。
    /// </summary>
    static bool IsLikelyNotLocation(string name)
    {
        // 包含数字和特殊字符，可能是编号而非地名
        if (name.Length > 3 && name.Any(c => char.IsDigit(c)) && name.Any(c => c == ')'))
            return true;

        // 纯数字
        if (name.All(c => char.IsDigit(c)))
            return true;

        // 以"号"结尾的短名称（如"第25号"）
        if (name.EndsWith("号") && name.Length <= 6)
            return true;

        return false;
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
            var url = $"http://127.0.0.1:8088/search?q={encoded}&format=json&limit=5&accept-language=zh&withevidence=1";

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

            // 返回置信度最高的结果
            var best = allResults.OrderByDescending(r => r.Confidence).FirstOrDefault();
            return new GeocodingAttemptResult { Result = best };
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

            // 解析 category (新) 或 class (旧) 字段
            ExtractJsonString(json, "category", out var category);
            ExtractJsonString(json, "class", out var cls);
            result.Class = category ?? cls ?? string.Empty;

            ExtractJsonString(json, "type", out var type);
            result.Type = type ?? string.Empty;

            ExtractJsonString(json, "importance", out var importance);
            result.Importance = double.TryParse(importance, out var imp) ? imp : 0;

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
}
