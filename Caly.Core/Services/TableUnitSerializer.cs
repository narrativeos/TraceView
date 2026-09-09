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
using System.Text;
using Caly.Core.Models;

namespace Caly.Core.Services;

/// <summary>
/// Serializes structured Popo tables into natural-language text for NLP analysis
/// (entity-extraction-quality-spec §14.1 A4).
///
/// table_data: a header line ("group·name (unit) | ...") followed by one
/// "名称(单位): 值" sentence per row. Large tables (&gt; 20 rows) skip rows whose
/// cells are all numeric/empty (pure data grids carry no term value); in-table
/// TOC groups are marked with "# {numbering} {title}" comment lines.
///
/// table_key_value: one "键: 值" line per pair.
///
/// Pure unit: no I/O, no UI dependencies.
/// </summary>
public static class TableUnitSerializer
{
    /// <summary>Row count above which a table is considered "large" (numeric rows skipped).</summary>
    public const int LargeTableThreshold = 20;

    /// <summary>
    /// Serializes a table node by its type. Returns an empty string for
    /// non-table types or tables without structured data.
    /// </summary>
    public static string Serialize(AnalysisTreeNode node)
    {
        return node.Type switch
        {
            "table_data" => SerializeTableData(node),
            "table_key_value" => SerializeTableKeyValue(node),
            _ => string.Empty
        };
    }

    /// <summary>
    /// Serializes a table_data node: header line + one "名称: 值" sentence per row.
    /// </summary>
    public static string SerializeTableData(AnalysisTreeNode node)
    {
        var headers = node.Headers;
        var rows = node.Rows;
        if ((headers == null || headers.Count == 0) && (rows == null || rows.Count == 0))
            return string.Empty;

        var sb = new StringBuilder();
        var headerList = (headers ?? new List<TableHeader>()).OrderBy(h => h.Index).ToList();
        var rowList = (rows ?? new List<TableDataRow>()).OrderBy(r => r.RowIndex).ToList();
        var isLarge = rowList.Count > LargeTableThreshold;

        // In-table TOC: row index → group comment line (inserted at group start).
        var groupComments = new Dictionary<int, string>();
        if (node.Toc != null)
        {
            foreach (var item in node.Toc)
            {
                if (item.RowRange == null || item.RowRange.Length < 1)
                    continue;
                var label = string.IsNullOrEmpty(item.Numbering)
                    ? item.Title
                    : $"{item.Numbering} {item.Title}".Trim();
                if (!string.IsNullOrEmpty(label))
                    groupComments[item.RowRange[0]] = $"# {label}";
            }
        }

        if (headerList.Count > 0)
        {
            sb.AppendLine(string.Join(" | ", headerList.Select(h => h.DisplayName)));
        }

        foreach (var row in rowList)
        {
            if (groupComments.TryGetValue(row.RowIndex, out var comment))
                sb.AppendLine(comment);

            if (isLarge && IsAllNumericOrEmpty(row))
                continue;

            var cells = new List<string>();
            foreach (var header in headerList)
            {
                if (!row.Cells.TryGetValue(header.Name, out var cell) &&
                    !row.Cells.TryGetValue(header.DisplayName, out cell))
                    continue;
                var raw = cell.Raw?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(raw))
                    continue;
                cells.Add($"{header.DisplayName}: {raw}");
            }

            if (cells.Count > 0)
                sb.AppendLine(string.Join(", ", cells));
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Serializes a table_key_value node: one "键: 值" line per pair.
    /// </summary>
    public static string SerializeTableKeyValue(AnalysisTreeNode node)
    {
        var pairs = node.Pairs;
        if (pairs == null || pairs.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        foreach (var pair in pairs.OrderBy(p => p.RowIndex))
        {
            var key = pair.Key?.Trim() ?? string.Empty;
            var value = pair.Value?.Raw?.Trim() ?? string.Empty;
            // Pairs without a value carry no information for NLP.
            if (string.IsNullOrEmpty(value))
                continue;
            sb.AppendLine(string.IsNullOrEmpty(key) ? value : $"{key}: {value}");
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// True when every cell of the row is numeric or empty — such rows are pure
    /// data and carry no term value in large tables.
    /// </summary>
    private static bool IsAllNumericOrEmpty(TableDataRow row)
        => row.Cells.Count > 0 &&
           row.Cells.Values.All(c => c.IsNumeric || string.IsNullOrWhiteSpace(c.Raw));
}
