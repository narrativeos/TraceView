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
using System.Text.Json.Nodes;
using Caly.Core.Services;

namespace Caly.Core.ViewModels;

/// <summary>
/// One row of the TOC lineage history panel: the recorded replay facts (rev, attribution,
/// pack fingerprint, level changes, quality metrics) and the nested override tree needed
/// to "replay at this rev" (docs/toc-metadata-schema.md M4-C). An immutable snapshot —
/// refreshing the lineage rebuilds the list.
/// </summary>
public sealed class TocRevisionEntryViewModel
{
    public TocRevisionEntryViewModel(PopoTocRevision revision)
    {
        Revision = revision;
        Rev = revision.Rev;
        Source = revision.Source;
        Fingerprint = revision.Fingerprint.Length > 12
            ? revision.Fingerprint[..12]
            : revision.Fingerprint;
        AtText = FormatAt(revision.At);
        SummaryText = $"层级变更 {revision.LevelDiffCount} · 跳变 {revision.LevelJumpViolations} · " +
                      $"编号 {revision.NumberingViolationCount} · 深度 {revision.MaxDepth}";
        Warning = revision.NeedsAttention;
    }

    public PopoTocRevision Revision { get; }

    /// <summary>Override tree handed to /retoc when replaying at this revision. Null for a
    /// revision recorded without overrides: replaying it restores the clean pack state,
    /// so the replay stays meaningful (the ledger records the fact either way).</summary>
    public JsonObject? NestedOverrides => Revision.Overrides;

    public int Rev { get; }

    public string Source { get; }

    public string Fingerprint { get; }

    public string AtText { get; }

    public string SummaryText { get; }

    /// <summary>Head-row emphasis: any REPORT metric that deserves a look.</summary>
    public bool Warning { get; }

    private static string FormatAt(string at)
    {
        return DateTimeOffset.TryParse(at, out var parsed)
            ? parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : at;
    }
}
