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
/// Unit tests for <see cref="SectionPathResolver"/> (spec §14.1 A1).
/// </summary>
public class SectionPathResolverTests
{
    private static AnalysisTreeNode Node(string type, string title, string content, params string[] sourceBlockIds)
        => new()
        {
            Type = type,
            Title = title,
            Content = content,
            SourceBlockIds = sourceBlockIds.ToList()
        };

    private static MinerUBlock Block(string blockId, int page)
        => new() { BlockId = blockId, Page = page };

    [Fact]
    public void Resolve_BuildsAncestorTitlePathsFromTree()
    {
        var body = Node("text", "", "绝缘电阻试验应按本文件执行。", "body-uuid");
        var s513 = Node("title", "5.13 绝缘电阻试验", "");
        s513.Children.Add(body);
        var ch5 = Node("title", "第5章 总则", "");
        ch5.Children.Add(s513);
        var root = Node("root", "", "");
        root.Children.Add(ch5);

        var doc = new StructureDocument { TreeRoot = root };
        var result = SectionPathResolver.Resolve(doc);

        Assert.Equal("第5章 总则 > 5.13 绝缘电阻试验", result.BlockSectionPaths["body-uuid"]);
    }

    [Fact]
    public void Resolve_TitleNodeWithEmptyTitle_FallsBackToContent()
    {
        var body = Node("text", "", "正文内容。", "body-uuid");
        var title = Node("title", "", "  5.14 工频耐压试验  ");
        title.Children.Add(body);
        var root = Node("root", "", "");
        root.Children.Add(title);

        var doc = new StructureDocument { TreeRoot = root };
        var result = SectionPathResolver.Resolve(doc);

        Assert.Equal("5.14 工频耐压试验", result.BlockSectionPaths["body-uuid"]);
    }

    [Fact]
    public void Resolve_TitleNodeWithEmptyTitleAndContent_DoesNotExtendPath()
    {
        var body = Node("text", "", "正文内容。", "body-uuid");
        var title = Node("title", "", "");
        title.Children.Add(body);
        var root = Node("root", "", "");
        root.Children.Add(title);

        var doc = new StructureDocument { TreeRoot = root };
        var result = SectionPathResolver.Resolve(doc);

        Assert.False(result.BlockSectionPaths.ContainsKey("body-uuid"));
    }

    [Fact]
    public void Resolve_DiscardedBlock_FallsBackToPageMatch()
    {
        var doc = new StructureDocument
        {
            TocEntries = new()
            {
                new TocEntry { Title = "第5章 总则", Level = 1, Page = 10 },
                new TocEntry { Title = "5.13 绝缘电阻试验", Level = 2, Page = 12 },
            },
            PagesBlocks = new Dictionary<int, List<MinerUBlock>>
            {
                [15] = new() { Block("discarded-uuid", 15) }
            }
        };

        var result = SectionPathResolver.Resolve(doc);

        Assert.Equal("第5章 总则 > 5.13 绝缘电阻试验", result.BlockSectionPaths["discarded-uuid"]);
    }

    [Fact]
    public void Resolve_TreePathWinsOverPageFallback()
    {
        var body = Node("text", "", "正文内容。", "body-uuid");
        var title = Node("title", "第5章 总则", "");
        title.Children.Add(body);
        var root = Node("root", "", "");
        root.Children.Add(title);

        var doc = new StructureDocument
        {
            TreeRoot = root,
            TocEntries = new()
            {
                new TocEntry { Title = "第9章 错误章节", Level = 1, Page = 1 },
            },
            PagesBlocks = new Dictionary<int, List<MinerUBlock>>
            {
                [1] = new() { Block("body-uuid", 1) }
            }
        };

        var result = SectionPathResolver.Resolve(doc);

        Assert.Equal("第5章 总则", result.BlockSectionPaths["body-uuid"]);
    }

    [Fact]
    public void Resolve_BuildsSectionAnchorsFromNumbering()
    {
        var doc = new StructureDocument
        {
            TocEntries = new()
            {
                new TocEntry { Title = "范围", Numbering = "1", Page = 3 },
                new TocEntry { Title = "绝缘电阻试验", Numbering = "5.13", Page = 12 },
                new TocEntry { Title = "无编号条目", Page = 20 },
            }
        };

        var result = SectionPathResolver.Resolve(doc);

        Assert.Equal(2, result.SectionAnchors.Count);
        Assert.Equal("范围", result.SectionAnchors["1"].Title);
        Assert.Equal(12, result.SectionAnchors["5.13"].Page);
    }

    [Fact]
    public void Resolve_NullDocument_ReturnsEmpty()
    {
        var result = SectionPathResolver.Resolve(null);

        Assert.Empty(result.BlockSectionPaths);
        Assert.Empty(result.SectionAnchors);
    }

    [Fact]
    public void PagePathAt_PopsDeeperLevelsOnNewChapter()
    {
        var toc = new List<TocEntry>
        {
            new() { Title = "第5章", Level = 1, Page = 10 },
            new() { Title = "5.13", Level = 2, Page = 12 },
            new() { Title = "第6章", Level = 1, Page = 20 },
        };

        Assert.Equal("第5章 > 5.13", SectionPathResolver.PagePathAt(toc, 15));
        Assert.Equal("第6章", SectionPathResolver.PagePathAt(toc, 25));
        Assert.Null(SectionPathResolver.PagePathAt(toc, 5));
        Assert.Null(SectionPathResolver.PagePathAt(null, 10));
    }

    [Fact]
    public void PagePathAt_IgnoresEntriesWithoutPage()
    {
        var toc = new List<TocEntry>
        {
            new() { Title = "无页码", Level = 1, Page = 0 },
            new() { Title = "第5章", Level = 1, Page = 10 },
        };

        Assert.Equal("第5章", SectionPathResolver.PagePathAt(toc, 12));
    }
}
