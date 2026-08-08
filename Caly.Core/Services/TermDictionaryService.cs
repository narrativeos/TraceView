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

namespace Caly.Core.Services;

/// <summary>
/// Manages domain-specific term dictionaries for entity discovery.
/// Dictionaries are loaded from JSON files in Assets/TermDictionaries/
/// and injected into HanLP API requests as entity_categories.
/// </summary>
public class TermDictionaryService
{
    private readonly string _dictionaryDir;

    /// <summary>
    /// Creates the service. Dictionaries are expected in Assets/TermDictionaries/
    /// relative to the application base directory.
    /// </summary>
    public TermDictionaryService(string? baseDir = null)
    {
        _dictionaryDir = baseDir ?? Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Assets", "TermDictionaries");
    }

    /// <summary>
    /// Loads term dictionaries for the specified document profile.
    /// Returns a dictionary ready for injection into HanLPAnalyzeRequest.EntityCategories.
    /// Format: { "category_name": ["term1", "term2", ...] }
    /// </summary>
    /// <param name="profile">Document type profile: "classical", "historical", "legal", "academic", "general", or "all".</param>
    public Dictionary<string, List<string>> LoadDictionariesForProfile(string profile = "general")
    {
        var result = new Dictionary<string, List<string>>();

        var filesToLoad = profile.ToLowerInvariant() switch
        {
            "classical" => new[] { "classical_offices.json", "classical_artifacts.json", "classical_rituals.json" },
            "historical" => new[] { "historical_eras.json", "classical_offices.json", "academic_concepts.json" },
            "legal" => new[] { "legal_terms.json" },
            "academic" => new[] { "academic_concepts.json" },
            "all" => GetAvailableDictionaryFiles(),
            _ => new[] { "general_terms.json" } // "general" or unknown
        };

        foreach (var file in filesToLoad)
        {
            var loaded = LoadDictionaryFile(file);
            foreach (var kvp in loaded)
            {
                if (!result.ContainsKey(kvp.Key))
                    result[kvp.Key] = new List<string>();
                result[kvp.Key].AddRange(kvp.Value);
            }
        }

        // Deduplicate
        foreach (var kvp in result)
        {
            result[kvp.Key] = kvp.Value.Distinct().ToList();
        }

        System.Diagnostics.Debug.WriteLine(
            $"[TermDictionaryService] Loaded {result.Sum(kvp => kvp.Value.Count)} terms in {result.Count} categories for profile '{profile}'");

        return result;
    }

    /// <summary>
    /// Detects the best document profile based on entity type distribution in NLP results.
    /// </summary>
    /// <param name="entityCategoryCounts">Dictionary of entity category → count.</param>
    /// <param name="contentSample">Sample of document content for keyword matching.</param>
    public string DetectProfile(
        IReadOnlyDictionary<string, int> entityCategoryCounts,
        string? contentSample = null)
    {
        int total = entityCategoryCounts.Values.Sum();
        if (total == 0) return "general";

        var locRatio = (double)(entityCategoryCounts.TryGetValue("LOCATION", out var l) ? l : 0) / total;
        var personRatio = (double)(entityCategoryCounts.TryGetValue("PERSON", out var p) ? p : 0) / total;
        var dateRatio = (double)(entityCategoryCounts.TryGetValue("DATE", out var d) ? d : 0) / total;
        var orgRatio = (double)(entityCategoryCounts.TryGetValue("ORGANIZATION", out var o) ? o : 0) / total;

        // Classical literature: high PERSON + LOCATION + DATE, low ORGANIZATION
        if (personRatio > 0.30 && locRatio > 0.20 && dateRatio > 0.10 && orgRatio < 0.05)
            return "classical";

        // Legal documents: high ORGANIZATION, structured language
        if (orgRatio > 0.20)
            return "legal";

        // Historical: high DATE + PERSON
        if (dateRatio > 0.15 && personRatio > 0.20)
            return "historical";

        // Academic: balanced distributions
        if (entityCategoryCounts.ContainsKey("CONCEPT") && entityCategoryCounts["CONCEPT"] > total * 0.15)
            return "academic";

        return "general";
    }

    private string[] GetAvailableDictionaryFiles()
    {
        if (!Directory.Exists(_dictionaryDir))
            return Array.Empty<string>();

        return Directory.GetFiles(_dictionaryDir, "*.json")
            .Select(Path.GetFileName)
            .Where(f => f != null)
            .Cast<string>()
            .ToArray();
    }

    private Dictionary<string, List<string>> LoadDictionaryFile(string fileName)
    {
        var result = new Dictionary<string, List<string>>();
        var filePath = Path.Combine(_dictionaryDir, fileName);

        if (!File.Exists(filePath))
        {
            System.Diagnostics.Debug.WriteLine($"[TermDictionaryService] Dictionary not found: {filePath}");
            return result;
        }

        try
        {
            var json = File.ReadAllText(filePath);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    var category = prop.Name;
                    var terms = new List<string>();

                    if (prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var term in prop.Value.EnumerateArray())
                        {
                            if (term.ValueKind == JsonValueKind.String)
                                terms.Add(term.GetString()!);
                        }
                    }

                    if (terms.Count > 0)
                        result[category] = terms;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TermDictionaryService] Failed to load {fileName}: {ex.Message}");
        }

        return result;
    }
}
