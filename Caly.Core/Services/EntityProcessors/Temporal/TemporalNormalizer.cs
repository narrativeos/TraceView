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
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Caly.Core.Models;
using Caly.Core.Services.EntityProcessors.Temporal.Models;

namespace Caly.Core.Services.EntityProcessors.Temporal;

/// <summary>
/// Normalizes temporal expressions to ISO 8601 format.
///
/// Strategies by type:
/// - Absolute: use HanLP's already-normalized value directly.
/// - Era: look up the era name in the embedded era table, compute Gregorian year.
/// - Relative: record offset metadata, mark as needing anchor.
/// - Range: parse start/end into iso_start/iso_end.
/// - Vague: record as-is with low confidence.
/// </summary>
public class TemporalNormalizer
{
    private readonly Dictionary<string, EraEntry> _eraByName;
    private readonly Dictionary<int, List<string>> _eraByYear;
    private static readonly char[] ChineseDigits = ['一', '二', '三', '四', '五', '六', '七', '八', '九', '十'];

    public TemporalNormalizer()
    {
        _eraByName = new Dictionary<string, EraEntry>(StringComparer.OrdinalIgnoreCase);
        _eraByYear = new Dictionary<int, List<string>>();

        LoadEraData();
    }

    // ── Public API ──────────────────────────────────────────────────

    /// <summary>
    /// Normalize a DATE entity to a TemporalNormalizationResult.
    /// </summary>
    public TemporalNormalizationResult Normalize(SemanticEntity entity)
    {
        var (_, type, _) = TemporalValidator.Validate(entity);
        var text = entity.Text?.Trim() ?? string.Empty;

        return type switch
        {
            TemporalType.Absolute => NormalizeAbsolute(entity),
            TemporalType.Era => NormalizeEra(text),
            TemporalType.Relative => NormalizeRelative(text),
            TemporalType.Range => NormalizeRange(text),
            TemporalType.Vague => new TemporalNormalizationResult
            {
                Type = TemporalType.Vague,
                Precision = TemporalPrecision.Unknown,
                Source = "None",
                Description = text
            },
            _ => new TemporalNormalizationResult
            {
                Type = TemporalType.Vague,
                Precision = TemporalPrecision.Unknown,
                Source = "None"
            }
        };
    }

    // ── Normalization strategies ────────────────────────────────────

    private static TemporalNormalizationResult NormalizeAbsolute(SemanticEntity entity)
    {
        var iso = entity.Normalized;
        var text = entity.Text?.Trim() ?? string.Empty;

        // Handle BC dates
        var bcMatch = Regex.Match(text, @"公元前?(\d+)年?");
        if (bcMatch.Success && int.TryParse(bcMatch.Groups[1].Value, out var bcYear))
        {
            iso = $"-{bcYear:0000}";
        }

        var precision = DeterminePrecision(iso ?? text);

        return new TemporalNormalizationResult
        {
            Iso8601 = iso,
            Type = TemporalType.Absolute,
            Precision = precision,
            Source = string.IsNullOrEmpty(entity.Normalized) ? "Regex" : "HanLP",
            Description = text
        };
    }

    private TemporalNormalizationResult NormalizeEra(string text)
    {
        // Pattern: "{era_name}{year_number}年"
        var match = Regex.Match(text, @"^(\S+?)(元|\d{1,2})年$");
        if (!match.Success)
        {
            return new TemporalNormalizationResult
            {
                Type = TemporalType.Era,
                Precision = TemporalPrecision.Unknown,
                Source = "EraTable",
                Description = $"Unable to parse era expression: {text}"
            };
        }

        var eraName = match.Groups[1].Value;
        var yearStr = match.Groups[2].Value;

        int eraYearNumber;
        if (yearStr == "元")
            eraYearNumber = 1;
        else if (!int.TryParse(yearStr, out eraYearNumber))
            return new TemporalNormalizationResult
            {
                Type = TemporalType.Era,
                Precision = TemporalPrecision.Unknown,
                Source = "EraTable",
                Description = $"Unable to parse year number: {yearStr}"
            };

        // Try exact match first, then try with common suffixes stripped
        if (!_eraByName.TryGetValue(eraName, out var era))
        {
            // Try without "中国" prefix or other common patterns
            var cleaned = eraName.Replace("中国", "").Replace("后", "").Replace("前", "");
            if (cleaned != eraName)
                _eraByName.TryGetValue(cleaned, out era);
        }

        if (era == null)
        {
            return new TemporalNormalizationResult
            {
                Type = TemporalType.Era,
                Precision = TemporalPrecision.Unknown,
                Source = "EraTable",
                Description = $"Unknown era name: {eraName}"
            };
        }

        if (eraYearNumber < 1 || eraYearNumber > era.Years)
        {
            return new TemporalNormalizationResult
            {
                Type = TemporalType.Era,
                Precision = TemporalPrecision.Year,
                Source = "EraTable",
                Description = $"Era year {eraYearNumber} out of range for {eraName} (1-{era.Years})",
                EraName = eraName,
                EraYear = eraYearNumber
            };
        }

        var gregorianYear = era.StartYear + eraYearNumber - 1;

        return new TemporalNormalizationResult
        {
            Iso8601 = gregorianYear.ToString(),
            Type = TemporalType.Era,
            Precision = TemporalPrecision.Year,
            Source = "EraTable",
            Description = $"{era.EmperorZh} {eraName}{yearStr}年 = {gregorianYear}年",
            EraName = eraName,
            EraYear = eraYearNumber
        };
    }

    private static TemporalNormalizationResult NormalizeRelative(string text)
    {
        var offset = new Dictionary<string, object>();

        // Parse relative offset
        var match = Regex.Match(text, @"(\d{1,2})[个]?([天日月年])[之]?([前后])");
        if (match.Success)
        {
            offset["direction"] = match.Groups[3].Value == "前" ? "before" : "after";
            offset["unit"] = match.Groups[2].Value switch
            {
                "天" => "day",
                "日" => "day",
                "月" => "month",
                "年" => "year",
                _ => match.Groups[2].Value
            };
            offset["value"] = int.Parse(match.Groups[1].Value);
        }
        else
        {
            // Named relative times
            offset["direction"] = text switch
            {
                "去年" or "昨日" or "昨天" or "前年" or "大前年" => "before",
                "明年" or "明日" or "明天" or "后年" or "大后年" => "after",
                "今年" or "今日" or "今天" => "current",
                _ => "unknown"
            };
            offset["unit"] = text switch
            {
                "去年" or "今年" or "明年" or "前年" or "后年" or "大前年" or "大后年" => "year",
                "昨日" or "昨天" or "今日" or "今天" or "明日" or "明天" => "day",
                _ => "unknown"
            };
            offset["value"] = text switch
            {
                "前年" => 2, "大前年" => 3,
                "后年" => 2, "大后年" => 3,
                _ => 1
            };
        }

        return new TemporalNormalizationResult
        {
            Type = TemporalType.Relative,
            Precision = offset["unit"]?.ToString() switch
            {
                "day" => TemporalPrecision.Day,
                "month" => TemporalPrecision.Month,
                "year" => TemporalPrecision.Year,
                _ => TemporalPrecision.Unknown
            },
            NeedsAnchor = true,
            Source = "RelativeParser",
            Description = text,
            RelativeOffset = offset
        };
    }

    private static TemporalNormalizationResult NormalizeRange(string text)
    {
        var result = new TemporalNormalizationResult
        {
            Type = TemporalType.Range,
            Source = "RangeParser",
            Description = text
        };

        // Pattern: "2020年至2023年"
        var match = Regex.Match(text, @"(\d{3,4})年?[至到\-–—](\d{3,4})年?");
        if (match.Success)
        {
            result.IsoStart = match.Groups[1].Value;
            result.IsoEnd = match.Groups[2].Value;
            result.Precision = TemporalPrecision.Year;
            return result;
        }

        // Pattern: "上世纪80年代" or "1990年代"
        match = Regex.Match(text, @"上?世纪(\d{2,4})年代");
        if (match.Success)
        {
            var decade = int.Parse(match.Groups[1].Value);
            if (decade < 100)
            {
                var centuryBase = text.Contains("上") ? 1900 : 1900;
                result.IsoStart = (centuryBase + decade).ToString();
                result.IsoEnd = (centuryBase + decade + 9).ToString();
            }
            else
            {
                result.IsoStart = decade.ToString();
                result.IsoEnd = (decade + 9).ToString();
            }
            result.Precision = TemporalPrecision.Decade;
            return result;
        }

        // Pattern: "\d{4}年代"
        match = Regex.Match(text, @"(\d{4})年代");
        if (match.Success)
        {
            var start = int.Parse(match.Groups[1].Value);
            result.IsoStart = start.ToString();
            result.IsoEnd = (start + 9).ToString();
            result.Precision = TemporalPrecision.Decade;
            return result;
        }

        // Pattern: BC range "公元前221至公元前206"
        match = Regex.Match(text, @"公元前(\d+)[至到\-–—]公元前(\d+)");
        if (match.Success)
        {
            result.IsoStart = $"-{match.Groups[2].Value}";
            result.IsoEnd = $"-{match.Groups[1].Value}";
            result.Precision = TemporalPrecision.Year;
            return result;
        }

        return result;
    }

    // ── Helper methods ──────────────────────────────────────────────

    private static TemporalPrecision DeterminePrecision(string isoText)
    {
        if (string.IsNullOrEmpty(isoText))
            return TemporalPrecision.Unknown;

        // ISO 8601 patterns: YYYY-MM-DD, YYYY-MM, YYYY
        if (Regex.IsMatch(isoText, @"^-?\d{4}-\d{2}-\d{2}"))
            return TemporalPrecision.Day;
        if (Regex.IsMatch(isoText, @"^-?\d{4}-\d{2}"))
            return TemporalPrecision.Month;
        if (Regex.IsMatch(isoText, @"^-?\d{4}$"))
            return TemporalPrecision.Year;
        if (Regex.IsMatch(isoText, @"^-?\d{3}$"))
            return TemporalPrecision.Year;  // e.g., "768" (BC or short year)
        return TemporalPrecision.Unknown;
    }

    // ── Era data loading ────────────────────────────────────────────

    private void LoadEraData()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            // Try embedded resource first, then file system
            using var stream = GetEraDataStream(assembly);
            if (stream == null)
                return;

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var eras = JsonSerializer.Deserialize<List<EraEntry>>(stream, options);

            if (eras != null)
            {
                foreach (var era in eras)
                {
                    _eraByName[era.NameZh] = era;
                    foreach (var alt in era.NameAlt)
                        _eraByName[alt] = era;

                    // Build year→era lookup
                    for (int year = era.StartYear; year <= era.EndYear; year++)
                    {
                        if (!_eraByYear.ContainsKey(year))
                            _eraByYear[year] = new List<string>();
                        _eraByYear[year].Add(era.Id);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TemporalNormalizer] Failed to load era data: {ex.Message}");
        }
    }

    private static Stream? GetEraDataStream(Assembly assembly)
    {
        // Try embedded resource
        var resourceName = "Caly.Core.Services.EntityProcessors.Temporal.Data.era.json";
        var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream != null)
            return stream;

        // Fallback: try file system (development mode)
        var baseDir = AppContext.BaseDirectory;
        var filePath = Path.Combine(baseDir, "Services", "EntityProcessors", "Temporal", "Data", "era.json");
        if (File.Exists(filePath))
            return File.OpenRead(filePath);

        // Try relative to current directory
        filePath = Path.Combine("Caly.Core", "Services", "EntityProcessors", "Temporal", "Data", "era.json");
        if (File.Exists(filePath))
            return File.OpenRead(filePath);

        return null;
    }

    // ── Internal model for JSON deserialization ─────────────────────

    private class EraEntry
    {
        public string Id { get; set; } = string.Empty;
        public string NameZh { get; set; } = string.Empty;
        public List<string> NameAlt { get; set; } = new();
        public string DynastyId { get; set; } = string.Empty;
        public string EmperorZh { get; set; } = string.Empty;
        public int StartYear { get; set; }
        public int EndYear { get; set; }
        public int StartMonth { get; set; }
        public int EndMonth { get; set; }
        public int Years { get; set; }
        public string Notes { get; set; } = string.Empty;
    }
}
