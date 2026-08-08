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
using CommunityToolkit.Mvvm.ComponentModel;

namespace Caly.Core.ViewModels;

/// <summary>
/// ViewModel for a single node in the term hierarchy tree.
/// Supports expand/collapse for TreeView binding.
/// </summary>
public partial class TermTreeNodeViewModel : ObservableObject
{
    public string Term { get; }
    public string Category { get; }
    public double Score { get; }
    public string? Definition { get; }
    public int Depth { get; }

    [ObservableProperty]
    private bool _isExpanded;

    public ObservableCollection<TermTreeNodeViewModel> Children { get; } = new();

    public bool HasChildren => Children.Count > 0;

    public string ScoreDisplay => $"{Score:F2}";

    public string CategoryColorHex => Category switch
    {
        "MATERIAL" => "#4CAF50",
        "STANDARD" => "#FF9800",
        "PRODUCT" => "#2196F3",
        "PERSON" => "#E91E63",
        "ORGANIZATION" => "#9C27B0",
        "LOCATION" => "#00BCD4",
        "DATE" => "#607D8B",
        "CONCEPT" => "#795548",
        _ => "#757575",
    };

    public string IndentDisplay => new string(' ', Depth * 2);

    public TermTreeNodeViewModel(string term, string category, double score, string? definition, int depth)
    {
        Term = term;
        Category = category;
        Score = score;
        Definition = definition;
        Depth = depth;
        _isExpanded = depth <= 1; // Auto-expand top 2 levels
    }
}
