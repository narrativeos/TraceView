// Copyright (c) 2025 BobLd
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated associated documentation files (the "Software"), to deal
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
using System.Collections.Generic;

namespace Caly.Core.ViewModels;

/// <summary>
/// One row of the default TOC popup: a current-tree heading rendered with the same visual
/// language as the 改前改后对照 rows (level chip + indent + title + page) so the two TOC
/// views read as one design. An immutable snapshot — no change notification needed.
/// </summary>
public sealed class TocEntryViewModel
{
    public required string Title { get; init; }

    /// <summary>Hierarchical level (1-based; the root may carry 0, shown as L0).</summary>
    public required int Level { get; init; }

    /// <summary>Starting page number (1-based), shown small next to the title.</summary>
    public required int Page { get; init; }

    /// <summary>Indent shared with the compare rows so the hierarchy matches across both views.</summary>
    public Thickness Indent => TocCompareBuilder.IndentFor(Level);

    /// <summary>Level chip text, e.g. "L2", mirroring the compare columns' chips.</summary>
    public string LevelText => "L" + Level.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Page label, e.g. "P318", mirroring the compare rows' page chips.</summary>
    public string PageText => "P" + Page.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Projects the flat TOC entries onto compare-styled popup rows.</summary>
    public static IReadOnlyList<TocEntryViewModel> From(IReadOnlyList<TocEntry> entries)
    {
        var rows = new List<TocEntryViewModel>(entries.Count);
        foreach (var entry in entries)
        {
            rows.Add(new TocEntryViewModel
            {
                Title = entry.Title,
                Level = entry.Level,
                Page = entry.Page,
            });
        }

        return rows;
    }
}
