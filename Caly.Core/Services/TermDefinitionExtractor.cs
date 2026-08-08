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
/// Extracts term definitions from text blocks using pattern matching and dependency parsing.
/// Supports both modern Chinese ("X是一种Y") and classical Chinese ("X者，Y也") definition patterns.
/// </summary>
public partial class TermDefinitionExtractor
{
    // ── Modern Chinese Definition Patterns ──

    /// <summary>"X是Y" / "X是一种Y" / "X是一个Y"</summary>
    [GeneratedRegex(@"(.{2,20}?)(?:是[一|某|种|个|类|门|项])?(?:是)(.{2,30}?)(?:[，。；、\s]|$)", RegexOptions.Compiled)]
    private static partial Regex IsPattern();

    /// <summary>"X即Y"</summary>
    [GeneratedRegex(@"(.{2,20}?)(?:即|也就是|就是说)(.{2,30}?)(?:[，。；、\s]|$)", RegexOptions.Compiled)]
    private static partial Regex JiPattern();

    /// <summary>"X指Y" / "X是指Y" / "X指的是Y"</summary>
    [GeneratedRegex(@"(.{2,20}?)(?:是)?指(?:的是)?(.{2,30}?)(?:[，。；、\s]|$)", RegexOptions.Compiled)]
    private static partial Regex ZhiPattern();

    /// <summary>"X定义为Y" / "X被定义为Y"</summary>
    [GeneratedRegex(@"(.{2,20}?)(?:被)?定义(?:为|成)(.{2,30}?)(?:[，。；、\s]|$)", RegexOptions.Compiled)]
    private static partial Regex DingYiPattern();

    /// <summary>"所谓X，(就是/即/是指)Y"</summary>
    [GeneratedRegex(@"所谓[“「]?(.{2,20}?)[」”]?[，,\s]*(?:就是|即|是指|指的是)(.{2,30}?)(?:[，。；、\s]|$)", RegexOptions.Compiled)]
    private static partial Regex SuoWeiPattern();

    // ── Classical Chinese Definition Patterns ──

    /// <summary>"X者，Y也" / "X者Y也"</summary>
    [GeneratedRegex(@"(.{2,20}?)者[，,\s]*(.{2,30}?)也", RegexOptions.Compiled)]
    private static partial Regex ZheYePattern();

    /// <summary>"X谓之Y" / "X之谓Y"</summary>
    [GeneratedRegex(@"(.{2,20}?)(?:之)?谓(?:之)?(.{2,30}?)(?:[，。；、\s]|$)", RegexOptions.Compiled)]
    private static partial Regex WeiZhiPattern();

    /// <summary>"X曰Y" (classical "X is called Y")</summary>
    [GeneratedRegex(@"(.{2,20}?)(?:曰|云)(.{2,30}?)(?:[，。；、\s]|$)", RegexOptions.Compiled)]
    private static partial Regex YuePattern();

    /// <summary>"X为Y" (copula in classical, but ambiguous — need POS verification)</summary>
    [GeneratedRegex(@"(?:^|[，。；、\s])(.{2,20}?)为(.{2,30}?)(?:[，。；、\s]|$)", RegexOptions.Compiled)]
    private static partial Regex WeiPattern();

    // ── P2: Technical/Engineering Document Patterns ──

    /// <summary>"X分为Y" / "X可分为Y" / "X按Y分为Z" (classification)</summary>
    [GeneratedRegex(@"(.{2,20}?)(?:可)?分(?:为|成)(.{2,30}?)(?:[，。；、\s]|$)", RegexOptions.Compiled)]
    private static partial Regex FenWeiPattern();

    /// <summary>"X包括Y" / "X包含Y" / "X含有Y" (composition)</summary>
    [GeneratedRegex(@"(.{2,20}?)(?:包[括含]|含[有]?)(.{2,30}?)(?:[，。；、\s]|$)", RegexOptions.Compiled)]
    private static partial Regex BaoKuoPattern();

    /// <summary>"X称为Y" / "X称作Y" / "X叫做Y" / "X名为Y" (naming)</summary>
    [GeneratedRegex(@"(.{2,20}?)(?:称|叫|名)(?:为|作|做)(.{2,20}?)(?:[，。；、\s]|$)", RegexOptions.Compiled)]
    private static partial Regex ChengWeiPattern();

    /// <summary>"X属于Y" / "X归属Y" (classification membership)</summary>
    [GeneratedRegex(@"(.{2,20}?)(?:属|归属)(?:于)(.{2,20}?)(?:[，。；、\s]|$)", RegexOptions.Compiled)]
    private static partial Regex ShuYuPattern();

    /// <summary>"X用于Y" / "X用来Y" (purpose — weaker relation)</summary>
    [GeneratedRegex(@"(.{2,20}?)(?:用[于来])(.{2,20}?)(?:[，。；、\s]|$)", RegexOptions.Compiled)]
    private static partial Regex YongYuPattern();

    // ── Synonym / Alternate Name Patterns ──

    /// <summary>"X亦称Y" / "X又称Y" / "X也叫Y" / "X又名Y"</summary>
    [GeneratedRegex(@"(.{2,20}?)(?:亦|又|也)(?:称|叫|名|号)(?:为|作|做)?(.{2,20}?)(?:[，。；、\s]|$)", RegexOptions.Compiled)]
    private static partial Regex AlsoCalledPattern();

    /// <summary>"X俗称Y" / "X通称Y"</summary>
    [GeneratedRegex(@"(.{2,20}?)(?:俗|通|简|统)(?:称|名)(?:为|作)?(.{2,20}?)(?:[，。；、\s]|$)", RegexOptions.Compiled)]
    private static partial Regex CommonNamePattern();

    // ── Noise filters ──

    private static readonly HashSet<string> GenericHypernyms = new(StringComparer.Ordinal)
    {
        "东西", "事物", "概念", "现象", "问题", "情况", "方法", "方式",
        "过程", "结果", "原因", "条件", "类型", "种类", "部分", "方面",
        "人", "地方", "时候", "时间", "事情"
    };

    /// <summary>
    /// Extracts term definitions from a single semantic block.
    /// Uses pattern matching on raw content, cross-referenced with entities and dependency parse.
    /// </summary>
    public List<TermDefinition> ExtractDefinitions(SemanticBlockResult block)
    {
        var definitions = new List<TermDefinition>();
        if (string.IsNullOrWhiteSpace(block.Content))
            return definitions;

        var content = block.Content;

        // ── Pass 1: Modern Chinese patterns ──
        ExtractFromPattern(definitions, content, IsPattern(), "IS_A",
            (m) => (m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim()));
        ExtractFromPattern(definitions, content, JiPattern(), "IS_A",
            (m) => (m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim()));
        ExtractFromPattern(definitions, content, ZhiPattern(), "IS_A",
            (m) => (m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim()));
        ExtractFromPattern(definitions, content, DingYiPattern(), "IS_A",
            (m) => (m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim()));
        ExtractFromPattern(definitions, content, SuoWeiPattern(), "IS_A",
            (m) => (m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim()));

        // ── Pass 2: Classical Chinese patterns ──
        ExtractFromPattern(definitions, content, ZheYePattern(), "IS_A",
            (m) => (m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim()));
        ExtractFromPattern(definitions, content, WeiZhiPattern(), "IS_A",
            (m) => (m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim()));
        ExtractFromPattern(definitions, content, YuePattern(), "IS_A",
            (m) => (m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim()));
        // "X为Y" only when Y is clearly a noun (not a verb complement)
        ExtractFromPattern(definitions, content, WeiPattern(), "IS_A",
            (m) => (m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim()),
            requireEntityMatch: true); // More conservative — require entity match

        // ── Pass 2.5: Technical/engineering document patterns (P2) ──
        ExtractFromPattern(definitions, content, FenWeiPattern(), "IS_A",
            (m) => (m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim()));
        ExtractFromPattern(definitions, content, BaoKuoPattern(), "COMPOSED_OF",
            (m) => (m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim()));
        ExtractFromPattern(definitions, content, ChengWeiPattern(), "IS_A",
            (m) => (m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim()));
        ExtractFromPattern(definitions, content, ShuYuPattern(), "IS_A",
            (m) => (m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim()));
        ExtractFromPattern(definitions, content, YongYuPattern(), "RELATED_TERM",
            (m) => (m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim()));

        // ── Pass 3: Synonym / alternate name patterns ──
        ExtractFromPattern(definitions, content, AlsoCalledPattern(), "SYNONYM",
            (m) => (m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim()));
        ExtractFromPattern(definitions, content, CommonNamePattern(), "SYNONYM",
            (m) => (m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim()));

        // ── Pass 4: Dependency-based definition extraction ──
        ExtractFromDependencyParse(definitions, block);

        // ── Post-processing: filter & score ──
        return PostProcessDefinitions(definitions, block);
    }

    private void ExtractFromPattern(
        List<TermDefinition> definitions,
        string content,
        Regex pattern,
        string relationType,
        Func<Match, (string term, string hypernym)> extractor,
        bool requireEntityMatch = false)
    {
        foreach (Match match in pattern.Matches(content))
        {
            var (term, hypernym) = extractor(match);

            // Basic noise filtering
            if (IsNoise(term) || IsNoise(hypernym))
                continue;
            if (GenericHypernyms.Contains(hypernym))
                continue;
            if (term == hypernym)
                continue;

            var evidence = match.Value.Trim();

            definitions.Add(new TermDefinition
            {
                TermText = term,
                DefinitionText = hypernym,
                RelationType = relationType,
                Confidence = CalculatePatternConfidence(pattern, match, requireEntityMatch),
                Evidence = evidence,
                EvidenceSpan = new[] { match.Index, match.Index + match.Length },
                Source = "pattern"
            });
        }
    }

    /// <summary>
    /// Extracts definitions from dependency parse by finding copula constructions.
    /// Look for: nsubj(是, X) + attr(是, Y) → "X是Y"
    /// Also: top(是, ...) patterns.
    /// </summary>
    private void ExtractFromDependencyParse(List<TermDefinition> definitions, SemanticBlockResult block)
    {
        if (block.DepTokens.Count == 0 || block.DepEdges.Count == 0)
            return;

        var tokenById = block.DepTokens.ToDictionary(t => t.Id, t => t);
        var edgesByChild = block.DepEdges.ToLookup(e => e.Child);
        var edgesByHead = block.DepEdges.ToLookup(e => e.Head);

        // Find copula tokens (是/为) with both nsubj and attr dependents
        foreach (var token in block.DepTokens)
        {
            if (token.Pos != "v" && token.Pos != "VC" && token.Pos != "VE")
                continue;
            if (token.Text != "是" && token.Text != "为" && token.Text != "即" && token.Text != "系")
                continue;

            var childEdges = edgesByChild[token.Id].ToList();
            var nsubjEdge = childEdges.FirstOrDefault(e => e.Rel == "nsubj" || e.Rel == "top");
            var attrEdge = childEdges.FirstOrDefault(e => e.Rel == "attr" || e.Rel == "dobj");

            if (nsubjEdge == null || attrEdge == null)
                continue;

            if (!tokenById.TryGetValue(nsubjEdge.Head, out var subjToken) ||
                !tokenById.TryGetValue(attrEdge.Head, out var attrToken))
                continue;

            var subjPhrase = ExpandPhrase(nsubjEdge.Head, block.DepTokens, block.DepEdges);
            var attrPhrase = ExpandPhrase(attrEdge.Head, block.DepTokens, block.DepEdges);

            if (IsNoise(subjPhrase) || IsNoise(attrPhrase))
                continue;
            if (GenericHypernyms.Contains(attrPhrase))
                continue;

            definitions.Add(new TermDefinition
            {
                TermText = subjPhrase,
                DefinitionText = attrPhrase,
                RelationType = "IS_A",
                Confidence = 0.85,
                Evidence = $"{subjPhrase} {token.Text} {attrPhrase}",
                Source = "dependency"
            });
        }
    }

    /// <summary>
    /// Expands a token to its full phrase by including modifiers (nn, amod, nummod, etc.).
    /// </summary>
    private static string ExpandPhrase(int headId, List<SemanticDepToken> tokens, List<SemanticDepEdge> edges)
    {
        var tokenById = tokens.ToDictionary(t => t.Id, t => t);
        if (!tokenById.TryGetValue(headId, out var headToken))
            return "";

        var modifierEdges = edges.Where(e =>
            e.Head == headId && (e.Rel == "nn" || e.Rel == "amod" || e.Rel == "nummod" || e.Rel == "det"));

        var parts = new List<(int id, string text)>();
        foreach (var edge in modifierEdges)
        {
            if (tokenById.TryGetValue(edge.Child, out var modToken) && !string.IsNullOrWhiteSpace(modToken.Text))
            {
                parts.Add((edge.Child, modToken.Text));
            }
        }
        // Sort modifiers by ID (left-to-right)
        parts.Sort((a, b) => a.id.CompareTo(b.id));

        // Insert head token in the right position
        var allParts = new List<string>();
        foreach (var part in parts)
        {
            if (part.id < headId)
                allParts.Add(part.text);
        }
        allParts.Add(headToken.Text);
        foreach (var part in parts)
        {
            if (part.id > headId)
                allParts.Add(part.text);
        }

        return string.Concat(allParts);
    }

    private static double CalculatePatternConfidence(Regex pattern, Match match, bool requireEntityMatch)
    {
        // Base confidence depends on pattern specificity
        var baseConf = pattern switch
        {
            _ when pattern == ZheYePattern() => 0.90,       // "X者Y也" — very strong definition marker
            _ when pattern == DingYiPattern() => 0.90,       // "X定义为Y" — explicit definition
            _ when pattern == SuoWeiPattern() => 0.85,       // "所谓X，就是Y"
            _ when pattern == FenWeiPattern() => 0.82,       // "X分为Y" — classification (tech)
            _ when pattern == JiPattern() => 0.80,            // "X即Y"
            _ when pattern == ZhiPattern() => 0.80,           // "X指Y"
            _ when pattern == ShuYuPattern() => 0.78,         // "X属于Y" — membership (tech)
            _ when pattern == ChengWeiPattern() => 0.78,      // "X称为Y" — naming (tech)
            _ when pattern == IsPattern() => 0.75,            // "X是Y" — common but ambiguous
            _ when pattern == BaoKuoPattern() => 0.72,        // "X包括Y" — composition (tech)
            _ when pattern == YongYuPattern() => 0.60,        // "X用于Y" — purpose (weaker)
            _ => 0.70
        };

        // Longer terms are more specific
        var termLen = match.Groups[1].Value.Trim().Length;
        if (termLen >= 3) baseConf += 0.05;
        if (termLen >= 5) baseConf += 0.03;

        return Math.Min(baseConf, 1.0);
    }

    /// <summary>
    /// Post-processes extracted definitions: normalizes, deduplicates, and scores.
    /// </summary>
    private List<TermDefinition> PostProcessDefinitions(
        List<TermDefinition> raw,
        SemanticBlockResult block)
    {
        if (raw.Count == 0) return raw;

        // Normalize text
        foreach (var def in raw)
        {
            def.TermText = NormalizeTerm(def.TermText);
            def.DefinitionText = NormalizeTerm(def.DefinitionText);
        }

        // Deduplicate by (term, definition) pair
        var seen = new HashSet<string>();
        var result = new List<TermDefinition>();
        foreach (var def in raw)
        {
            var key = $"{def.TermText}|{def.DefinitionText}";
            if (seen.Contains(key))
                continue;
            seen.Add(key);
            result.Add(def);
        }

        // Boost confidence for terms that match known entities
        var entityTexts = new HashSet<string>(block.Entities.Select(e => e.Text), StringComparer.Ordinal);
        foreach (var def in result)
        {
            if (entityTexts.Contains(def.TermText))
                def.Confidence = Math.Min(def.Confidence + 0.10, 1.0);
            if (entityTexts.Contains(def.DefinitionText))
                def.Confidence = Math.Min(def.Confidence + 0.05, 1.0);
        }

        return result;
    }

    /// <summary>
    /// Resolves cross-block definitions: merges repeated definitions and normalizes term variants.
    /// </summary>
    public void ResolveCrossBlockDefinitions(List<SemanticBlockResult> blocks)
    {
        // Collect all definitions
        var allDefs = new List<(TermDefinition def, int blockIndex)>();
        for (int i = 0; i < blocks.Count; i++)
        {
            foreach (var def in blocks[i].TermDefinitions)
            {
                allDefs.Add((def, i));
            }
        }

        // Group by normalized term
        var byTerm = new Dictionary<string, List<(TermDefinition def, int blockIdx)>>(StringComparer.Ordinal);
        foreach (var item in allDefs)
        {
            var key = item.def.TermText;
            if (!byTerm.ContainsKey(key))
                byTerm[key] = new List<(TermDefinition, int)>();
            byTerm[key].Add(item);
        }

        // For terms with multiple definitions, pick the best one and mark cross-block
        foreach (var kvp in byTerm)
        {
            var defs = kvp.Value;
            if (defs.Count <= 1) continue;

            // Pick the highest-confidence definition
            var best = defs.OrderByDescending(d => d.def.Confidence).First();
            best.def.CrossBlockCount = defs.Count;

            // Update the original block's definition confidence
            for (int i = 0; i < blocks.Count; i++)
            {
                var blockDefs = blocks[i].TermDefinitions;
                for (int j = 0; j < blockDefs.Count; j++)
                {
                    if (blockDefs[j].TermText == kvp.Key &&
                        blockDefs[j].DefinitionText != best.def.DefinitionText)
                    {
                        // Mark lower-confidence variants as alternate definitions
                        if (blockDefs[j].Confidence < best.def.Confidence)
                        {
                            blockDefs[j].IsAlternateDefinition = true;
                            blockDefs[j].PrimaryDefinition = best.def.DefinitionText;
                        }
                    }
                }
            }
        }
    }

    private static string NormalizeTerm(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        text = text.Trim();

        // Remove leading/trailing punctuation (using Unicode escapes for smart quotes)
        var trimChars = new[] { '\uFF0C', '\u3002', '\u3001', '\uFF1B', '\uFF1A', '\u201C', '\u201D', '\u2018', '\u2019', '\u300C', '\u300D', '\uFF08', '\uFF09', '(', ')' };
        text = text.TrimStart(trimChars);
        text = text.TrimEnd(trimChars);

        // Remove "的" prefix (e.g., "的丞相" → "丞相")
        if (text.StartsWith("的") && text.Length > 2)
            text = text.Substring(1);

        return text;
    }

    private static bool IsNoise(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return true;
        if (text.Length <= 1)
            return true;

        // Pure punctuation / whitespace
        if (Regex.IsMatch(text, @"^[\s\p{P}]+$"))
            return true;

        // Common function words / pronouns that shouldn't be terms
        var noiseWords = new HashSet<string>(StringComparer.Ordinal)
        {
            "这", "那", "哪", "什么", "怎么", "怎样", "如何", "为什么",
            "我", "你", "他", "她", "它", "我们", "你们", "他们", "她们",
            "这个", "那个", "哪个", "这些", "那些", "这里", "那里",
            "一个", "一种", "所有", "一些", "很多", "许多",
            "可以", "能够", "应该", "必须", "需要", "可能",
            "因为", "所以", "但是", "虽然", "如果", "而且",
            "的", "地", "得", "了", "着", "过", "吗", "呢", "吧", "啊",
        };
        if (noiseWords.Contains(text))
            return true;

        return false;
    }
}
