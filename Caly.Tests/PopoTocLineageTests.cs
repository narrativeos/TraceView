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
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Caly.Core.Services;
using Caly.Core.ViewModels;
using Xunit;

namespace Caly.Tests;

/// <summary>
/// Pure parsing of the popo.revisions sidecar (M4-C consumption):
/// ParseTocLineage field mapping, head-from-max derivation, flat dotted overrides
/// expanded into the nested tree /retoc expects, and the row model's derived text.
/// No network: every case feeds JSON literals shaped like the server's documents
/// (contract: narrative-operator-MinerU-Popo docs/toc-metadata-schema.md §10).
/// </summary>
public class PopoTocLineageTests
{
    private const string TwoRevisions = """
    {
      "schema": "popo.revisions",
      "doc_id": "doc-9",
      "revisions": [
        {
          "rev": 2,
          "at": "2026-11-20T08:30:00+00:00",
          "source": "agent",
          "overrides": {"alignment.max_toc_depth": 4, "signals.confidence.pattern": 0.9},
          "rule_pack": {"fingerprint": "ccc1c6785f61aabbccddeeff"},
          "level_diff": [{"node_id": "n7", "old": 2, "new": 3}, {"node_id": "n9", "old": 3, "new": 2}],
          "metrics": {
            "level_jump_violations": 1,
            "l1_share": 0.42,
            "max_depth": 5,
            "numbering": {"mode": "report", "checked": 120, "violations": [{"node_id": "n3"}]}
          }
        },
        {
          "rev": 1,
          "at": "2026-11-19T01:00:00+00:00",
          "source": "user",
          "overrides": {},
          "rule_pack": {"fingerprint": "0123456789abcdef0123456789abcdef"},
          "level_diff": [],
          "metrics": {
            "level_jump_violations": 0,
            "l1_share": 0.31,
            "max_depth": 4,
            "numbering": {"mode": "report", "checked": 118, "violations": []}
          }
        }
      ]
    }
    """;

    [Fact]
    public void ParseTocLineage_MapsFieldsAndDerivesHeadFromMaxRev()
    {
        var lineage = PopoService.ParseTocLineage(TwoRevisions);

        Assert.Equal("doc-9", lineage.DocId);
        Assert.Equal(2, lineage.Revisions.Count);
        // The revisions document carries no head field; head is the highest rev,
        // regardless of the order the ledger lists them in.
        Assert.Equal(2, lineage.HeadRevision);

        var head = lineage.Revisions.Single(r => r.Rev == 2);
        Assert.Equal("agent", head.Source);
        Assert.Equal("ccc1c6785f61aabbccddeeff", head.Fingerprint);
        Assert.Equal(2, head.LevelDiffCount);

        // The compare view consumes the same level_diff as node-id-aligned entries.
        Assert.Equal(2, head.LevelDiffs.Count);
        Assert.Equal("n7", head.LevelDiffs[0].NodeId);
        Assert.Equal(2, head.LevelDiffs[0].Old);
        Assert.Equal(3, head.LevelDiffs[0].New);
        Assert.Equal("n9", head.LevelDiffs[1].NodeId);
        Assert.Equal(3, head.LevelDiffs[1].Old);
        Assert.Equal(2, head.LevelDiffs[1].New);

        Assert.Equal(1, head.LevelJumpViolations);
        Assert.Equal(5, head.MaxDepth);
        Assert.Equal(1, head.NumberingViolationCount);
        Assert.True(head.NeedsAttention);

        var first = lineage.Revisions.Single(r => r.Rev == 1);
        Assert.Equal(0, first.LevelDiffCount);
        Assert.Empty(first.LevelDiffs);
        Assert.Equal(0, first.NumberingViolationCount);
        Assert.False(first.NeedsAttention);
    }

    [Fact]
    public void ParseTocLineage_ExpandsDottedOverridesIntoNestedTree()
    {
        var lineage = PopoService.ParseTocLineage(TwoRevisions);

        var overrides = lineage.Revisions.Single(r => r.Rev == 2).Overrides;
        Assert.NotNull(overrides);
        Assert.Equal(4, overrides!["alignment"]!["max_toc_depth"]!.GetValue<int>());
        Assert.Equal(0.9, overrides["signals"]!["confidence"]!["pattern"]!.GetValue<double>());
        // A dotted key must live inside its group, not beside it.
        Assert.Null(overrides["alignment.max_toc_depth"]);

        // Empty overrides record "replayed with the clean pack": no tree, replay restores defaults.
        Assert.Null(lineage.Revisions.Single(r => r.Rev == 1).Overrides);
    }

    [Fact]
    public void ParseTocLineage_NumberingViolationsDriveAttentionAlone()
    {
        var json = """
        {
          "schema": "popo.revisions",
          "doc_id": "d",
          "revisions": [
            {
              "rev": 1,
              "at": "x",
              "source": "user",
              "rule_pack": {"fingerprint": "f"},
              "level_diff": [],
              "metrics": {
                "level_jump_violations": 0,
                "l1_share": 0.2,
                "max_depth": 3,
                "numbering": {"mode": "report", "checked": 10, "violations": [{"a": 1}, {"b": 2}]}
              }
            }
          ]
        }
        """;

        var lineage = PopoService.ParseTocLineage(json);
        var revision = Assert.Single(lineage.Revisions);
        Assert.Equal(2, revision.NumberingViolationCount);
        Assert.True(revision.NeedsAttention);
    }

    [Fact]
    public void ParseTocLineage_MissingOrEmptyRevisionsYieldsEmptyLedger()
    {
        var empty = PopoService.ParseTocLineage("""{"schema": "popo.revisions", "doc_id": "d", "revisions": []}""");
        Assert.Equal("d", empty.DocId);
        Assert.Empty(empty.Revisions);
        Assert.Equal(0, empty.HeadRevision);

        var absent = PopoService.ParseTocLineage("""{"schema": "popo.revisions", "doc_id": "d"}""");
        Assert.Empty(absent.Revisions);
    }

    [Fact]
    public void ParseTocLineage_MalformedJsonThrows()
    {
        // JsonReaderException derives from JsonException; both shapes reach callers alike.
        Assert.ThrowsAny<JsonException>(() => PopoService.ParseTocLineage("not json at all"));
    }

    [Fact]
    public void NestRuleOverrides_BuildsNestedChainAndPreservesValues()
    {
        var flat = new JsonObject
        {
            ["alignment.max_toc_depth"] = 4,
            ["signals.confidence.pattern"] = 0.9,
            ["a.b.c"] = "v",
        };

        var nested = PopoService.NestRuleOverrides(flat);

        Assert.Equal(4, nested["alignment"]!["max_toc_depth"]!.GetValue<int>());
        Assert.Equal(0.9, nested["signals"]!["confidence"]!["pattern"]!.GetValue<double>());
        Assert.Equal("v", nested["a"]!["b"]!["c"]!.GetValue<string>());
        Assert.Equal(3, nested.Count);
    }

    [Fact]
    public void TocRevisionEntryViewModel_FormatsRowAndTruncatesFingerprint()
    {
        var revision = PopoService.ParseTocLineage(TwoRevisions).Revisions.Single(r => r.Rev == 2);
        var row = new TocRevisionEntryViewModel(revision);

        Assert.Equal(2, row.Rev);
        Assert.Equal("agent", row.Source);
        // Long pack fingerprints are shortened for the panel row.
        Assert.Equal("ccc1c6785f61", row.Fingerprint);
        Assert.Contains("层级变更 2", row.SummaryText);
        Assert.Contains("深度 5", row.SummaryText);
        Assert.True(row.Warning);
        // The replay hand-off is the nested tree, same as on the model.
        Assert.Equal(4, row.NestedOverrides!["alignment"]!["max_toc_depth"]!.GetValue<int>());
    }
}
