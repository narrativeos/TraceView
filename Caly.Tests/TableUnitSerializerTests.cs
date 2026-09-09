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
/// Unit tests for <see cref="TableUnitSerializer"/> (spec §14.1 A4).
/// </summary>
public class TableUnitSerializerTests
{
    private static TableHeader H(int index, string name, string? group = null, string? unit = null)
        => new() { Index = index, Name = name, Group = group, Unit = unit };

    private static TableCellValue Cell(string raw, bool numeric = false)
        => new() { Raw = raw, IsNumeric = numeric };

    private static TableDataRow Row(int index, params (string Header, string Raw, bool Numeric)[] cells)
        => new()
        {
            RowIndex = index,
            Cells = cells.ToDictionary(c => c.Header, c => Cell(c.Raw, c.Numeric))
        };

    [Fact]
    public void SerializeTableData_SmallTable_HeaderAndRows()
    {
        var node = new AnalysisTreeNode
        {
            Type = "table_data",
            Headers = new() { H(0, "绝缘电阻", unit: "GΩ"), H(1, "15s"), H(2, "60s") },
            Rows = new()
            {
                Row(0, ("绝缘电阻", "1000", true), ("15s", "950", true), ("60s", "980", true)),
                Row(1, ("绝缘电阻", "2000", true), ("15s", "1900", true), ("60s", "1950", true)),
            }
        };

        var text = TableUnitSerializer.Serialize(node);

        Assert.Contains("绝缘电阻 (GΩ) | 15s | 60s", text);
        Assert.Contains("绝缘电阻 (GΩ): 1000, 15s: 950, 60s: 980", text);
        Assert.Contains("绝缘电阻 (GΩ): 2000, 15s: 1900, 60s: 1950", text);
    }

    [Fact]
    public void SerializeTableData_GroupHeader_UsesDisplayName()
    {
        var node = new AnalysisTreeNode
        {
            Type = "table_data",
            Headers = new() { H(0, "15s", group: "绝缘电阻(GΩ)") },
            Rows = new() { Row(0, ("15s", "950", true)) }
        };

        var text = TableUnitSerializer.Serialize(node);

        Assert.Contains("绝缘电阻(GΩ)·15s", text);
    }

    [Fact]
    public void SerializeTableData_LargeTable_SkipsAllNumericRows()
    {
        var rows = new List<TableDataRow>();
        for (int i = 0; i < TableUnitSerializer.LargeTableThreshold + 1; i++)
            rows.Add(Row(i, ("值", i.ToString(), true)));

        var node = new AnalysisTreeNode
        {
            Type = "table_data",
            Headers = new() { H(0, "值") },
            Rows = rows
        };

        var text = TableUnitSerializer.Serialize(node);

        // Only the header line remains — all numeric rows skipped.
        Assert.Equal("值", text);
    }

    [Fact]
    public void SerializeTableData_LargeTable_KeepsTextRows()
    {
        var rows = new List<TableDataRow>();
        for (int i = 0; i < TableUnitSerializer.LargeTableThreshold + 1; i++)
            rows.Add(Row(i, ("值", i.ToString(), true), ("备注", i == 5 ? "异常" : "", false)));

        var node = new AnalysisTreeNode
        {
            Type = "table_data",
            Headers = new() { H(0, "值"), H(1, "备注") },
            Rows = rows
        };

        var text = TableUnitSerializer.Serialize(node);

        Assert.Contains("备注: 异常", text);
        // Numeric-only rows are not serialized.
        Assert.DoesNotContain("值: 3, ", text);
    }

    [Fact]
    public void SerializeTableData_InTableToc_InsertsGroupComments()
    {
        var node = new AnalysisTreeNode
        {
            Type = "table_data",
            Headers = new() { H(0, "项目") },
            Rows = new()
            {
                Row(0, ("项目", "变压器油", false)),
                Row(10, ("项目", "电缆", false)),
            },
            Toc = new()
            {
                new TocEntry { Numbering = "A", Title = "变压器", RowRange = new[] { 0, 9 } },
                new TocEntry { Numbering = "B", Title = "电缆", RowRange = new[] { 10, 19 } },
            }
        };

        var text = TableUnitSerializer.Serialize(node);
        var lines = text.Split('\n');

        Assert.Contains("# A 变压器", lines);
        Assert.Contains("# B 电缆", lines);
        // Group comment precedes its first row.
        Assert.True(Array.IndexOf(lines, "# A 变压器") < Array.IndexOf(lines, "项目: 变压器油"));
    }

    [Fact]
    public void SerializeTableKeyValue_OneLinePerPair()
    {
        var node = new AnalysisTreeNode
        {
            Type = "table_key_value",
            Pairs = new()
            {
                new TableKeyValuePair { Key = "额定电压", Value = Cell("10 kV"), RowIndex = 0 },
                new TableKeyValuePair { Key = "相数", Value = Cell("3"), RowIndex = 1 },
                new TableKeyValuePair { Key = "空键", Value = Cell(""), RowIndex = 2 },
            }
        };

        var text = TableUnitSerializer.Serialize(node);
        var lines = text.Split('\n');

        Assert.Equal(new[] { "额定电压: 10 kV", "相数: 3" }, lines);
    }

    [Fact]
    public void Serialize_NonTableType_ReturnsEmpty()
    {
        var node = new AnalysisTreeNode { Type = "text", Content = "正文" };

        Assert.Equal(string.Empty, TableUnitSerializer.Serialize(node));
    }

    [Fact]
    public void SerializeTableData_NoStructuredData_ReturnsEmpty()
    {
        var node = new AnalysisTreeNode { Type = "table_data", Content = "<table>...</table>" };

        Assert.Equal(string.Empty, TableUnitSerializer.Serialize(node));
    }
}
