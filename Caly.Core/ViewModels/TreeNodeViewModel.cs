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

using Avalonia.Collections;
using Caly.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Caly.Core.ViewModels;

/// <summary>
/// View model for displaying a tree node in the tree view.
/// </summary>
public partial class TreeNodeViewModel : ObservableObject
{
    private readonly PopoTreeNode _node;

    public TreeNodeViewModel(PopoTreeNode node)
    {
        _node = node;
        foreach (var child in node.Children)
        {
            Children.Add(new TreeNodeViewModel(child));
        }
    }

    public string Type => _node.Type;
    public string Title => _node.Title;
    public string Metadata => _node.Metadata;
    public string Content => _node.Content;
    public int Level => _node.Level;
    public List<int> BlockIds => _node.BlockIds;

    [ObservableProperty]
    private ObservableCollection<TreeNodeViewModel> _children = new();

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// Display title (truncated to 50 chars).
    /// </summary>
    public string DisplayTitle
    {
        get
        {
            var title = string.IsNullOrEmpty(Title) ? $"[{Type}]" : Title;
            return title.Length > 50 ? title.Substring(0, 50) + "..." : title;
        }
    }

    /// <summary>
    /// Display info string with level and block count.
    /// </summary>
    public string DisplayInfo => $"level:{Level} blocks:[{string.Join(",", BlockIds)}]";

    /// <summary>
    /// Total block count including descendants.
    /// </summary>
    public int TotalBlockCount
    {
        get
        {
            int count = BlockIds.Count;
            foreach (var child in Children)
            {
                count += child.TotalBlockCount;
            }
            return count;
        }
    }

    [RelayCommand]
    private void ToggleExpand()
    {
        IsExpanded = !IsExpanded;
    }

    [RelayCommand]
    private void ToggleSelect()
    {
        IsSelected = !IsSelected;
    }
}