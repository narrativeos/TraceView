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

using System.Text.Json;
using Caly.Core.Models;
using Caly.Core.Services;

namespace Caly.Tests;

/// <summary>
/// Contract tests for the NLP entity-quality (F1/F2) + noun-signal integration:
/// request serialization (policy / noun_signals, snake_case) and response parsing
/// (keep / filter / filter_reason / noun_signals), plus backward compatibility
/// when the NLP omits the new fields.
/// </summary>
public class SemanticNlpContractTests
{
    [Fact]
    public void Request_SerializesPolicyAndNounSignals_SnakeCase()
    {
        var request = new HanLPAnalyzeRequest
        {
            Text = "测试",
            Source = "hanlp_v2",
            Language = "auto",
            Policy = SemanticAnalysisService.DefaultQualityPolicy,
            NounSignals = SemanticAnalysisService.DefaultNounSignalConfig,
        };

        var json = JsonSerializer.Serialize(request, SourceGenerationContext.Default.HanLPAnalyzeRequest);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("policy", out var policy), "policy key missing");
        Assert.True(policy.TryGetProperty("f1_shape", out _), "f1_shape missing");
        Assert.True(policy.TryGetProperty("f2_confidence", out _), "f2_confidence missing");

        Assert.True(root.TryGetProperty("noun_signals", out var ns), "noun_signals key missing");
        Assert.Equal(JsonValueKind.True, ns.GetProperty("enabled").ValueKind);
    }

    [Fact]
    public void Request_NullPolicyAndNounSignals_Omitted()
    {
        var request = new HanLPAnalyzeRequest { Text = "测试" };
        var json = JsonSerializer.Serialize(request, SourceGenerationContext.Default.HanLPAnalyzeRequest);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.False(root.TryGetProperty("policy", out _), "policy should be omitted when null");
        Assert.False(root.TryGetProperty("noun_signals", out _), "noun_signals should be omitted when null");
    }

    [Fact]
    public void Response_ParsesKeepFilterAndNounSignals()
    {
        const string json = """
        {
          "content": {
            "entities": [
              { "id": "ent_001", "text": "5章", "category": "SECTION_REF", "span": [10, 12], "keep": true },
              { "id": "ent_002", "text": "3", "category": "NUMBER", "span": [20, 21],
                "keep": false, "filter": "F2_confidence", "filter_reason": "confidence 0.4 < 0.6" }
            ],
            "noun_signals": [
              { "text": "测量", "pos": "NN", "syntactic_role": "Object", "score": 0.7,
                "span": [5, 7], "evidence": { "head_rel": "dobj", "governing_verb": "进行" } }
            ]
          }
        }
        """;

        var response = JsonSerializer.Deserialize<HanLPAnalyzeResponse>(json, SourceGenerationContext.Default.HanLPAnalyzeResponse)!;
        var content = response.Content!;

        Assert.Equal(2, content.Entities!.Count);
        Assert.True(content.Entities[0].Keep);
        Assert.Null(content.Entities[0].Filter);

        Assert.False(content.Entities[1].Keep);
        Assert.Equal("F2_confidence", content.Entities[1].Filter);
        Assert.Equal("confidence 0.4 < 0.6", content.Entities[1].FilterReason);

        Assert.NotNull(content.NounSignals);
        Assert.Single(content.NounSignals!);
        var signal = content.NounSignals[0];
        Assert.Equal("测量", signal.Text);
        Assert.Equal("NN", signal.Pos);
        Assert.Equal("Object", signal.SyntacticRole);
        Assert.Equal(0.7, signal.Score, 3);
        Assert.Equal(5, signal.Span[0]);
        Assert.Equal(7, signal.Span[1]);
        Assert.NotNull(signal.Evidence);
        Assert.Equal("进行", ((JsonElement)signal.Evidence!["governing_verb"]).GetString());
    }

    [Fact]
    public void Response_MissingKeepAndNounSignals_BackwardCompatible()
    {
        const string json = """
        {
          "content": {
            "entities": [
              { "id": "ent_001", "text": "北京", "category": "LOCATION", "span": [0, 2] }
            ]
          }
        }
        """;

        var response = JsonSerializer.Deserialize<HanLPAnalyzeResponse>(json, SourceGenerationContext.Default.HanLPAnalyzeResponse)!;
        var content = response.Content!;

        // keep defaults to true when the field is absent (backward compatible)
        Assert.True(content.Entities![0].Keep);
        Assert.Null(content.Entities[0].Filter);
        Assert.Null(content.Entities[0].FilterReason);
        // noun_signals absent → null
        Assert.Null(content.NounSignals);
    }

    // ── A3: section path prefix + span offset (spec §14.1 A3) ──

    [Fact]
    public void ApplySectionPathPrefix_PrependsAndMeasures()
    {
        var (text, prefixLen) = SemanticAnalysisService.ApplySectionPathPrefix(
            "绝缘电阻应不小于1000MΩ", "第5章 总则 > 5.13 绝缘电阻试验");

        Assert.Equal("【第5章 总则 > 5.13 绝缘电阻试验】绝缘电阻应不小于1000MΩ", text);
        Assert.Equal("【第5章 总则 > 5.13 绝缘电阻试验】".Length, prefixLen);
        Assert.Equal(text.Length, prefixLen + "绝缘电阻应不小于1000MΩ".Length);
    }

    [Fact]
    public void ApplySectionPathPrefix_NullOrEmpty_NoPrefix()
    {
        var (text1, len1) = SemanticAnalysisService.ApplySectionPathPrefix("正文", null);
        Assert.Equal("正文", text1);
        Assert.Equal(0, len1);

        var (text2, len2) = SemanticAnalysisService.ApplySectionPathPrefix("正文", "   ");
        Assert.Equal("正文", text2);
        Assert.Equal(0, len2);
    }

    [Fact]
    public void MapSpanToContent_SubtractsPrefixLen()
    {
        // Span [10, 12] in prefixed text, prefix length 8 → [2, 4] in original content.
        var span = TermResultBuilder.MapSpanToContent(new[] { 10, 12 }, 8);

        Assert.Equal(new[] { 2, 4 }, span);
    }

    [Fact]
    public void MapSpanToContent_ZeroPrefix_Unchanged()
    {
        var span = TermResultBuilder.MapSpanToContent(new[] { 5, 7 }, 0);

        Assert.Equal(new[] { 5, 7 }, span);
    }

    [Fact]
    public void MapSpanToContent_SpanInPrefix_ClampedToStart()
    {
        // Entity extracted from the prefix itself: span [1, 3], prefix length 8 → [0, 0].
        var span = TermResultBuilder.MapSpanToContent(new[] { 1, 3 }, 8);

        Assert.Equal(new[] { 0, 0 }, span);
    }

    [Fact]
    public void Result_SerializesSectionPathAndPrefixLen()
    {
        var block = new SemanticBlockResult
        {
            Content = "原文内容",
            SectionPath = "第5章 总则 > 5.13 绝缘电阻试验",
            PrefixLen = 20
        };

        var json = JsonSerializer.Serialize(block, SourceGenerationContext.Default.SemanticBlockResult);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("第5章 总则 > 5.13 绝缘电阻试验", root.GetProperty("section_path").GetString());
        Assert.Equal(20, root.GetProperty("prefix_len").GetInt32());
        // Content stores the original text (no prefix).
        Assert.Equal("原文内容", root.GetProperty("content").GetString());
    }

    [Fact]
    public void Result_OldJsonWithoutSectionPath_BackwardCompatible()
    {
        const string json = """
        {
          "block_ids": [1],
          "content": "原文内容"
        }
        """;

        var block = JsonSerializer.Deserialize<SemanticBlockResult>(json, SourceGenerationContext.Default.SemanticBlockResult)!;

        Assert.Null(block.SectionPath);
        Assert.Equal(0, block.PrefixLen);
        Assert.Equal("原文内容", block.Content);
    }
}
