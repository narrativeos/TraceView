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

using System.Text.RegularExpressions;
using Caly.Core.Models;
using Caly.Core.Services.EntityProcessors.Temporal.Models;

namespace Caly.Core.Services.EntityProcessors.Temporal;

/// <summary>
/// Validates and classifies DATE entities into temporal types.
/// Determines whether a date expression is Absolute, Era-based, Relative, Range, or Vague.
/// </summary>
public static partial class TemporalValidator
{
    // ── Era name patterns ──────────────────────────────────────────

    /// <summary>Recognizes Chinese era names followed by a year number.</summary>
    /// <example>"乾隆十年", "康熙五十二年", "光绪元年"</example>
    [GeneratedRegex(
        @"^(" +
        @"康熙|雍正|乾隆|嘉庆|道光|咸丰|同治|光绪|宣统|" +        // 清
        @"洪武|建文|永乐|洪熙|宣德|正统|景泰|天顺|成化|弘治|正德|嘉靖|隆庆|万历|泰昌|天启|崇祯|" + // 明
        @"贞观|开元|天宝|" +                                    // 唐（代表性）
        @"建武|建武中元|永平|建初|元和|章和|永元|元兴|建安|延康|" + // 东汉
        @"黄初|太和|青龙|景初" +                                 // 三国魏
        @")" +
        @"(元|\d{1,2})年",
        RegexOptions.Compiled)]
    private static partial Regex EraNamePattern();

    // ── Relative time patterns ─────────────────────────────────────

    /// <summary>Recognizes relative time expressions.</summary>
    /// <example>"去年", "三天前", "次年", "两个月后"</example>
    [GeneratedRegex(
        @"(去年|今年|明年|前年|后年|大前年|大后年|" +
        @"今日|昨日|明日|今天|昨天|明天|" +
        @"次日|翌日|当日|是日|" +
        @"\d{1,2}[天日月年][之]?[前后内]|" +
        @"\d{1,2}个?[月年][之]?[前后内]|" +
        @"上[个]?月|下[个]?月|本月|" +
        @"上周|下周|本周|" +
        @"年初|年底|年末|月初|月末|月底|" +
        @"上旬|中旬|下旬|" +
        @"当时|那时|此时|如今|现今|现在|" +
        @"近来|近年|近日|最近|近期|" +
        @"不久[之]?[前后]|稍[前后]|随即|随后)",
        RegexOptions.Compiled)]
    private static partial Regex RelativeTimePattern();

    // ── Range patterns ──────────────────────────────────────────────

    /// <summary>Recognizes time range expressions.</summary>
    /// <example>"2020年至2023年", "上世纪80年代", "1990年代"</example>
    [GeneratedRegex(
        @"(\d{3,4}年[至到\-–—]\d{3,4}年|" +
        @"\d{3,4}[至到\-–—]\d{3,4}年|" +
        @"上?世纪\d{2,4}年代|" +
        @"\d{4}年代|" +
        @"\d{4}年\d{1,2}月[至到\-–—]\d{1,2}月|" +
        @"公元前?\d+[至到\-–—]公元前?\d+)",
        RegexOptions.Compiled)]
    private static partial Regex RangePattern();

    // ── Vague patterns ──────────────────────────────────────────────

    [GeneratedRegex(
        @"(很久[以之]?[前后]|许久[以之]?[前后]|早年|晚年|从前|以往|古时|古代|自古以来|历来)",
        RegexOptions.Compiled)]
    private static partial Regex VaguePattern();

    // ── BC patterns ─────────────────────────────────────────────────

    [GeneratedRegex(@"公元前?\d+年?", RegexOptions.Compiled)]
    private static partial Regex BCPattern();

    // ── Chinese numeral patterns ────────────────────────────────────

    [GeneratedRegex(@"[\u4e00-\u9fff一二三四五六七八九十百千万亿]+年", RegexOptions.Compiled)]
    private static partial Regex ChineseYearPattern();

    // ── Public API ──────────────────────────────────────────────────

    /// <summary>
    /// Validates and classifies a DATE entity.
    /// </summary>
    /// <returns>A tuple of (isValid, temporalType, reason).</returns>
    public static (bool IsValid, TemporalType Type, string? Reason) Validate(
        SemanticEntity entity)
    {
        var text = entity.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(text))
            return (false, TemporalType.Vague, "Empty text");

        // 1. HanLP already normalized → Absolute
        if (!string.IsNullOrEmpty(entity.Normalized))
            return (true, TemporalType.Absolute, $"HanLP normalized: {entity.Normalized}");

        // 2. BC dates → Absolute
        if (BCPattern().IsMatch(text))
            return (true, TemporalType.Absolute, "BC date");

        // 3. Era name pattern → Era
        if (EraNamePattern().IsMatch(text))
            return (true, TemporalType.Era, "Era name detected");

        // 4. Range pattern → Range
        if (RangePattern().IsMatch(text))
            return (true, TemporalType.Range, "Time range detected");

        // 5. Relative time → Ambiguous (needs anchor)
        if (RelativeTimePattern().IsMatch(text))
            return (true, TemporalType.Relative, "Relative time expression (needs anchor)");

        // 6. Vague → Valid but low confidence
        if (VaguePattern().IsMatch(text))
            return (true, TemporalType.Vague, "Vague temporal expression");

        // 7. Contains Chinese year-like pattern → Vague
        if (ChineseYearPattern().IsMatch(text))
            return (true, TemporalType.Vague, "Chinese year-like pattern");

        // 8. Contains digits that look like a year → Absolute or Range
        if (Regex.IsMatch(text, @"\d{4}"))
            return (true, TemporalType.Absolute, "Contains 4-digit year");

        if (Regex.IsMatch(text, @"\d{1,2}月|\d{1,2}日"))
            return (true, TemporalType.Relative, "Partial date (month/day only)");

        // 9. Pure punctuation or single char → Invalid
        if (text.Length <= 1 && !char.IsDigit(text[0]))
            return (false, TemporalType.Vague, "Too short");

        // 10. No recognizable temporal pattern at all → Invalid
        if (!Regex.IsMatch(text, @"[\d年日月世纪代]"))
            return (false, TemporalType.Vague, "No temporal indicators");

        return (true, TemporalType.Vague, "Unclassified temporal expression");
    }
}
