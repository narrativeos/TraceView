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

namespace Caly.Core.Utilities;

/// <summary>
/// Result of location name validation.
/// </summary>
public enum LocationValidationResult
{
    /// <summary>The name appears to be a valid location name.</summary>
    Valid,

    /// <summary>The name is clearly not a location (e.g., numbers, punctuation).</summary>
    Invalid,

    /// <summary>The name may be a location but is ambiguous and needs disambiguation.</summary>
    Ambiguous
}

/// <summary>
/// Semantic type of a location — distinguishes between specific named places
/// and generic facility types that serve as contextual pointers.
/// </summary>
public enum LocationType
{
    /// <summary>A specific named place (e.g., "北京", "故宫", "泰山").</summary>
    PlaceName,

    /// <summary>A generic facility/place type that lacks absolute geo coordinates
    /// but can serve as a location pointer within a region (e.g., "餐厅", "地铁站").</summary>
    GenericFacility,
}

/// <summary>
/// Provides centralized validation logic for location names extracted from NLP analysis.
/// Determines whether a string is likely a valid location name, clearly invalid,
/// or ambiguous and in need of disambiguation.
/// </summary>
public static class LocationValidator
{
    // ──────────────────────────────────────────────
    //  Pre-compiled regex patterns (performance)
    // ──────────────────────────────────────────────

    /// <summary>Matches ordinal number patterns like "第一", "第二十五", "第3".</summary>
    private static readonly Regex OrdinalPattern = new(
        @"^第[零一二三四五六七八九十百千万\d]+",
        RegexOptions.Compiled);

    /// <summary>Matches strings that contain at least one Chinese character.</summary>
    private static readonly Regex ContainsHanzi = new(
        @"[\u4e00-\u9fff\u3400-\u4dbf]",
        RegexOptions.Compiled);

    /// <summary>Matches pure punctuation/symbol characters.</summary>
    private static readonly Regex AllPunctuation = new(
        @"^[\p{P}\p{S}\s]+$",
        RegexOptions.Compiled);

    /// <summary>Matches reference number patterns like "01-2023-4239号", "NAS12".</summary>
    private static readonly Regex ReferenceNumberPattern = new(
        @"[\d]{2,}[-/][\d]{2,}|^[A-Z]+[\d]+$|^[\d]+[-/][\d]+",
        RegexOptions.Compiled);

    /// <summary>Matches mixed digit+short-hanzi patterns like "8线", "衔铁6".</summary>
    private static readonly Regex DigitWithShortHanzi = new(
        @"^[\u4e00-\u9fff]{1,2}\d+$|^\d+[\u4e00-\u9fff]{1,2}$",
        RegexOptions.Compiled);

    // ──────────────────────────────────────────────
    //  Invalid patterns (clearly not locations)
    // ──────────────────────────────────────────────

    /// <summary>
    /// Common placeholder words that are not real place names.
    /// </summary>
    private static readonly string[] PlaceholderWords =
    {
        "某地", "此处", "该处", "此地", "该地", "本地",
        "这里", "那里", "哪儿", "哪里", "那边", "这边",
        "其他地方", "别处", "他处", "各处", "到处",
    };

    /// <summary>
    /// Common technical/non-location terms that HanLP may misclassify as LOCATION.
    /// </summary>
    private static readonly HashSet<string> NonLocationTerms = new()
    {
        "程序", "系统", "模块", "单元", "部件", "组件",
        "上半球", "下半球", "二位", "三位",
        "其重要用途", "喷嘴口", "龙格-库塔", "龙格 -库塔",
        "古德里奇", "丹佛斯", "伊顿", "赫格隆", "霍尼韦尔", // 公司/品牌名
        "LVDT", "Hydrocontrol", "NAS", // 英文术语
        "太广域网", "奈奎斯特", "间内", "丁方园", "云化", "山岱岳",
        "天津罗根", "美国阿瑞斯", "上海海岳",
    };

    /// <summary>
    /// Generic facility/place type terms that lack absolute geo coordinates
    /// but can serve as location pointers within a broader region.
    /// These are NOT filtered out — they are classified as LocationType.GenericFacility.
    /// </summary>
    private static readonly HashSet<string> GenericFacilityTerms = new()
    {
        "餐厅", "西餐厅", "中餐厅",
        "地铁站", "高铁站", "火车站", "汽车站", "公交站", "始发站",
        "博物馆", "纪念馆", "展览馆", "科技馆",
        "观景台", "候车厅", "四合院",
        "打卡地", "风景区", "步行街",
    };

    /// <summary>Matches company name patterns: Chinese + space + ASCII uppercase (e.g., "霍尼韦尔 EHV").</summary>
    private static readonly Regex CompanyNamePattern = new(
        @"[\u4e00-\u9fff]+\s+[A-Z]{2,}",
        RegexOptions.Compiled);

    /// <summary>Matches OCR artifacts with special separators like underscores.</summary>
    private static readonly Regex OcrArtifactPattern = new(
        @"[_=]",
        RegexOptions.Compiled);

    /// <summary>
    /// Common ordinal measure words that follow numbers.
    /// </summary>
    private static readonly string[] OrdinalMeasureWords =
    {
        "号", "条", "款", "项", "章", "节", "页", "段", "行",
        "个", "只", "件", "种", "类", "份", "篇", "本", "册",
    };

    /// <summary>
    /// Single-character cardinal directions that are too ambiguous alone.
    /// </summary>
    private static readonly string[] CardinalDirections =
    {
        "东", "南", "西", "北", "中",
        "上", "下", "左", "右", "前", "后",
        "内", "外", "里",
    };

    /// <summary>
    /// Single-character geographic generic terms — too ambiguous when appearing alone.
    /// </summary>
    private static readonly string[] SingleCharGenerics =
    {
        "河", "山", "海", "江", "湖", "岛", "湾", "港",
        "城", "镇", "村", "庄", "市", "县", "区", "省",
        "路", "街", "桥", "门", "关", "岭", "峰", "谷",
        "原", "漠", "林", "泉", "溪", "潭", "池",
    };

    // ──────────────────────────────────────────────
    //  Ambiguous abbreviation patterns
    // ──────────────────────────────────────────────

    /// <summary>
    /// Province abbreviation + direction patterns that need parent context.
    /// e.g., "京西" (west of Beijing), "苏南" (southern Jiangsu)
    /// </summary>
    private static readonly string[] AmbiguousPatterns =
    {
        // 北京方位
        "京西", "京南", "京北", "京东",
        // 上海方位
        "沪上", "沪西", "沪南", "沪北",
        // 重庆方位
        "渝中", "渝北", "渝南",
        // 安徽方位
        "皖中", "皖南", "皖北", "皖西",
        // 江苏方位
        "苏南", "苏北", "苏中",
        // 浙江方位
        "浙西", "浙东",
        // 四川方位
        "川西", "川东", "川南", "川北",
        // 云南方位
        "滇西", "滇南",
        // 陕西方位
        "关中", "陕北", "陕南",
        // 湖北方位
        "鄂西", "鄂东",
        // 湖南方位
        "湘西", "湘南", "湘北",
        // 江西方位
        "赣南", "赣北",
        // 福建方位
        "闽西", "闽东",
        // 广东方位
        "岭南", "岭北",
        // 北方方位
        "塞外", "塞北",
        // 泛区域
        "江南", "江北", "江东", "江西",
        "塞上", "关外",
    };

    // ──────────────────────────────────────────────
    //  Public API
    // ──────────────────────────────────────────────

    /// <summary>
    /// Validates a location name and returns its validation result with a reason.
    /// </summary>
    /// <param name="name">The location name to validate.</param>
    /// <returns>A tuple of (result, reason).</returns>
    public static (LocationValidationResult Result, string Reason) Validate(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return (LocationValidationResult.Invalid, "空字符串");

        var trimmed = name.Trim();

        if (trimmed.Length == 0)
            return (LocationValidationResult.Invalid, "空字符串");

        // Check clearly invalid patterns first
        if (IsLikelyNotLocation(trimmed, out var invalidReason))
            return (LocationValidationResult.Invalid, invalidReason);

        // Then check ambiguous patterns
        if (IsAmbiguousAbbreviation(trimmed, out var ambiguousReason))
            return (LocationValidationResult.Ambiguous, ambiguousReason);

        return (LocationValidationResult.Valid, "有效地名");
    }

    /// <summary>
    /// Determines whether a location name is clearly NOT a valid location.
    /// </summary>
    public static bool IsLikelyNotLocation(string name) =>
        IsLikelyNotLocation(name, out _);

    /// <summary>
    /// Determines whether a location name is ambiguous and needs disambiguation.
    /// </summary>
    public static bool IsAmbiguousAbbreviation(string name) =>
        IsAmbiguousAbbreviation(name, out _);

    /// <summary>
    /// Classifies a valid location name into its semantic type.
    /// Call this after Validate() returns Valid to distinguish
    /// between specific PlaceNames and GenericFacility pointers.
    /// </summary>
    public static LocationType ClassifyType(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return LocationType.PlaceName;

        var trimmed = name.Trim();

        if (GenericFacilityTerms.Contains(trimmed))
            return LocationType.GenericFacility;

        return LocationType.PlaceName;
    }

    /// <summary>
    /// Chinese display name for a location type.
    /// </summary>
    public static string ToDisplay(LocationType type) => type switch
    {
        LocationType.PlaceName => "地名",
        LocationType.GenericFacility => "泛称设施",
        _ => "未知"
    };

    // ──────────────────────────────────────────────
    //  Private implementation
    // ──────────────────────────────────────────────

    /// <summary>
    /// Checks if the name is clearly not a location, with a reason.
    /// </summary>
    private static bool IsLikelyNotLocation(string name, out string reason)
    {
        reason = string.Empty;

        // Empty or whitespace
        if (string.IsNullOrWhiteSpace(name))
        {
            reason = "空字符串";
            return true;
        }

        // Pure punctuation / symbols
        if (AllPunctuation.IsMatch(name))
        {
            reason = "纯标点符号";
            return true;
        }

        // Ends with "号" — likely a reference number (check before digit patterns)
        if (name.EndsWith("号"))
        {
            if (name.Length <= 6 && (name.Any(c => char.IsDigit(c)) || name.StartsWith("第")))
            {
                reason = "编号类名称（以号结尾）";
                return true;
            }
            if (name.Any(c => char.IsDigit(c)) && name.Any(c => c == '-'))
            {
                reason = "编号类名称（含数字和连字符）";
                return true;
            }
        }

        // Mixed digit + 1-2 Chinese chars: "8线", "衔铁6"
        if (DigitWithShortHanzi.IsMatch(name) && name.Length <= 6)
        {
            reason = "数字+短中文（非标准地名）";
            return true;
        }

        // Pure digits
        if (name.All(c => char.IsDigit(c)))
        {
            reason = "纯数字";
            return true;
        }

        // Single character that is not a Chinese character
        if (name.Length == 1 && !ContainsHanzi.IsMatch(name))
        {
            reason = "单字符非汉字";
            return true;
        }

        // OCR contamination checks (before ASCII-only check — catch "foo_bar" etc.)
        if (name.Contains('\n') || name.Contains('\r'))
        {
            reason = "OCR换行污染";
            return true;
        }

        if (OcrArtifactPattern.IsMatch(name))
        {
            reason = "OCR混合文本（含特殊分隔符）";
            return true;
        }

        // Pure ASCII letters + digits, no Chinese characters — not a Chinese location
        if (name.All(c => char.IsAscii(c)) && !ContainsHanzi.IsMatch(name))
        {
            reason = "不含汉字（非中文地名）";
            return true;
        }

        // Reference number patterns: "01-2023-4239号", "NAS12", "51-54"
        if (ReferenceNumberPattern.IsMatch(name))
        {
            reason = "编号/代码模式";
            return true;
        }

        // Ordinal number patterns: "第一", "第二十五", "第3条" etc.
        if (OrdinalPattern.IsMatch(name))
        {
            reason = "序数词模式";
            return true;
        }

        // Contains digits AND special chars like ')' — likely a reference number, not a place
        if (name.Length > 3 && name.Any(c => char.IsDigit(c)) && name.Any(c => c == ')'))
        {
            reason = "含数字和括号（可能是编号）";
            return true;
        }

        // Ends with "号" — likely a reference number
        // Short names: "第25号", "25号"
        // Longer names with number patterns: "01-2023-4239号"
        if (name.EndsWith("号"))
        {
            if (name.Length <= 6 && (name.Any(c => char.IsDigit(c)) || name.StartsWith("第")))
            {
                reason = "编号类名称（以号结尾的短字符串）";
                return true;
            }
            if (name.Any(c => char.IsDigit(c)) && name.Any(c => c == '-'))
            {
                reason = "编号类名称（含数字和连字符）";
                return true;
            }
        }

        // Placeholder words
        if (PlaceholderWords.Contains(name))
        {
            reason = "占位词（非真实地名）";
            return true;
        }

        // Known non-location technical terms
        if (NonLocationTerms.Contains(name))
        {
            reason = "技术术语（非地名）";
            return true;
        }

        // Company/product name patterns: "霍尼韦尔 EHV", "天津罗根"
        if (CompanyNamePattern.IsMatch(name))
        {
            reason = "公司/产品名模式";
            return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if the name is an ambiguous abbreviation that needs disambiguation, with a reason.
    /// </summary>
    private static bool IsAmbiguousAbbreviation(string name, out string reason)
    {
        reason = string.Empty;

        if (string.IsNullOrWhiteSpace(name))
            return false;

        // Single Chinese character — too ambiguous
        if (name.Length == 1 && ContainsHanzi.IsMatch(name))
        {
            // Check if it's a cardinal direction or geographic generic
            if (CardinalDirections.Contains(name))
            {
                reason = "单字方位词";
                return true;
            }
            if (SingleCharGenerics.Contains(name))
            {
                reason = "单字通名";
                return true;
            }
            // Other single Chinese chars — still ambiguous as standalone locations
            reason = "单字地名（歧义）";
            return true;
        }

        // Known ambiguous province+direction patterns
        if (AmbiguousPatterns.Contains(name))
        {
            reason = "方位简称（需要上下文确定具体地点）";
            return true;
        }

        return false;
    }

    /// <summary>
    /// Returns a human-readable Chinese description for a validation result.
    /// </summary>
    public static string ToDisplay(LocationValidationResult result) => result switch
    {
        LocationValidationResult.Valid => "有效",
        LocationValidationResult.Invalid => "无效",
        LocationValidationResult.Ambiguous => "歧义",
        _ => "未知"
    };
}
