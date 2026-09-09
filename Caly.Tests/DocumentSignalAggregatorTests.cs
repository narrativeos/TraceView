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
using Caly.Core.Services;

namespace Caly.Tests;

/// <summary>
/// Unit tests for <see cref="DocumentSignalAggregator"/> (Step C + F3 + F4).
/// </summary>
public class DocumentSignalAggregatorTests
{
    private static SemanticEntity Ent(string text, string cat, double conf, bool keep = true, string? filter = null)
        => new() { Text = text, Category = cat, Confidence = conf, Keep = keep, Filter = filter };

    private static SemanticNounSignal Noun(string text, string role, double score, string pos = "NN")
        => new() { Text = text, SyntacticRole = role, Score = score, Pos = pos };

    private static SemanticBlockResult Block(
        List<SemanticEntity>? entities = null,
        List<SemanticNounSignal>? nouns = null,
        List<SemanticDepToken>? tokens = null,
        List<SemanticDepEdge>? edges = null)
        => new()
        {
            Entities = entities ?? new(),
            NounSignals = nouns ?? new(),
            DepTokens = tokens ?? new(),
            DepEdges = edges ?? new(),
        };

    // ── Step C: noun-signal aggregation ──

    [Fact]
    public void StepC_AggregatesDedupesCountsAndSortsByRole()
    {
        var blocks = new List<SemanticBlockResult>
        {
            Block(nouns: new() { Noun("测量", "Object", 0.7), Noun("电阻", "Subject", 0.7) }),
            Block(nouns: new() { Noun("测量", "Object", 0.7), Noun("试验", "Attributive", 0.55) }),
        };

        var result = new DocumentSignalAggregator().Aggregate(blocks);

        Assert.Equal(4, result.TotalNounSignals);
        Assert.Equal(3, result.NounSignals.Count);

        // Sort: Subject (电阻) > Object (测量) > Attributive (试验).
        Assert.Equal("电阻", result.NounSignals[0].Text);
        Assert.Equal("测量", result.NounSignals[1].Text);
        Assert.Equal("试验", result.NounSignals[2].Text);

        var celiang = result.NounSignals.Single(s => s.Text == "测量");
        Assert.Equal(2, celiang.Occurrences);
        Assert.Equal(2, celiang.BlockCount);
        Assert.Equal("Object", celiang.SyntacticRole);
        Assert.Equal(0.7, celiang.BestScore, 3);
    }

    // ── F3: frequency demotion ──

    [Fact]
    public void F3_HighFrequencyEntity_IsDemoted()
    {
        // "测量" appears 12 times (>= default threshold 10).
        var blocks = Enumerable.Range(0, 12)
            .Select(_ => Block(entities: new() { Ent("测量", "NUMBER", 0.9) }))
            .ToList();

        var result = new DocumentSignalAggregator().Aggregate(blocks);

        var q = result.EntityQuality.Single(e => e.Text == "测量");
        Assert.True(q.IsHighFrequencyNoise);
        Assert.False(q.Keep);
        Assert.Equal("high_frequency_noise", q.Filter);
        Assert.Equal(12, q.Occurrences);
    }

    [Fact]
    public void F3_BelowThreshold_NotDemoted()
    {
        var blocks = Enumerable.Range(0, 5)
            .Select(_ => Block(entities: new() { Ent("测量", "EQUIPMENT", 0.9) }))
            .ToList();

        var result = new DocumentSignalAggregator().Aggregate(blocks);

        var q = result.EntityQuality.Single(e => e.Text == "测量");
        Assert.False(q.IsHighFrequencyNoise);
        Assert.True(q.Keep);
    }

    // ── F4: cross-signal validation ──

    [Fact]
    public void F4_EntityInNounAndDepCore_IsBoosted()
    {
        var block = Block(
            entities: new() { Ent("测量", "NUMBER", 0.5) },
            nouns: new() { Noun("测量", "Object", 0.7) },
            tokens: new() { new() { Id = 0, Text = "进行", Pos = "V" }, new() { Id = 1, Text = "测量", Pos = "NN" } },
            edges: new() { new() { Child = 1, Head = 0, Rel = "dobj" } });

        var result = new DocumentSignalAggregator().Aggregate(new[] { block });

        var q = result.EntityQuality.Single(e => e.Text == "测量");
        Assert.True(q.InNounSignals);
        Assert.True(q.InDepCore);
        Assert.True(q.Boosted);
        Assert.Equal(3, q.SignalSupport);
        Assert.True(q.Keep);
    }

    [Fact]
    public void F4_LowConfidenceNoSupport_IsDemoted()
    {
        var block = Block(entities: new() { Ent("某词", "UNKNOWN", 0.3) });

        var result = new DocumentSignalAggregator().Aggregate(new[] { block });

        var q = result.EntityQuality.Single(e => e.Text == "某词");
        Assert.False(q.Keep);
        Assert.Equal("low_confidence_no_support", q.Filter);
    }

    [Fact]
    public void F4_HighConfidenceNoSupport_IsKept()
    {
        var block = Block(entities: new() { Ent("某设备", "EQUIPMENT", 0.9) });

        var result = new DocumentSignalAggregator().Aggregate(new[] { block });

        var q = result.EntityQuality.Single(e => e.Text == "某设备");
        Assert.True(q.Keep);
    }

    [Fact]
    public void F4_BoostOverridesBlockLevelDemotion()
    {
        // Demoted at block level (keep=false) but boosted by noun + dep core.
        var block = Block(
            entities: new() { Ent("测量", "NUMBER", 0.9, keep: false, filter: "bare_number") },
            nouns: new() { Noun("测量", "Object", 0.7) },
            tokens: new() { new() { Id = 0, Text = "进行", Pos = "V" }, new() { Id = 1, Text = "测量", Pos = "NN" } },
            edges: new() { new() { Child = 1, Head = 0, Rel = "dobj" } });

        var result = new DocumentSignalAggregator().Aggregate(new[] { block });

        var q = result.EntityQuality.Single(e => e.Text == "测量");
        Assert.True(q.Boosted);
        Assert.True(q.Keep);
    }

    // ── F3 wins over F4 boost ──

    [Fact]
    public void F3_WinsOverBoost()
    {
        // High-frequency (12x) AND boosted (noun + dep core) → still demoted by F3.
        var blocks = new List<SemanticBlockResult>();
        for (int i = 0; i < 12; i++)
        {
            blocks.Add(Block(
                entities: new() { Ent("测量", "NUMBER", 0.9) },
                nouns: new() { Noun("测量", "Object", 0.7) },
                tokens: new() { new() { Id = 0, Text = "进行", Pos = "V" }, new() { Id = 1, Text = "测量", Pos = "NN" } },
                edges: new() { new() { Child = 1, Head = 0, Rel = "dobj" } }));
        }

        var result = new DocumentSignalAggregator().Aggregate(blocks);

        var q = result.EntityQuality.Single(e => e.Text == "测量");
        Assert.True(q.IsHighFrequencyNoise);
        Assert.True(q.Boosted);
        Assert.False(q.Keep);
        Assert.Equal("high_frequency_noise", q.Filter);
    }

    // ── Block-level consistent demotion ──

    [Fact]
    public void BlockLevel_DemotedInAllBlocks_IsDemoted()
    {
        var blocks = new List<SemanticBlockResult>
        {
            Block(entities: new() { Ent("某词", "NUMBER", 0.9, keep: false, filter: "bare_number") }),
            Block(entities: new() { Ent("某词", "NUMBER", 0.9, keep: false, filter: "bare_number") }),
        };

        var result = new DocumentSignalAggregator().Aggregate(blocks);

        var q = result.EntityQuality.Single(e => e.Text == "某词");
        Assert.False(q.Keep);
        Assert.Equal("block_level_demoted", q.Filter);
    }

    // ── Edge cases ──

    [Fact]
    public void Aggregate_NullOrEmpty_ReturnsEmpty()
    {
        var agg = new DocumentSignalAggregator();

        var nullResult = agg.Aggregate(null);
        Assert.Equal(0, nullResult.TotalBlocks);
        Assert.Empty(nullResult.NounSignals);
        Assert.Empty(nullResult.EntityQuality);

        var emptyResult = agg.Aggregate(new List<SemanticBlockResult>());
        Assert.Equal(0, emptyResult.TotalBlocks);
        Assert.Empty(emptyResult.EntityQuality);
    }

    [Fact]
    public void DepCore_DetectsNsubjSubject()
    {
        var block = Block(
            entities: new() { Ent("设备", "EQUIPMENT", 0.5) },
            nouns: new() { Noun("设备", "Subject", 0.7) },
            tokens: new() { new() { Id = 0, Text = "进行", Pos = "V" }, new() { Id = 1, Text = "设备", Pos = "NN" } },
            edges: new() { new() { Child = 1, Head = 0, Rel = "nsubj" } });

        var result = new DocumentSignalAggregator().Aggregate(new[] { block });

        var q = result.EntityQuality.Single(e => e.Text == "设备");
        Assert.True(q.InDepCore);
        Assert.True(q.Boosted);
    }
}
