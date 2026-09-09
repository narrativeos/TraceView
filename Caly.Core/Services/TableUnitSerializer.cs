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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Caly.Core.Models;

namespace Caly.Core.Services;

/// <summary>
/// Serializes structured Popo tables into compact natural-language text for NLP
/// analysis (entity-extraction-quality-spec §14.1 A4). The goal is to feed the
/// NLP the table's <em>indicators</em> (metric names / labels) without the bulk
/// of numeric data cells, keeping the request small enough to avoid timeouts.
///
/// table_data: the header row (column indicators, "group·name (unit) | ...")
/// plus the first column's deduplicated values (the row labels). Numeric data
/// cells carry no term value for entity extraction and are dropped.
///
/// table_key_value: one "键: 值" line per pair. Both key and value are kept —
/// the value often carries the entities (company names, model numbers, ratings).
///
/// Pure unit: no I/O, no UI dependencies.
/// </summary>
public static class TableUnitSerializer
{
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
    /// Serializes a table_data node into a compact NLP view: the header row
    /// (the column indicators) plus the first column's deduplicated values
    /// (the row labels). Numeric data cells are dropped — they carry no term
    /// value for entity extraction and would bloat the NLP request.
    /// </summary>
    public static string SerializeTableData(AnalysisTreeNode node)
    {
        var headers = node.Headers;
        var rows = node.Rows;
        if ((headers == null || headers.Count == 0) && (rows == null || rows.Count == 0))
            return string.Empty;

        var headerList = (headers ?? new List<TableHeader>()).OrderBy(h => h.Index).ToList();
        var rowList = (rows ?? new List<TableDataRow>()).OrderBy(r => r.RowIndex).ToList();

        var sb = new StringBuilder();

        // First row: the column headers (the table's indicators).
        if (headerList.Count > 0)
            sb.AppendLine(string.Join(" | ", headerList.Select(h => h.DisplayName)));

        // First column: the row labels, deduplicated (order-preserving).
        var first = headerList.Count > 0 ? headerList[0] : null;
        var values = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rowList)
        {
            string? raw = null;
            if (first != null)
            {
                if (row.Cells.TryGetValue(first.Name, out var cell))
                    raw = cell.Raw;
                else if (row.Cells.TryGetValue(first.DisplayName, out var cell2))
                    raw = cell2.Raw;
            }
            else if (row.Cells.Count > 0)
            {
                raw = row.Cells.Values.FirstOrDefault()?.Raw;
            }

            var value = raw?.Trim();
            if (!string.IsNullOrEmpty(value) && seen.Add(value))
                values.Add(value);
        }

        if (values.Count > 0)
            sb.AppendLine(first != null
                ? $"{first.DisplayName}: {string.Join(", ", values)}"
                : string.Join(", ", values));

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
}
