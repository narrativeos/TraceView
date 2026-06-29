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
using System.Linq;

namespace Caly.Core.ViewModels;

/// <summary>
/// Main view model for the Popo Analysis panel.
/// Manages block list, tree view, aggregation, and markdown preview.
/// </summary>
public partial class PopoAnalysisViewModel : ViewModelBase
{
    private readonly PopoDocument _popoDocument;

    public PopoAnalysisViewModel(PopoDocument popoDocument)
    {
        _popoDocument = popoDocument;

        // Build block view models
        foreach (var block in popoDocument.GetAllBlocks())
        {
            AllBlocks.Add(new BlockViewModel(block));
        }

        // Build tree view model
        if (popoDocument.TreeRoot is not null)
        {
            TreeRoot = new TreeNodeViewModel(popoDocument.TreeRoot);
        }

        // Load aggregations
        Aggregations = popoDocument.Aggregations;

        // Initialize visible types
        VisibleTypes.Add("title");
        VisibleTypes.Add("text");
        VisibleTypes.Add("image");
        VisibleTypes.Add("table");
        VisibleTypes.Add("caption");

        ApplyFilters();
    }

    public PopoDocument PopoDocument => _popoDocument;

    // === Data availability indicators ===
    public bool HasNormalization => _popoDocument.PagesBlocks.Count > 0;
    public bool HasInference => _popoDocument.InferenceBlocks.Count > 0;
    public bool HasTree => _popoDocument.TreeRoot is not null;

    /// <summary>
    /// Human-readable status text showing which stages are available.
    /// </summary>
    public string StatusText
    {
        get
        {
            var parts = new System.Collections.Generic.List<string>();
            if (HasNormalization) parts.Add("归一化 ✓");
            if (HasInference) parts.Add("推理 ✓");
            if (HasTree) parts.Add("树形 ✓");
            return parts.Count > 0 ? string.Join(" | ", parts) : "无 Popo 数据";
        }
    }

    // === Block list ===
    [ObservableProperty]
    private ObservableCollection<BlockViewModel> _allBlocks = new();

    [ObservableProperty]
    private ObservableCollection<BlockViewModel> _filteredBlocks = new();

    [ObservableProperty]
    private string _blockSearchText = string.Empty;

    public HashSet<string> VisibleTypes { get; } = new();

    // === Tree view ===
    [ObservableProperty]
    private TreeNodeViewModel? _treeRoot;

    /// <summary>
    /// Safe access to TreeRoot.Children for TreeView binding.
    /// </summary>
    public ObservableCollection<TreeNodeViewModel>? TreeRootChildren =>
        TreeRoot?.Children;

    // === Aggregation ===
    [ObservableProperty]
    private IReadOnlyList<PopoAggregation>? _aggregations;

    partial void OnTreeRootChanged(TreeNodeViewModel? value)
    {
        // Reload aggregations from PopoDocument
        Aggregations = _popoDocument.Aggregations;
    }

    // === Selection/Highlight ===
    [ObservableProperty]
    private int? _selectedBlockId;

    [ObservableProperty]
    private BlockViewModel? _selectedBlock;

    [ObservableProperty]
    private bool _isSelectedBlockChanged;

    partial void OnSelectedBlockIdChanged(int? value)
    {
        // Clear previous selection
        foreach (var block in AllBlocks)
        {
            block.IsSelected = false;
        }

        // Highlight selected block
        if (value.HasValue)
        {
            foreach (var block in AllBlocks)
            {
                if (block.Id == value.Value)
                {
                    block.IsSelected = true;
                    SelectedBlock = block;
                    break;
                }
            }
        }
        else
        {
            SelectedBlock = null;
        }

        IsSelectedBlockChanged = !IsSelectedBlockChanged; // Trigger refresh
    }

    partial void OnSelectedBlockChanged(BlockViewModel? value)
    {
        // Sync SelectedBlockId when SelectedBlock changes (from ListBox selection)
        if (value is not null)
        {
            SelectedBlockId = value.Id;
        }
        else
        {
            SelectedBlockId = null;
        }
    }

    // === UI state ===
    [ObservableProperty]
    private bool _showBlockOverlay = true;

    [ObservableProperty]
    private bool _showAggregationLines = true;

    [ObservableProperty]
    private bool _isPaneOpen = true;

    // === Type filter commands ===
    [RelayCommand]
    private void ToggleTypeFilter(string type)
    {
        if (VisibleTypes.Contains(type))
        {
            VisibleTypes.Remove(type);
        }
        else
        {
            VisibleTypes.Add(type);
        }
        ApplyFilters();
    }

    [RelayCommand]
    private void SelectBlock(int id)
    {
        SelectedBlockId = id;
    }

    [RelayCommand]
    private void ResetSelection()
    {
        SelectedBlockId = null;
    }

    // === Markdown generation ===
    [ObservableProperty]
    private string _markdownText = string.Empty;

    partial void OnIsPaneOpenChanged(bool value)
    {
        if (value)
        {
            MarkdownText = GenerateMarkdown();
        }
    }

    public string GenerateMarkdown()
    {
        if (TreeRoot is null)
            return string.Empty;

        var sb = new System.Text.StringBuilder();
        GenerateMarkdownRecursive(TreeRoot, sb);
        return sb.ToString();
    }

    private void GenerateMarkdownRecursive(TreeNodeViewModel node, System.Text.StringBuilder sb)
    {
        var type = node.Type;
        var level = node.Level;
        var title = node.Title;
        var content = node.Content;

        if (type == "title")
        {
            var headers = new string('#', level + 1);
            sb.AppendLine($"{headers} {title}");
            sb.AppendLine();
        }
        else if (type == "text")
        {
            // Replace <|txt_split|> with newlines
            var text = content.Replace("<|txt_split|>", "\n");
            if (!string.IsNullOrEmpty(title))
                sb.AppendLine($"## {title}");
            sb.AppendLine(text);
            sb.AppendLine();
        }
        else if (type == "image")
        {
            sb.AppendLine($"![{title}](image_placeholder)");
            if (!string.IsNullOrEmpty(node.Metadata))
                sb.AppendLine($"*图注: {node.Metadata}*");
            sb.AppendLine();
        }
        else if (type == "table")
        {
            sb.AppendLine($"### {title}");
            sb.AppendLine(content);
            sb.AppendLine();
        }
        else if (type == "caption")
        {
            sb.AppendLine($"*{content}*");
            sb.AppendLine();
        }

        // Process children
        foreach (var child in node.Children)
        {
            GenerateMarkdownRecursive(child, sb);
        }
    }

    private void ApplyFilters()
    {
        var filtered = AllBlocks
            .Where(b => VisibleTypes.Contains(b.Type))
            .ToList();

        if (!string.IsNullOrEmpty(BlockSearchText))
        {
            var search = BlockSearchText.ToLowerInvariant();
            filtered = filtered.Where(b => b.Content.ToLowerInvariant().Contains(search)).ToList();
        }

        // Clear old collection
        FilteredBlocks.Clear();
        foreach (var block in filtered)
        {
            FilteredBlocks.Add(block);
        }
    }

    partial void OnBlockSearchTextChanged(string value)
    {
        ApplyFilters();
    }
}