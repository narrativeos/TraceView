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
using System.Text.RegularExpressions;
using Caly.Core.Models;
using Caly.Core.Services.EntityProcessors.Person.Models;

namespace Caly.Core.Services.EntityProcessors.Person;

/// <summary>
/// Validates and classifies PERSON entities by their name structure.
/// Filters out clearly invalid "names" (pure titles, pronouns, punctuation).
/// </summary>
public static partial class PersonValidator
{
    // ── Common Chinese surnames (covering ~99% of population) ──────
    // Source: MPS 2020 surname statistics, top 100 + common compound surnames

    private static readonly HashSet<string> CommonSurnames = new()
    {
        // Top 100 single-character surnames
        "王","李","张","刘","陈","杨","赵","黄","周","吴",
        "徐","孙","胡","朱","高","林","何","郭","马","罗",
        "梁","宋","郑","谢","韩","唐","冯","于","董","萧",
        "程","曹","袁","邓","许","傅","沈","曾","彭","吕",
        "苏","卢","蒋","蔡","贾","丁","魏","薛","叶","阎",
        "余","潘","杜","戴","夏","钟","汪","田","任","姜",
        "范","方","石","姚","谭","廖","邹","熊","金","陆",
        "郝","孔","白","崔","康","毛","邱","秦","江","史",
        "顾","侯","邵","孟","龙","万","段","雷","钱","汤",
        "尹","易","常","武","乔","贺","赖","龚","文",
        // Common compound surnames
        "欧阳","司马","上官","诸葛","东方","皇甫","令狐","慕容",
        "公孙","尉迟","长孙","宇文","独孤"
    };

    // ── Title/role words (should not be treated as names) ──────────

    private static readonly HashSet<string> TitleOnlyWords = new()
    {
        "先生","女士","小姐","同志","某人","此人","该人","这人","那人",
        "老师","教授","医生","律师","总统","主席","皇帝","将军",
        "父皇","母后","儿臣","陛下","殿下","大人","夫人","公子",
        "师傅","老板","经理","主任","书记","部长","局长","处长",
        "秀才","举人","进士","状元","榜眼","探花"
    };

    // ── Title suffix words (can be stripped to get real name) ─────

    private static readonly HashSet<string> TitleSuffixes = new()
    {
        "同志","先生","女士","小姐","老师","教授","医生","律师",
        "将军","元帅","总统","主席","总理","部长","书记","主任",
        "皇帝","皇后","太子","公主","王爷","大人","夫人"
    };

    // ── Kinship title → role mapping ──────────────────────────────

    private static readonly Dictionary<string, KinshipRole> KinshipMap = new()
    {
        ["父亲"]=KinshipRole.Parent,["爸爸"]=KinshipRole.Parent,["爹"]=KinshipRole.Parent,
        ["母亲"]=KinshipRole.Parent,["妈妈"]=KinshipRole.Parent,["娘"]=KinshipRole.Parent,
        ["儿子"]=KinshipRole.Child,["子"]=KinshipRole.Child,
        ["女儿"]=KinshipRole.Child,["女"]=KinshipRole.Child,
        ["兄"]=KinshipRole.Sibling,["哥"]=KinshipRole.Sibling,["哥哥"]=KinshipRole.Sibling,
        ["弟"]=KinshipRole.Sibling,["弟弟"]=KinshipRole.Sibling,
        ["姐"]=KinshipRole.Sibling,["姊"]=KinshipRole.Sibling,["姐姐"]=KinshipRole.Sibling,
        ["妹"]=KinshipRole.Sibling,["妹妹"]=KinshipRole.Sibling,
        ["妻"]=KinshipRole.Spouse,["夫人"]=KinshipRole.Spouse,["妻子"]=KinshipRole.Spouse,
        ["夫"]=KinshipRole.Spouse,["夫君"]=KinshipRole.Spouse,["丈夫"]=KinshipRole.Spouse,
        ["祖父"]=KinshipRole.Other,["祖母"]=KinshipRole.Other,
        ["孙子"]=KinshipRole.Other,["孙女"]=KinshipRole.Other,
        ["伯父"]=KinshipRole.Other,["叔父"]=KinshipRole.Other,["叔叔"]=KinshipRole.Other,
        ["舅舅"]=KinshipRole.Other,["姨妈"]=KinshipRole.Other,["姑姑"]=KinshipRole.Other,
    };

    // ── Regex patterns ─────────────────────────────────────────────

    [GeneratedRegex(@"^[\u4e00-\u9fff]{2,4}$", RegexOptions.Compiled)]
    private static partial Regex ChineseNamePattern();

    [GeneratedRegex(@"^[A-Z][a-z]+(?:\s[A-Z][a-z]+)+$", RegexOptions.Compiled)]
    private static partial Regex ForeignNamePattern();

    [GeneratedRegex(@"^[\u4e00-\u9fff]$", RegexOptions.Compiled)]
    private static partial Regex SingleCharPattern();

    // ── Public API ─────────────────────────────────────────────────

    /// <summary>
    /// Validates a PERSON entity and classifies its name structure.
    /// </summary>
    public static (bool IsValid, PersonNameType Type, string? CoreName, string? Reason) Validate(
        SemanticEntity entity)
    {
        var text = entity.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(text))
            return (false, PersonNameType.Unknown, null, "Empty text");

        // 1. Pure title word → Invalid
        if (TitleOnlyWords.Contains(text))
            return (false, PersonNameType.TitleOnly, null, "Pure title word");

        // 2. Contains title suffix → Valid, extract core name
        foreach (var suffix in TitleSuffixes)
        {
            if (text.EndsWith(suffix) && text.Length > suffix.Length)
            {
                var coreName = text[..^suffix.Length];
                if (coreName.Length >= 2 || IsKnownSurname(coreName))
                    return (true, PersonNameType.TitleSuffixed, coreName, $"Title suffix '{suffix}' removed");
            }
        }

        // 3. Chinese name pattern (2-4 chars, all CJK)
        if (ChineseNamePattern().IsMatch(text))
        {
            var hasKnownSurname = IsKnownSurname(text[..1]) ||
                                   (text.Length >= 2 && IsKnownSurname(text[..2]));
            if (hasKnownSurname)
                return (true, PersonNameType.FullChineseName, text, "Standard Chinese name with known surname");
            return (true, PersonNameType.FullChineseName, text, "Standard Chinese name pattern");
        }

        // 4. Foreign name pattern (alphabetic, capitalized, multi-word)
        if (ForeignNamePattern().IsMatch(text))
            return (true, PersonNameType.ForeignName, text, "Foreign name pattern");

        // 5. Single character → Ambiguous (could be surname-only reference)
        if (SingleCharPattern().IsMatch(text))
        {
            if (IsKnownSurname(text))
                return (true, PersonNameType.SingleName, text, "Single-character surname reference");
            return (false, PersonNameType.Unknown, null, "Single character, not a known surname");
        }

        // 6. Contains some CJK but doesn't match standard pattern → check more loosely
        if (Regex.IsMatch(text, @"[\u4e00-\u9fff]"))
        {
            if (text.Length >= 2 && text.Length <= 6)
                return (true, PersonNameType.FullChineseName, text, "Chinese-like name (non-standard pattern)");
        }

        // 7. Pure punctuation/numbers → Invalid
        if (text.All(c => char.IsPunctuation(c) || char.IsDigit(c) || char.IsWhiteSpace(c)))
            return (false, PersonNameType.Unknown, null, "Only punctuation/numbers");

        return (false, PersonNameType.Unknown, null, "Cannot identify as a person name");
    }

    /// <summary>
    /// Check if a given string (1-2 chars) is a known Chinese surname.
    /// </summary>
    public static bool IsKnownSurname(string text)
    {
        return CommonSurnames.Contains(text);
    }

    /// <summary>
    /// Try to classify a word as a kinship title and return the role.
    /// </summary>
    public static KinshipRole ClassifyKinship(string text)
    {
        return KinshipMap.TryGetValue(text, out var role) ? role : KinshipRole.None;
    }
}
