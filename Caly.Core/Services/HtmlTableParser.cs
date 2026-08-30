// Copyright (c) 2025 BobLd
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons for whom the Software is
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
using System.Net;
using System.Text.RegularExpressions;

namespace Caly.Core.Services;

/// <summary>
/// Lightweight parser for simple machine-generated HTML tables (Popo API output).
/// Supports &lt;table&gt;, &lt;tr&gt;, &lt;td&gt;, &lt;th&gt;, colspan, rowspan,
/// HTML entity decoding, and stripping of nested inline tags.
/// </summary>
public static class HtmlTableParser
{
    private static readonly Regex s_tableRegex = new(@"<table[^>]*>([\s\S]*?)</table>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex s_rowRegex = new(@"<tr[^>]*>([\s\S]*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex s_cellRegex = new(@"<(td|th)([^>]*)>([\s\S]*?)</\1>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex s_tagRegex = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex s_brRegex = new(@"<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex s_whitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Parses the first HTML table in the given string into a 2D grid of cell texts.
    /// Returns null if no table is found.
    /// colspan: text goes into the first spanned column, remaining columns are empty.
    /// rowspan: the text is repeated in each spanned row (self-contained rows for data extraction).
    /// </summary>
    public static List<List<string>>? Parse(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return null;

        var tableMatch = s_tableRegex.Match(html);
        if (!tableMatch.Success)
            return null;

        var rowMatches = s_rowRegex.Matches(tableMatch.Groups[1].Value);
        if (rowMatches.Count == 0)
            return null;

        // occupied[(row, col)] = cell text; also tracks cells spanned from above/left
        var occupied = new Dictionary<(int Row, int Col), string>();
        var maxCol = 0;

        for (int r = 0; r < rowMatches.Count; r++)
        {
            var cellMatches = s_cellRegex.Matches(rowMatches[r].Groups[1].Value);
            var col = 0;

            foreach (Match cell in cellMatches)
            {
                // Advance past columns already filled by a rowspan from a previous row
                while (occupied.ContainsKey((r, col)))
                    col++;

                var colspan = GetIntAttribute(cell.Groups[2].Value, "colspan");
                var rowspan = GetIntAttribute(cell.Groups[2].Value, "rowspan");
                var text = CleanCellText(cell.Groups[3].Value);

                for (int dr = 0; dr < rowspan; dr++)
                {
                    for (int dc = 0; dc < colspan; dc++)
                    {
                        var targetCol = col + dc;
                        if (!occupied.ContainsKey((r + dr, targetCol)))
                        {
                            // colspan: text only in the first column; rowspan: repeat text
                            occupied[(r + dr, targetCol)] = dc == 0 ? text : string.Empty;
                        }
                        if (targetCol + 1 > maxCol)
                            maxCol = targetCol + 1;
                    }
                }
                col += colspan;
            }
        }

        var grid = new List<List<string>>(rowMatches.Count);
        for (int r = 0; r < rowMatches.Count; r++)
        {
            var row = new List<string>(maxCol);
            for (int c = 0; c < maxCol; c++)
                row.Add(occupied.TryGetValue((r, c), out var text) ? text : string.Empty);
            grid.Add(row);
        }

        return grid;
    }

    /// <summary>
    /// Reads a positive integer attribute value from an HTML attribute string (default 1).
    /// </summary>
    private static int GetIntAttribute(string attributes, string name)
    {
        var match = Regex.Match(attributes, name + @"=""?(\d+)""?", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, out var value) && value > 0
            ? value
            : 1;
    }

    /// <summary>
    /// Strips nested inline tags (&lt;br&gt; becomes a space), decodes HTML entities,
    /// and normalizes whitespace.
    /// </summary>
    private static string CleanCellText(string inner)
    {
        if (string.IsNullOrEmpty(inner))
            return string.Empty;

        var noBr = s_brRegex.Replace(inner, " ");
        var noTags = s_tagRegex.Replace(noBr, string.Empty);
        var decoded = WebUtility.HtmlDecode(noTags);
        return s_whitespaceRegex.Replace(decoded, " ").Trim();
    }
}