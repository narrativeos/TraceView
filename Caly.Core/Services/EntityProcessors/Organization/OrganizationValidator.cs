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

using System.Collections.Generic;
using System.Linq;
using Caly.Core.Models;
using Caly.Core.Services.EntityProcessors.Organization.Models;

namespace Caly.Core.Services.EntityProcessors.Organization;

/// <summary>
/// Validates and classifies ORGANIZATION entities by their name structure.
/// Detects formal names (with recognized suffixes), abbreviations, and invalid entries.
/// </summary>
public static class OrganizationValidator
{
    // ── Organization suffixes (formal name indicators) ─────────────

    private static readonly HashSet<string> OrgSuffixes = new()
    {
        // Companies
        "公司","有限公司","股份有限公司","集团","企业","工厂","商店","商行","实业",
        // Government
        "政府","部","厅","局","处","科","委","办","署","院",
        // Education
        "大学","学院","学校","中学","小学","研究所","研究院",
        // Healthcare
        "医院","诊所","卫生院",
        // Finance
        "银行","信用社","保险","证券",
        // Associations
        "协会","学会","委员会","组织","机构","中心","基金会",
        // Military
        "部队","军区","司令部","兵团","军","师","旅","团",
        // Media
        "报社","电视台","广播电台","出版社",
        // Historical
        "书院","衙门","商号","票号","洋行"
    };

    // ── Geographic prefixes (common in org names) ──────────────────

    private static readonly HashSet<string> GeoPrefixes = new()
    {
        "中国","中华","全国","中央","国家",
        "北京","上海","天津","重庆",
        "江苏","浙江","广东","山东","河南","四川","湖北","湖南","福建",
        "河北","辽宁","陕西","安徽","江西","山西","吉林","黑龙江",
        "云南","贵州","甘肃","海南","青海","台湾","内蒙古","西藏",
        "广西","宁夏","新疆","香港","澳门"
    };

    // ── Public API ─────────────────────────────────────────────────

    /// <summary>
    /// Validate an ORGANIZATION entity and classify its name type.
    /// </summary>
    public static (bool IsValid, OrganizationNameType Type, string? Reason) Validate(
        SemanticEntity entity)
    {
        var text = entity.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(text))
            return (false, OrganizationNameType.Invalid, "Empty text");

        // 1. Pure punctuation/numbers → Invalid
        if (text.All(c => char.IsPunctuation(c) || char.IsDigit(c) || char.IsWhiteSpace(c)))
            return (false, OrganizationNameType.Invalid, "Only punctuation/numbers");

        // 2. Single character → Invalid (unless it's a known gov body like "部")
        if (text.Length <= 1)
        {
            if (OrgSuffixes.Contains(text))
                return (true, OrganizationNameType.Abbreviation, "Single-char known org suffix");
            return (false, OrganizationNameType.Invalid, "Single character");
        }

        // 3. Contains a recognized org suffix → FullFormalName
        foreach (var suffix in OrgSuffixes)
        {
            if (text.EndsWith(suffix) && text.Length > suffix.Length)
                return (true, OrganizationNameType.FullFormalName, $"Recognized suffix: {suffix}");
        }

        // 4. Starts with geographic prefix → Valid (likely a regional org)
        foreach (var prefix in GeoPrefixes)
        {
            if (text.StartsWith(prefix) && text.Length > prefix.Length + 1)
                return (true, OrganizationNameType.GeographicPrefix, $"Geographic prefix: {prefix}");
        }

        // 5. Short name (2-5 chars) without suffix → Likely abbreviation
        if (text.Length >= 2 && text.Length <= 5 && HasChineseChars(text))
            return (true, OrganizationNameType.Abbreviation, "Likely abbreviation (2-5 chars, no suffix)");

        // 6. Contains org-like Chinese characters → Generic
        if (HasChineseChars(text) && text.Length >= 2)
            return (true, OrganizationNameType.Generic, "Generic Chinese name (no recognized suffix)");

        return (false, OrganizationNameType.Invalid, "Cannot identify as organization name");
    }

    /// <summary>
    /// Check if text contains Chinese characters.
    /// </summary>
    public static bool HasChineseChars(string text)
    {
        return text.Any(c => c >= 0x4e00 && c <= 0x9fff);
    }
}
