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

namespace Caly.Core.Services;

/// <summary>
/// Extracts domain terms from TOC titles for <c>entity_categories</c> injection
/// (entity-extraction-quality-spec §14.1 A2).
///
/// Pipeline per title: F0-style cleaning (HTML tags / LaTeX commands / OCR
/// markers) → strip leading section numbering (6.1 / 2.0.2 / 第5章 / 附录A) →
/// filter (length ≥ 2, must contain a letter) → dedupe.
///
/// Pure unit: no I/O, no UI dependencies.
/// </summary>
public static class TitleTermExtractor
{
    /// <summary>HTML tags (any) — removed entirely, inner text kept.</summary>
    private static readonly Regex HtmlTagPattern = new("<[^>]+>", RegexOptions.Compiled);

    /// <summary>LaTeX commands with braces: \mathbf { S } → S (keep the argument).</summary>
    private static readonly Regex LatexBracedPattern =
        new(@"\\[a-zA-Z]+\s*\{([^{}]*)\}", RegexOptions.Compiled);

    /// <summary>LaTeX commands without braces: \times → removed.</summary>
    private static readonly Regex LatexBarePattern =
        new(@"\\[a-zA-Z]+", RegexOptions.Compiled);

    /// <summary>OCR connection markers: &lt;|txt_split|&gt; etc.</summary>
    private static readonly Regex OcrMarkerPattern =
        new(@"<\|[^|]*\|>", RegexOptions.Compiled);

    /// <summary>
    /// Leading section numbering:
    /// 1. 第X章/节/条/款/部分/编 (Chinese numerals or digits)
    /// 2. 附录A / 附录 2 (appendix markers)
    /// 3. 6.1 / 2.0.2 / 5 (dotted or bare numbers)
    /// followed by optional separators (space, dot, 、).
    /// </summary>
    private static readonly Regex SectionNumberPattern = new(
        @"^\s*(?:(?:第\s*[0-9一二三四五六七八九十百零]+\s*[章节条款部分编])|(?:附录\s*[A-Z0-9]*)|\d+(?:\.\d+)*)[\s\.、]*",
        RegexOptions.Compiled);

    /// <summary>
    /// Extracts clean domain terms from TOC entries (deduplicated, order-preserving).
    /// Returns an empty list when the input is null or empty.
    /// </summary>
    public static List<string> Extract(IReadOnlyList<TocEntry>? tocEntries)
    {
        var terms = new List<string>();
        if (tocEntries == null || tocEntries.Count == 0)
            return terms;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in tocEntries)
        {
            var term = Clean(entry.Title);
            if (term.Length < 2)
                continue;
            if (!term.Any(char.IsLetter))
                continue;
            if (seen.Add(term))
                terms.Add(term);
        }
        return terms;
    }

    /// <summary>
    /// Cleans a single TOC title: strips HTML/LaTeX/OCR noise and leading
    /// section numbering, collapses whitespace.
    /// </summary>
    public static string Clean(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return string.Empty;

        var s = title;
        s = HtmlTagPattern.Replace(s, " ");
        s = LatexBracedPattern.Replace(s, "$1");
        s = LatexBarePattern.Replace(s, string.Empty);
        s = OcrMarkerPattern.Replace(s, string.Empty);
        s = SectionNumberPattern.Replace(s, string.Empty);
        s = Regex.Replace(s, @"\s+", " ").Trim();
        return s;
    }
}
