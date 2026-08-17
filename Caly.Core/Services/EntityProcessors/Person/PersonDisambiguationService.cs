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
using Caly.Core.Utilities;

namespace Caly.Core.Services.EntityProcessors.Person;

/// <summary>
/// Wikidata REST API client for person name disambiguation.
///
/// Follows the same pattern as <see cref="OrganizationDisambiguationService"/>:
/// - HttpClient with User-Agent header
/// - <see cref="RateLimiter"/> at 5 requests/second
/// - AOT-safe manual JSON parsing (no reflection-based deserialization)
/// - Non-fatal: failures are logged but don't block the pipeline
/// - Filters results to human entities only (instance of Q5: human)
/// </summary>
public sealed class PersonDisambiguationService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly RateLimiter _rateLimiter;

    public PersonDisambiguationService()
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "CalyPdf/1.0 (Wikidata Client)");
        _rateLimiter = new RateLimiter(maxRequestsPerSecond: 5.0);
    }

    /// <summary>
    /// Wikidata search result for a person entity.
    /// </summary>
    public class WikidataPersonResult
    {
        public string QId { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public double Confidence { get; set; }
    }

    /// <summary>
    /// Search Wikidata for persons matching the given name.
    /// Uses the wbsearchentities API endpoint with human entity filtering.
    /// </summary>
    public async Task<List<WikidataPersonResult>> SearchAsync(
        string name,
        CancellationToken ct = default)
    {
        var results = new List<WikidataPersonResult>();

        try
        {
            await _rateLimiter.WaitAsync(ct);

            var encoded = Uri.EscapeDataString(name);
            var url = "https://www.wikidata.org/w/api.php?action=wbsearchentities" +
                      "&search=" + encoded + "&language=zh&format=json&limit=5" +
                      "&type=item&origin=*";

            var response = await _httpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            results = ParseSearchResults(json, name);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PersonDisambiguationService] Search failed for '{name}': {ex.Message}");
        }

        return results;
    }

    private List<WikidataPersonResult> ParseSearchResults(string json, string queryName)
    {
        var results = new List<WikidataPersonResult>();
        var searchIdx = json.IndexOf("\"search\"");
        if (searchIdx < 0) return results;

        searchIdx = json.IndexOf('[', searchIdx);
        if (searchIdx < 0) return results;

        var pos = searchIdx + 1;
        while (pos < json.Length)
        {
            if (json[pos] == ']') break;
            if (json[pos] == '{')
            {
                var endObj = FindMatchingBrace(json, pos);
                if (endObj < 0) break;

                var obj = json.Substring(pos, endObj - pos + 1);
                var result = ParseSearchResultObject(obj, queryName);
                if (result != null)
                    results.Add(result);

                pos = endObj + 1;
            }
            else
            {
                pos++;
            }
        }

        return results;
    }

    private WikidataPersonResult? ParseSearchResultObject(string json, string queryName)
    {
        ExtractJsonString(json, "id", out var id);
        ExtractJsonString(json, "label", out var label);
        ExtractJsonString(json, "description", out var description);

        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(label))
            return null;

        if (!IsLikelyHumanEntity(label, description))
            return null;

        double confidence = 0.5;
        if (label.Contains(queryName) || queryName.Contains(label))
            confidence = 0.8;
        if (string.Equals(label, queryName, StringComparison.OrdinalIgnoreCase))
            confidence = 0.95;

        if (confidence >= 0.9 && IsHumanDescription(description))
            confidence = Math.Min(confidence + 0.03, 0.98);

        return new WikidataPersonResult
        {
            QId = id,
            Label = label,
            Description = description ?? string.Empty,
            Confidence = confidence
        };
    }

    private static bool IsLikelyHumanEntity(string label, string? description)
    {
        if (!string.IsNullOrEmpty(description))
        {
            var humanKeywords = new[]
            {
                "人", "演员", "导演", "作家", "画家", "音乐家", "科学家", "政治家",
                "运动员", "歌手", "诗人", "哲学家", "将军", "皇帝", "皇后",
                "actor", "director", "writer", "artist", "musician", "scientist",
                "politician", "athlete", "singer", "poet", "philosopher", "general",
                "emperor", "empress", "human", "person"
            };
            foreach (var keyword in humanKeywords)
            {
                if (description.Contains(keyword))
                    return true;
            }
        }

        if (label.Length >= 2 && label.Length <= 4)
        {
            bool allChinese = label.All(c => c >= 0x4E00 && c <= 0x9FFF);
            if (allChinese)
                return true;
        }

        if (label.All(c => char.IsLetter(c) || c == ' ' || c == '.' || c == '-'))
        {
            var words = label.Split(new[] { ' ', '.', '-' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length >= 2 && words.Length <= 4)
                return true;
        }

        return false;
    }

    private static bool IsHumanDescription(string? description)
    {
        if (string.IsNullOrEmpty(description))
            return false;

        var humanTerms = new[]
        {
            "人", "人物", "演员", "导演", "作家", "画家", "音乐家", "科学家",
            "政治家", "运动员", "歌手", "诗人", "哲学家", "将军", "皇帝",
            "actor", "director", "writer", "artist", "musician", "scientist",
            "politician", "athlete", "singer", "poet", "philosopher", "general"
        };
        return humanTerms.Any(term => description.Contains(term));
    }

    private static int FindMatchingBrace(string json, int start)
    {
        if (start >= json.Length || json[start] != '{')
            return -1;

        int depth = 0;
        for (int i = start; i < json.Length; i++)
        {
            if (json[i] == '{') depth++;
            else if (json[i] == '}')
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    private static void ExtractJsonString(string json, string propertyName, out string? value)
    {
        var keyPattern = $"\"{propertyName}\"";
        var idx = json.IndexOf(keyPattern, StringComparison.Ordinal);
        if (idx < 0) { value = null; return; }

        idx = json.IndexOf(':', idx + keyPattern.Length);
        if (idx < 0) { value = null; return; }

        idx++;
        while (idx < json.Length && (json[idx] == ' ' || json[idx] == '\t' || json[idx] == '\n' || json[idx] == '\r'))
            idx++;

        if (idx >= json.Length) { value = null; return; }

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

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}