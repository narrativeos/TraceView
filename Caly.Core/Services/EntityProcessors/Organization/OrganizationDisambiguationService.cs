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

namespace Caly.Core.Services.EntityProcessors.Organization;

/// <summary>
/// Wikidata REST API client for organization name disambiguation.
///
/// Follows the same pattern as <see cref="GeocodingService"/>:
/// - HttpClient with User-Agent header
/// - <see cref="RateLimiter"/> at 5 requests/second
/// - AOT-safe manual JSON parsing (no reflection-based deserialization)
/// - Non-fatal: failures are logged but don't block the pipeline
/// </summary>
public sealed class OrganizationDisambiguationService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly RateLimiter _rateLimiter;

    public OrganizationDisambiguationService()
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "CalyPdf/1.0 (Wikidata Client)");
        _rateLimiter = new RateLimiter(maxRequestsPerSecond: 5.0);
    }

    /// <summary>
    /// Search Wikidata for organizations matching the given name.
    /// Uses the wbsearchentities API endpoint.
    /// </summary>
    public async Task<List<WikidataOrgResult>> SearchAsync(
        string name,
        CancellationToken ct = default)
    {
        var results = new List<WikidataOrgResult>();

        try
        {
            await _rateLimiter.WaitAsync(ct);

            var encoded = Uri.EscapeDataString(name);
            var url = $"https://www.wikidata.org/w/api.php?action=wbsearchentities" +
                      $"&search={encoded}&language=zh&format=json&limit=5";

            var response = await _httpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();

            // Parse the JSON array manually (AOT-safe)
            results = ParseSearchResults(json, name);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Wikidata] Search failed for '{name}': {ex.Message}");
        }

        return results;
    }

    /// <summary>
    /// Represents a Wikidata search result for an organization.
    /// </summary>
    public class WikidataOrgResult
    {
        public string QId { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public double Confidence { get; set; }
    }

    // ── AOT-safe JSON parsing ──────────────────────────────────────

    private List<WikidataOrgResult> ParseSearchResults(string json, string queryName)
    {
        var results = new List<WikidataOrgResult>();

        // Find the "search" array
        var searchIdx = json.IndexOf("\"search\"", StringComparison.Ordinal);
        if (searchIdx < 0) return results;

        searchIdx = json.IndexOf('[', searchIdx);
        if (searchIdx < 0) return results;

        // Parse each object in the array
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

    private WikidataOrgResult? ParseSearchResultObject(string json, string queryName)
    {
        ExtractJsonString(json, "id", out var id);
        ExtractJsonString(json, "label", out var label);
        ExtractJsonString(json, "description", out var description);

        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(label))
            return null;

        // Calculate simple confidence based on name similarity
        double confidence = 0.5;
        if (label.Contains(queryName) || queryName.Contains(label))
            confidence = 0.8;
        if (string.Equals(label, queryName, StringComparison.OrdinalIgnoreCase))
            confidence = 0.95;

        return new WikidataOrgResult
        {
            QId = id,
            Label = label,
            Description = description ?? string.Empty,
            Confidence = confidence
        };
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
