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
using Caly.Core.Models;
using Caly.Core.Services;

namespace Caly.Tests;

/// <summary>
/// Unit tests for <see cref="TitleTermExtractor"/> (spec §14.1 A2).
/// </summary>
public class TitleTermExtractorTests
{
    [Fact]
    public void Clean_StripsDottedSectionNumbers()
    {
        Assert.Equal("绝缘电阻试验", TitleTermExtractor.Clean("5.13 绝缘电阻试验"));
        Assert.Equal("电磁式电压互感器", TitleTermExtractor.Clean("2.0.2 电磁式电压互感器"));
        Assert.Equal("范围", TitleTermExtractor.Clean("1 范围"));
    }

    [Fact]
    public void Clean_StripsChineseChapterNumbers()
    {
        Assert.Equal("总则", TitleTermExtractor.Clean("第5章 总则"));
        Assert.Equal("术语和定义", TitleTermExtractor.Clean("第 3 章 术语和定义"));
    }

    [Fact]
    public void Clean_StripsAppendixMarkers()
    {
        Assert.Equal("接地装置", TitleTermExtractor.Clean("附录A 接地装置"));
    }

    [Fact]
    public void Clean_StripsHtmlTagsKeepingInnerText()
    {
        Assert.Equal("绝缘 电阻", TitleTermExtractor.Clean("<b>绝缘</b> 电阻"));
    }

    [Fact]
    public void Clean_StripsLatexCommands()
    {
        // \mathbf { S } → S (braced argument kept)
        Assert.Equal("R S", TitleTermExtractor.Clean("R\\mathbf { S }"));
        // bare command removed (× sign dropped, surrounding text kept)
        Assert.Equal("试验 方法", TitleTermExtractor.Clean("试验\\times 方法"));
    }

    [Fact]
    public void Clean_StripsOcrMarkers()
    {
        Assert.Equal("绝缘电阻 试验", TitleTermExtractor.Clean("绝缘电阻<|txt_split|>试验"));
    }

    [Fact]
    public void Clean_EmptyOrWhitespace_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, TitleTermExtractor.Clean(null));
        Assert.Equal(string.Empty, TitleTermExtractor.Clean("   "));
    }

    [Fact]
    public void Extract_FiltersPureNumbersAndShortTitles()
    {
        var toc = new List<TocEntry>
        {
            new() { Title = "6.1" },          // number only → stripped → empty
            new() { Title = "100%" },          // no letter
            new() { Title = "A" },             // too short
            new() { Title = "绝缘电阻试验" },
        };

        var terms = TitleTermExtractor.Extract(toc);

        Assert.Equal(new[] { "绝缘电阻试验" }, terms);
    }

    [Fact]
    public void Extract_DeduplicatesOrderPreserving()
    {
        var toc = new List<TocEntry>
        {
            new() { Title = "5.1 绝缘电阻试验" },
            new() { Title = "绝缘电阻试验" },
            new() { Title = "5.2 工频耐压试验" },
        };

        var terms = TitleTermExtractor.Extract(toc);

        Assert.Equal(new[] { "绝缘电阻试验", "工频耐压试验" }, terms);
    }

    [Fact]
    public void Extract_IncludesBackMatterEntries()
    {
        var toc = new List<TocEntry>
        {
            new() { Title = "附录A 接地装置", Source = "back_matter" },
        };

        var terms = TitleTermExtractor.Extract(toc);

        Assert.Equal(new[] { "接地装置" }, terms);
    }

    [Fact]
    public void Extract_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Empty(TitleTermExtractor.Extract(null));
        Assert.Empty(TitleTermExtractor.Extract(new List<TocEntry>()));
    }
}
