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
    public void SerializeTableData_Compact_HeaderAndFirstColumn()
    {
        var node = new AnalysisTreeNode
        {
            Type = "table_data",
            Headers = new() { H(0, "绕组"), H(1, "分接开关位置"), H(2, "实测值", unit: "Ω") },
            Rows = new()
            {
                Row(0, ("绕组", "高压绕组", false), ("分接开关位置", "1", true), ("实测值", "0.35", true)),
                Row(1, ("绕组", "高压绕组", false), ("分接开关位置", "2", true), ("实测值", "0.36", true)),
                Row(2, ("绕组", "低压绕组", false), ("分接开关位置", "1", true), ("实测值", "0.10", true)),
            }
        };

        var text = TableUnitSerializer.Serialize(node);
        var lines = text.Split('\n');

        // Header row: all column indicators.
        Assert.Equal("绕组 | 分接开关位置 | 实测值 (Ω)", lines[0]);
        // First column: deduplicated row labels.
        Assert.Equal("绕组: 高压绕组, 低压绕组", lines[1]);
        // Numeric data cells (second/third columns) are dropped.
        Assert.DoesNotContain("0.35", text);
        Assert.DoesNotContain("分接开关位置: 1", text);
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
    public void SerializeTableData_FirstColumn_Deduplicated()
    {
        var rows = new List<TableDataRow>();
        for (int i = 0; i < 30; i++)
            rows.Add(Row(i, ("相别", i % 3 == 0 ? "A相" : i % 3 == 1 ? "B相" : "C相", false)));

        var node = new AnalysisTreeNode
        {
            Type = "table_data",
            Headers = new() { H(0, "相别"), H(1, "实测值", unit: "Ω") },
            Rows = rows
        };

        var text = TableUnitSerializer.Serialize(node);

        // 30 rows collapse to the three unique phase labels.
        Assert.Contains("相别: A相, B相, C相", text);
    }

    [Fact]
    public void SerializeTableData_NoHeaders_UsesFirstCell()
    {
        var node = new AnalysisTreeNode
        {
            Type = "table_data",
            Rows = new()
            {
                Row(0, ("col0", "高压", false), ("col1", "100", true)),
                Row(1, ("col0", "低压", false), ("col1", "200", true)),
            }
        };

        var text = TableUnitSerializer.Serialize(node);

        // No header line; first-cell values are the row labels.
        Assert.Contains("高压", text);
        Assert.Contains("低压", text);
    }

    [Fact]
    public void SerializeTableData_OnlyHeaders_NoRows()
    {
        var node = new AnalysisTreeNode
        {
            Type = "table_data",
            Headers = new() { H(0, "绕组"), H(1, "实测值", unit: "Ω") }
        };

        var text = TableUnitSerializer.Serialize(node);

        Assert.Equal("绕组 | 实测值 (Ω)", text);
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
