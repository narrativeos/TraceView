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
using System.Text.RegularExpressions;
using Caly.Core.Models;
using Caly.Core.Models.EntityProcessors;

namespace Caly.Core.Services.EntityProcessors.Term;

/// <summary>
/// Validates whether a semantic entity qualifies as a domain-specific term.
/// Filters out common words, noise, and entities that are too generic.
/// 
/// Category-aware: NUMBER/DATE are almost never domain terms; MATERIAL has
/// special single-char rules for elements/alloys; UNKNOWN needs stricter filtering.
/// </summary>
public partial class TermValidator
{
    // ── Category-level filtering rules ──

    /// <summary>Categories that are almost never domain terms (numeric/date codes).</summary>
    private static readonly HashSet<string> NonTermCategories = new(StringComparer.Ordinal)
    {
        "NUMBER",   // Pure numbers, percentage values, spec codes
        "DATE",      // Year ranges, dates — not conceptual terms
    };

    /// <summary>Categories that need domain-aware single-char validation.</summary>
    private static readonly HashSet<string> DomainSingleCharCategories = new(StringComparer.Ordinal)
    {
        "MATERIAL",    // Single-char elements: 钢, 铁, 铜, 铝, 钛, 镍, ...
        "STANDARD",    // Single-char standards may exist in context
    };

    /// <summary>Categories that are always valid as terms (high-quality NER).</summary>
    private static readonly HashSet<string> AlwaysTermCategories = new(StringComparer.Ordinal)
    {
        "PERSON",
        "ORGANIZATION",
        "LOCATION",
    };

    /// <summary>Valid single-character material/element names.</summary>
    private static readonly HashSet<string> ValidSingleCharMaterials = new(StringComparer.Ordinal)
    {
        "钢", "铁", "铜", "铝", "钛", "镍", "锌", "锡", "铅", "镁",
        "金", "银", "铂", "钴", "铬", "锰", "钨", "钼", "钒", "铌",
        "硅", "碳", "硫", "磷", "硼", "氮", "氢", "氧",
    };

    // ── Noise patterns ──

    /// <summary>LaTeX commands and math artifacts.</summary>
    [GeneratedRegex(@"^\\|\\frac|\\mathrm|\\mathbf|\\begin|\\array|\\bigcirc|\\mu|\\sigma|\\times|\\cdot|\\sum|\\int")]
    private static partial Regex LatexPattern();

    /// <summary>HTML/XML tag fragments.</summary>
    [GeneratedRegex(@"^</?[a-z]+\s*>?$|^</?sup>?$|^</?sub>?$", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlFragmentPattern();

    /// <summary>Year ranges like "1234—2020" or "2018—2023".</summary>
    [GeneratedRegex(@"^\d{4}[—\-–]\d{4}$")]
    private static partial Regex YearRangePattern();

    /// <summary>Pure numeric/symbol patterns.</summary>
    [GeneratedRegex(@"^[\d\s\-\.,;:±×÷≤≥<>%‰＃＠＆＊～]+$")]
    private static partial Regex PureNumericPattern();

    /// <summary>Standard code patterns: GB/T, JB/T, NB/T, ISO, ASTM, etc.</summary>
    [GeneratedRegex(@"^[A-Z]{2,5}[\s/]?[A-Z]?[\s/]?[A-Z]?$|^[A-Z]+/[A-Z]+$")]
    private static partial Regex StandardCodePattern();

    /// <summary>Single ASCII character (almost never a term in Chinese docs).</summary>
    [GeneratedRegex(@"^[A-Za-z]$")]
    private static partial Regex SingleAsciiPattern();

    /// <summary>ASCII-only text (likely English/OCR fragments in Chinese docs).</summary>
    [GeneratedRegex(@"^[A-Za-z][A-Za-z\s/\-]+$")]
    private static partial Regex AsciiOnlyPattern();

    /// <summary>Known OCR/HTML fragment words that are never domain terms.</summary>
    private static readonly HashSet<string> KnownNoiseWords = new(StringComparer.Ordinal)
    {
        "sup", "sub", "</sup", "</sub", "<sup>", "<sub>",
        "Metal", "times", "frac", "mathrm", "mathbf", "textrm",
        "begin", "array", "bigcirc", "cdot", "sum", "int",
    };

    // Common Chinese words that should never be considered domain-specific terms
    private static readonly HashSet<string> CommonWords = new(StringComparer.Ordinal)
    {
        "可以", "能够", "应该", "必须", "需要", "可能", "已经", "正在", "将要",
        "因为", "所以", "但是", "虽然", "如果", "而且", "或者", "并且",
        "这个", "那个", "这些", "那些", "这里", "那里", "什么", "怎么",
        "一个", "一种", "所有", "一些", "很多", "许多", "每个", "各个",
        "进行", "使用", "通过", "根据", "关于", "对于", "作为", "成为",
        "表示", "说明", "认为", "发现", "研究", "分析", "结果", "结论",
        "时间", "情况", "问题", "方法", "方式", "过程", "原因", "条件",
        "不同", "相同", "重要", "主要", "基本", "一般", "特别", "非常",
        "第一", "第二", "第三", "首先", "其次", "最后", "另外", "此外",
    };

    /// <summary>
    /// Minimum document frequency for a term to be considered valid.
    /// Terms appearing only once are likely noise unless they have an explicit definition.
    /// </summary>
    public int MinFrequency { get; set; } = 2;

    /// <summary>
    /// Whether to allow single-character terms for domain-specific categories (MATERIAL, etc.).
    /// Default: true (materials like 钢, 铁 are valid terms).
    /// </summary>
    public bool AllowDomainSingleChar { get; set; } = true;

    /// <summary>
    /// Validates a semantic entity as a potential domain-specific term.
    /// </summary>
    public EntityValidationResult Validate(
        SemanticEntity entity,
        int definitionCount = 0,
        int totalOccurrences = 1)
    {
        var text = entity.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return EntityValidationResult.Invalid("Empty text");

        var category = entity.Category ?? "UNKNOWN";

        // ── P1.1: Category-level exclusion ──
        if (NonTermCategories.Contains(category))
            return EntityValidationResult.Invalid($"Non-term category: {category}");

        // ── P1.2: Noise pattern detection ──
        var noiseReason = DetectNoise(text);
        if (noiseReason != null)
            return EntityValidationResult.Invalid($"Noise: {noiseReason}");

        // ── P1.3: Length check (category-aware) ──
        if (text.Length <= 1)
        {
            if (AllowDomainSingleChar && DomainSingleCharCategories.Contains(category))
            {
                if (ValidSingleCharMaterials.Contains(text))
                    return EntityValidationResult.Valid($"Single-char domain term: {text}");
                // Other single-char in domain category — ambiguous
                if (totalOccurrences >= 3)
                    return EntityValidationResult.Ambiguous($"Unrecognized single-char in {category}");
                return EntityValidationResult.Invalid($"Single char in {category}, freq < 3");
            }

            // Always-term categories: single char may be surname or abbreviation
            if (AlwaysTermCategories.Contains(category) && definitionCount > 0)
                return EntityValidationResult.Ambiguous("Single-char named entity with definition");

            return EntityValidationResult.Invalid("Single character without domain support");
        }

        // ── Common word check ──
        if (CommonWords.Contains(text))
            return EntityValidationResult.Invalid("Common function word");

        // ── P1.3: Always-term categories pass automatically ──
        if (AlwaysTermCategories.Contains(category))
            return EntityValidationResult.Valid($"Named entity: {category}");

        // ── Definition boost ──
        if (definitionCount > 0)
            return EntityValidationResult.Valid($"Has explicit definition ({definitionCount})");

        // ── Frequency check ──
        if (totalOccurrences < MinFrequency)
            return EntityValidationResult.Ambiguous($"Low frequency ({totalOccurrences})");

        // ── Domain categories: MATERIAL, STANDARD, PRODUCT, CONCEPT ──
        if (category is "MATERIAL" or "STANDARD" or "PRODUCT" or "CONCEPT"
            or "FACILITY" or "INSTITUTION" or "EVENT")
        {
            return EntityValidationResult.Valid($"Domain entity: {category}");
        }

        // ── UNKNOWN: need higher bar ──
        if (category == "UNKNOWN")
        {
            if (totalOccurrences >= 5)
                return EntityValidationResult.Ambiguous("High-frequency UNKNOWN entity");
            return EntityValidationResult.Invalid("Low-confidence UNKNOWN entity");
        }

        return EntityValidationResult.Ambiguous($"Unrecognized category: {category}");
    }

    /// <summary>
    /// Detects if the text matches known noise patterns.
    /// Returns the reason string if noise, or null if clean.
    /// </summary>
    public static string? DetectNoise(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "empty";

        // LaTeX artifacts
        if (LatexPattern().IsMatch(text))
            return "latex";
        // Also catch standalone LaTeX fragment words
        if (text is "frac" or "mathrm" or "mathbf" or "textrm" or "begin" or "array"
            or "bigcirc" or "times" or "cdot" or "sum" or "int")
            return "latex_fragment";

        // HTML/XML fragments
        if (HtmlFragmentPattern().IsMatch(text))
            return "html_fragment";

        // Known noise words (OCR/HTML fragments like "sup", "sub", "Metal")
        if (KnownNoiseWords.Contains(text))
            return "known_noise";

        // Year ranges
        if (YearRangePattern().IsMatch(text))
            return "year_range";

        // Pure numeric/symbol
        if (PureNumericPattern().IsMatch(text))
            return "pure_numeric";

        // Single ASCII char
        if (SingleAsciiPattern().IsMatch(text))
            return "single_ascii";

        // Standard codes (may be valid in some contexts, but usually noise when short)
        if (StandardCodePattern().IsMatch(text) && text.Length <= 6)
            return "standard_code";

        // Short ASCII-only strings in Chinese docs are usually OCR/formatting noise
        if (text.Length <= 10 && AsciiOnlyPattern().IsMatch(text) && !text.Contains(" "))
            return "ascii_only";

        return null;
    }

    /// <summary>
    /// Quick pre-filter: returns true if the text is clearly not a term candidate.
    /// </summary>
    public bool IsClearlyNotTerm(string text)
    {
        return DetectNoise(text) != null ||
               string.IsNullOrWhiteSpace(text) ||
               CommonWords.Contains(text);
    }
}
