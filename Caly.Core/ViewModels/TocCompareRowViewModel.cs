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

using Avalonia;
using Caly.Core.Models;
using Caly.Core.Services;
using System.Collections.Generic;

namespace Caly.Core.ViewModels;

/// <summary>
/// One aligned row of the TOC 改前/改后 compare view: the same heading node rendered on
/// the left at its pre-replay level and on the right at its post-replay level, with the
/// connecting lane between them carrying a badge when the level changed. Levels are
/// 1-based; the diff (from the lineage head's <c>level_diff</c>) wins over the tree's
/// current Level because the in-memory tree may not reflect the last replay.
/// An immutable snapshot — no change notification needed.
/// </summary>
public sealed class TocCompareRowViewModel
{
    public required string NodeId { get; init; }

    public required string Title { get; init; }

    /// <summary>First location's page, shown small next to both columns' titles.</summary>
    public required string PageText { get; init; }

    /// <summary>Level before the replay (from level_diff.old, or the tree level if unchanged).</summary>
    public required int LeftLevel { get; init; }

    /// <summary>Level after the replay (from level_diff.new, or the tree level if unchanged).</summary>
    public required int RightLevel { get; init; }

    public Thickness LeftMargin => TocCompareBuilder.IndentFor(LeftLevel);

    public Thickness RightMargin => TocCompareBuilder.IndentFor(RightLevel);

    public bool IsChanged => LeftLevel != RightLevel;

    public bool IsUnchanged => !IsChanged;

    /// <summary>Connector badge text for changed rows, e.g. "5→3"; empty when unchanged.</summary>
    public string BadgeText => IsChanged
        ? $"{LeftLevel.ToString(System.Globalization.CultureInfo.InvariantCulture)}→{RightLevel.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
        : string.Empty;
}

/// <summary>
/// Builds the aligned compare rows: a document-order walk of the structure tree emits one
/// row per TOC heading node (same filter as <see cref="PopoJsonService"/>'s tree-derived
/// TOC), and the head revision's node-id-aligned level_diff decides each row's left and
/// right level. Pure function; both sides share one node sequence so rows align by
/// construction.
/// </summary>
public static class TocCompareBuilder
{
    /// <summary>Horizontal indent added per TOC level, matching the tree view's rhythm.</summary>
    public const double IndentPerLevel = 14.0;

    public static Thickness IndentFor(int level) =>
        new(System.Math.Max(0, level - 1) * IndentPerLevel, 0, 0, 0);

    public static List<TocCompareRowViewModel> BuildRows(
        AnalysisTreeNode root,
        IReadOnlyDictionary<string, PopoTocLevelDiff>? levelDiffs)
    {
        var rows = new List<TocCompareRowViewModel>();
        Collect(root, levelDiffs, rows);
        return rows;
    }

    private static void Collect(
        AnalysisTreeNode node,
        IReadOnlyDictionary<string, PopoTocLevelDiff>? levelDiffs,
        List<TocCompareRowViewModel> rows)
    {
        if (PopoJsonService.IsTocHeadingNode(node))
        {
            PopoTocLevelDiff? diff = null;
            if (!string.IsNullOrEmpty(node.NodeId) && levelDiffs is not null)
            {
                levelDiffs.TryGetValue(node.NodeId, out diff);
            }

            rows.Add(new TocCompareRowViewModel
            {
                NodeId = node.NodeId,
                Title = node.Title.Trim(),
                PageText = "P" + node.Location[0].Page.ToString(System.Globalization.CultureInfo.InvariantCulture),
                LeftLevel = diff?.Old ?? node.Level,
                RightLevel = diff?.New ?? node.Level,
            });
        }

        foreach (var child in node.Children)
        {
            Collect(child, levelDiffs, rows);
        }
    }
}
