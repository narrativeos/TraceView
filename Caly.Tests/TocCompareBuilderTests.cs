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
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Caly.Core.Models;
using Caly.Core.Services;
using Caly.Core.ViewModels;
using Xunit;

namespace Caly.Tests;

/// <summary>
/// The 改前/改后 compare rows: one aligned row per TOC heading node, document order,
/// left level from the head revision's level_diff.old and right level from .new,
/// unchanged rows falling back to the tree's current level on both sides. The diff
/// must win wherever it speaks because the in-memory tree may not reflect the last
/// replay.
/// </summary>
public class TocCompareBuilderTests
{
    private static AnalysisTreeNode Node(string nodeId, string title, int level, int page,
        string type = "text", params AnalysisTreeNode[] children)
    {
        return new AnalysisTreeNode
        {
            NodeId = nodeId,
            Title = title,
            Type = type,
            Level = level,
            Location = new List<LocationEntry> { new() { Page = page } },
            Children = new ObservableCollection<AnalysisTreeNode>(children),
        };
    }

    private static AnalysisTreeNode MakeTree() =>
        Node("root", "", 0, 1, "root",
            Node("n1", "第1章 总则", 1, 1, "text",
                Node("n2", "1.1 适用范围", 2, 2),
                Node("n3", "1.2 术语", 2, 3)),
            Node("n4", "第2章 试验", 1, 5, "text",
                Node("n5", "2.1 绝缘试验", 2, 6),
                Node("n6", "图表", 3, 7, "image"),
                Node("n7", "2.2 机械试验", 3, 8)),
            Node("n8", "Default Title", 1, 9));

    [Fact]
    public void BuildRows_OneAlignedRowPerHeadingInDocumentOrder()
    {
        var rows = TocCompareBuilder.BuildRows(MakeTree(), null);

        // Root, image-type and "Default Title" nodes are not TOC headings.
        Assert.Equal(
            new[] { "第1章 总则", "1.1 适用范围", "1.2 术语", "第2章 试验", "2.1 绝缘试验", "2.2 机械试验" },
            rows.Select(r => r.Title));
        Assert.All(rows, r => Assert.False(r.IsChanged));
        Assert.All(rows, r => Assert.Equal(string.Empty, r.BadgeText));
        // Unchanged rows indent by their tree level.
        Assert.Equal(new Thickness(0, 0, 0, 0), rows[0].LeftMargin);
        Assert.Equal(new Thickness(TocCompareBuilder.IndentPerLevel, 0, 0, 0), rows[1].LeftMargin);
        Assert.Equal(new Thickness(TocCompareBuilder.IndentPerLevel * 2, 0, 0, 0), rows[5].LeftMargin);
    }

    [Fact]
    public void BuildRows_DiffWinsOverTreeLevelOnBothSides()
    {
        var diffs = new Dictionary<string, PopoTocLevelDiff>
        {
            // 2.2 机械试验: level_diff says the replay moved it 3 -> 2, even though the
            // in-memory tree may still show the stale level.
            ["n7"] = new() { NodeId = "n7", Old = 3, New = 2 },
            // n2: a replay that recorded the node without changing it stays unchanged.
            ["n2"] = new() { NodeId = "n2", Old = 2, New = 2 },
        };

        var rows = TocCompareBuilder.BuildRows(MakeTree(), diffs);

        var moved = rows.Single(r => r.Title == "2.2 机械试验");
        Assert.True(moved.IsChanged);
        Assert.Equal(3, moved.LeftLevel);
        Assert.Equal(2, moved.RightLevel);
        Assert.Equal("3→2", moved.BadgeText);
        Assert.Equal(new Thickness(TocCompareBuilder.IndentPerLevel * 2, 0, 0, 0), moved.LeftMargin);
        Assert.Equal(new Thickness(TocCompareBuilder.IndentPerLevel, 0, 0, 0), moved.RightMargin);

        var recordedUnchanged = rows.Single(r => r.Title == "1.1 适用范围");
        Assert.False(recordedUnchanged.IsChanged);
        Assert.Equal(2, recordedUnchanged.LeftLevel);

        // Diffs for nodes absent from the tree are simply ignored: n7 still moves,
        // no phantom row appears for n999.
        var withForeign = new Dictionary<string, PopoTocLevelDiff>(diffs)
        {
            ["n999"] = new() { NodeId = "n999", Old = 1, New = 4 },
        };
        var foreignRows = TocCompareBuilder.BuildRows(MakeTree(), withForeign);
        Assert.Equal(1, foreignRows.Count(r => r.IsChanged));
        Assert.DoesNotContain(foreignRows, r => r.Title == "n999");
    }
}
