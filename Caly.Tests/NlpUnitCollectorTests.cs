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
/// Unit tests for <see cref="NlpUnitCollector"/> (spec §14.2 B2):
/// granularity matrix, traceability tiers, segment packing, TOC batching,
/// and smooth degradation.
/// </summary>
public class NlpUnitCollectorTests
{
    private static AnalysisTreeNode Node(string type, string title, string content, params string[] sourceBlockIds)
        => new()
        {
            Type = type,
            Title = title,
            Content = content,
            SourceBlockIds = sourceBlockIds.ToList()
        };

    private static MinerUBlock Block(string blockId, string type, string content, int page = 1)
        => new() { BlockId = blockId, Type = type, Content = content, Page = page };

    private static Dictionary<string, MinerUBlock> Map(params MinerUBlock[] blocks)
        => blocks.ToDictionary(b => b.BlockId);

    [Fact]
    public void Collect_NullOrEmptyDoc_ReturnsEmpty()
    {
        Assert.Empty(NlpUnitCollector.Collect(null, Map()));
        Assert.Empty(NlpUnitCollector.Collect(new StructureDocument(), Map()));
    }

    [Fact]
    public void Collect_MediumTextNode_SingleUnitWithPrefixAndTraceability()
    {
        var body = Node("text", "", new string('测', 100), "sb-1", "sb-2");
        body.NodeId = "n-body";
        body.BlockIds = new List<int> { 11, 12 };
        body.Location = new List<LocationEntry> { new() { Page = 3 } };
        var sec = Node("title", "5.1 试验", "");
        sec.Children.Add(body);
        var root = Node("root", "", "");
        root.Children.Add(sec);

        var units = NlpUnitCollector.Collect(new StructureDocument { TreeRoot = root }, Map());

        var unit = Assert.Single(units);
        Assert.Equal("text", unit.UnitType);
        Assert.Equal("n-body", unit.OriginNodeId);
        Assert.Equal("5.1 试验", unit.SectionPath);
        Assert.Equal($"【5.1 试验】{new string('测', 100)}", unit.Text);
        Assert.Equal("【5.1 试验】".Length, unit.PrefixLen);
        Assert.Equal(new List<string> { "sb-1", "sb-2" }, unit.SourceBlockIds);
        Assert.Equal(new List<int> { 11, 12 }, unit.BlockIds);
        Assert.Single(unit.Locations);
        Assert.Equal(3, unit.Locations[0].Page);
        Assert.Null(unit.SegmentRange);
    }

    [Fact]
    public void Collect_TitleNode_Skipped()
    {
        var sec = Node("title", "5.1 试验", "");
        var body = Node("text", "", new string('测', 100), "sb-1");
        sec.Children.Add(body);
        var root = Node("root", "", "");
        root.Children.Add(sec);

        var units = NlpUnitCollector.Collect(new StructureDocument { TreeRoot = root }, Map());

        var unit = Assert.Single(units);
        Assert.Equal("text", unit.UnitType);
    }

    [Fact]
    public void Collect_ShortText_MergesIntoPreviousUnit()
    {
        var longBody = Node("text", "", new string('甲', 100), "sb-1");
        longBody.NodeId = "n-long";
        var shortBody = Node("text", "", "注：见附录。", "sb-2");
        shortBody.NodeId = "n-short";
        var sec = Node("title", "5.1 试验", "");
        sec.Children.Add(longBody);
        sec.Children.Add(shortBody);
        var root = Node("root", "", "");
        root.Children.Add(sec);

        var units = NlpUnitCollector.Collect(new StructureDocument { TreeRoot = root }, Map());

        var unit = Assert.Single(units);
        Assert.Equal("n-long", unit.OriginNodeId);
        Assert.Contains("注：见附录。", unit.Text);
        Assert.Equal(new List<string> { "sb-1", "sb-2" }, unit.SourceBlockIds);
        Assert.Null(unit.SegmentRange);
    }

    [Fact]
    public void Collect_ShortText_FirstNode_EmittedStandalone()
    {
        var shortBody = Node("text", "", "短文本。", "sb-1");
        var root = Node("root", "", "");
        root.Children.Add(shortBody);

        var units = NlpUnitCollector.Collect(new StructureDocument { TreeRoot = root }, Map());

        var unit = Assert.Single(units);
        Assert.Equal("短文本。", unit.Text);
        Assert.Equal(0, unit.PrefixLen);
    }

    [Fact]
    public void Collect_LongText_PacksAtSegmentBoundaries()
    {
        var seg = new string('测', 500);
        var content = string.Join(NlpUnitCollector.SegmentSeparator,
            new[] { seg, seg, seg, seg, seg });
        var body = Node("text", "", content, "sb-1", "sb-2", "sb-3", "sb-4", "sb-5");
        body.NodeId = "n-long";
        var root = Node("root", "", "");
        root.Children.Add(body);

        var units = NlpUnitCollector.Collect(new StructureDocument { TreeRoot = root }, Map());

        // 5×500 + 4×13 separators = 2552 > 2000 → chunks [0..2] and [3..4].
        Assert.Equal(2, units.Count);
        Assert.Equal(new[] { 0, 2 }, units[0].SegmentRange);
        Assert.Equal(new[] { 3, 4 }, units[1].SegmentRange);
        // Sanitized text drops the separators.
        Assert.Equal(3 * 500, units[0].Text.Length);
        Assert.Equal(2 * 500, units[1].Text.Length);
        // SegmentDirect tier: chunk 1 ↔ sb-1..sb-3, chunk 2 ↔ sb-4..sb-5.
        Assert.Equal(new List<string> { "sb-1", "sb-2", "sb-3" }, units[0].SourceBlockIds);
        Assert.Equal(new List<string> { "sb-4", "sb-5" }, units[1].SourceBlockIds);
    }

    [Fact]
    public void Collect_TierSegmentOffset_FirstBlockIsTitle_SkipsFirstBlock()
    {
        var seg = new string('测', 60);
        var content = string.Join(NlpUnitCollector.SegmentSeparator, new[] { seg, seg });
        var body = Node("text", "", content, "sb-title", "sb-1", "sb-2");
        body.BlockIds = new List<int> { 1, 2, 3 };
        var root = Node("root", "", "");
        root.Children.Add(body);

        var blockMap = Map(
            Block("sb-title", "title", "5.1 标题"),
            Block("sb-1", "text", seg),
            Block("sb-2", "text", seg));

        var units = NlpUnitCollector.Collect(new StructureDocument { TreeRoot = root }, blockMap);

        var unit = Assert.Single(units);
        // 2 segments vs 3 sbids, first block is a title → offset mapping.
        Assert.Equal(new List<string> { "sb-1", "sb-2" }, unit.SourceBlockIds);
        Assert.Equal(new List<int> { 2, 3 }, unit.BlockIds);
    }

    [Fact]
    public void Collect_TierNode_MismatchedDelta_KeepsFullArrays()
    {
        var seg = new string('测', 60);
        var content = string.Join(NlpUnitCollector.SegmentSeparator, new[] { seg, seg });
        var body = Node("text", "", content, "sb-1", "sb-2", "sb-3", "sb-4");
        body.BlockIds = new List<int> { 1, 2, 3, 4 };
        var root = Node("root", "", "");
        root.Children.Add(body);

        var units = NlpUnitCollector.Collect(new StructureDocument { TreeRoot = root }, Map(
            Block("sb-1", "text", seg),
            Block("sb-2", "text", seg),
            Block("sb-3", "text", seg),
            Block("sb-4", "text", seg)));

        var unit = Assert.Single(units);
        // 2 segments vs 4 sbids → node tier → full arrays.
        Assert.Equal(new List<string> { "sb-1", "sb-2", "sb-3", "sb-4" }, unit.SourceBlockIds);
        Assert.Equal(new List<int> { 1, 2, 3, 4 }, unit.BlockIds);
    }

    [Fact]
    public void Collect_AggregationNodeWithChildren_Skipped_NoDuplication()
    {
        var agg = Node("text", "", "", "sb-1", "sb-2");
        agg.NodeId = "n-agg";
        var child = Node("text", "", new string('测', 100), "sb-1", "sb-2");
        child.NodeId = "n-child";
        agg.Children.Add(child);
        var root = Node("root", "", "");
        root.Children.Add(agg);

        var units = NlpUnitCollector.Collect(new StructureDocument { TreeRoot = root }, Map());

        // Only the child's content is emitted — the aggregation node is skipped.
        var unit = Assert.Single(units);
        Assert.Equal("n-child", unit.OriginNodeId);
    }

    [Fact]
    public void Collect_EmptyLeafNode_DegradesToBlockUnits()
    {
        var leaf = Node("text", "", "", "sb-1", "sb-title", "sb-img");
        leaf.NodeId = "n-leaf";
        var root = Node("root", "", "");
        root.Children.Add(leaf);

        var blockMap = Map(
            Block("sb-1", "text", new string('测', 50)),
            Block("sb-title", "title", "5.1 标题"),
            Block("sb-img", "image", ""));

        var units = NlpUnitCollector.Collect(new StructureDocument { TreeRoot = root }, blockMap);

        // Only the text block degrades; title/image blocks are skipped.
        var unit = Assert.Single(units);
        Assert.Equal("n-leaf", unit.OriginNodeId);
        Assert.Equal(new List<string> { "sb-1" }, unit.SourceBlockIds);
        Assert.Equal(new string('测', 50), unit.Text);
    }

    [Fact]
    public void Collect_TableNode_SerializesStructured()
    {
        var table = Node("table_data", "", "", "sb-t1");
        table.NodeId = "n-table";
        table.Headers = new List<TableHeader>
        {
            new() { Index = 0, Name = "项目" },
            new() { Index = 1, Name = "数值" }
        };
        table.Rows = new List<TableDataRow>
        {
            new()
            {
                RowIndex = 0,
                Cells = new Dictionary<string, TableCellValue>
                {
                    ["项目"] = new() { Raw = "绝缘电阻" },
                    ["数值"] = new() { Raw = "100MΩ" }
                }
            }
        };
        var root = Node("root", "", "");
        root.Children.Add(table);

        var units = NlpUnitCollector.Collect(new StructureDocument { TreeRoot = root }, Map());

        var unit = Assert.Single(units);
        Assert.Equal("table_data", unit.UnitType);
        Assert.Equal("n-table", unit.OriginNodeId);
        // Compact NLP view: header row + first column (row label) are kept.
        Assert.Contains("项目", unit.Text);
        Assert.Contains("数值", unit.Text);
        Assert.Contains("绝缘电阻", unit.Text);
        // Numeric data cells (second column) are dropped.
        Assert.DoesNotContain("100MΩ", unit.Text);
        Assert.Equal(new List<string> { "sb-t1" }, unit.SourceBlockIds);
    }

    [Fact]
    public void Collect_TableWithoutStructuredChildren_FallsBackToHtmlText()
    {
        // A "table" parent with HTML content but no table_data /
        // table_key_value children must not lose its content.
        var table = Node("table", "",
            "<table><tr><td colspan=\"8\">2.试验依据</td></tr><tr><td>GB 50150-2016 电力设备交接试验标准</td></tr></table>",
            "sb-t3");
        table.NodeId = "n-table3";
        var root = Node("root", "", "");
        root.Children.Add(table);

        var units = NlpUnitCollector.Collect(new StructureDocument { TreeRoot = root }, Map());

        var unit = Assert.Single(units);
        Assert.Equal("table_data", unit.UnitType);
        Assert.Equal("n-table3", unit.OriginNodeId);
        Assert.Contains("2.试验依据", unit.Text);
        Assert.Contains("GB 50150-2016", unit.Text);
        Assert.DoesNotContain("<td", unit.Text);
    }

    [Fact]
    public void Collect_TableWithStructuredChildren_ParentNotDuplicated()
    {
        // The structured children cover the parent's content — the parent
        // itself must not emit a second (duplicated) unit.
        var kv = Node("table_key_value", "", "", "sb-t1");
        kv.NodeId = "n-kv";
        kv.Pairs = new List<TableKeyValuePair>
        {
            new() { Key = "设备名称", Value = new TableCellValue { Raw = "110kV 主变" } }
        };
        var table = Node("table", "", "<table><tr><td>设备名称</td><td>110kV 主变</td></tr></table>", "sb-t1");
        table.NodeId = "n-table";
        table.Children.Add(kv);
        var root = Node("root", "", "");
        root.Children.Add(table);

        var units = NlpUnitCollector.Collect(new StructureDocument { TreeRoot = root }, Map());

        var unit = Assert.Single(units);
        Assert.Equal("table_kv", unit.UnitType);
        Assert.Equal("n-kv", unit.OriginNodeId);
    }

    [Fact]
    public void TableHtmlToText_MultiRowTable_PipeSeparated()
    {
        var html = "<table><tr><td>绕组</td><td>实测值(Ω)</td></tr><tr><td>高压侧</td><td>0.3523</td></tr></table>";
        Assert.Equal("绕组 | 实测值(Ω)\n高压侧 | 0.3523", NlpUnitCollector.TableHtmlToText(html));
    }

    [Fact]
    public void TableHtmlToText_NonHtml_StripsTagsOnly()
    {
        Assert.Equal("纯文本 内容", NlpUnitCollector.TableHtmlToText("纯文本 内容"));
        Assert.Equal("甲 乙", NlpUnitCollector.TableHtmlToText("<p>甲</p><p>乙</p>"));
    }

    [Fact]
    public void Collect_CaptionNode_Unit()
    {
        var caption = Node("caption", "", "表 5.1.1 试验设备", "sb-c1");
        caption.NodeId = "n-cap";
        var root = Node("root", "", "");
        root.Children.Add(caption);

        var units = NlpUnitCollector.Collect(new StructureDocument { TreeRoot = root }, Map());

        var unit = Assert.Single(units);
        Assert.Equal("caption", unit.UnitType);
        Assert.Equal("表 5.1.1 试验设备", unit.Text);
    }

    [Fact]
    public void Collect_TocEntries_BatchUnitAtEnd()
    {
        var body = Node("text", "", new string('测', 100), "sb-1");
        var root = Node("root", "", "");
        root.Children.Add(body);

        var doc = new StructureDocument
        {
            TreeRoot = root,
            TocEntries = new List<TocEntry>
            {
                new() { Title = "第5章 试验", Level = 1 },
                new() { Title = "5.1 绝缘电阻", Level = 2 }
            }
        };

        var units = NlpUnitCollector.Collect(doc, Map());

        var toc = units.Last();
        Assert.Equal("toc", toc.UnitType);
        Assert.Equal(0, toc.PrefixLen);
        Assert.Contains("第5章 试验", toc.Text);
        Assert.Contains("5.1 绝缘电阻", toc.Text);
    }

    [Fact]
    public void Collect_TocOversized_SplitsByChapter()
    {
        var entries = new List<TocEntry>();
        for (int i = 0; i < 4; i++)
        {
            entries.Add(new TocEntry { Title = $"第{i + 1}章 " + new string('章', 600), Level = 1 });
            entries.Add(new TocEntry { Title = $"节 {i + 1} " + new string('节', 100), Level = 2 });
        }
        var doc = new StructureDocument
        {
            TreeRoot = Node("root", "", ""),
            TocEntries = entries
        };

        var units = NlpUnitCollector.Collect(doc, Map());

        var tocUnits = units.Where(u => u.UnitType == "toc").ToList();
        // Full TOC ≈ 2850 chars > 2000 → split into one unit per chapter.
        Assert.Equal(4, tocUnits.Count);
    }

    [Fact]
    public void ApplySectionPathPrefix_NullPath_ReturnsUnchanged()
    {
        var (text, prefixLen) = SemanticAnalysisService.ApplySectionPathPrefix("正文", null);
        Assert.Equal("正文", text);
        Assert.Equal(0, prefixLen);
    }

    [Fact]
    public void ApplySectionPathPrefix_WithPath_PrependsBrackets()
    {
        var (text, prefixLen) = SemanticAnalysisService.ApplySectionPathPrefix("正文", "第5章 > 5.1");
        Assert.Equal("【第5章 > 5.1】正文", text);
        Assert.Equal("【第5章 > 5.1】".Length, prefixLen);
    }
}
