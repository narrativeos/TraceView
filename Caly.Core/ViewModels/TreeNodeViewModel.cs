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
using Avalonia.Collections;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Caly.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace Caly.Core.ViewModels;

/// <summary>
/// View model for displaying a tree node in the tree view.
/// </summary>
public partial class TreeNodeViewModel : ObservableObject
{
    private readonly AnalysisTreeNode _node;
    private readonly string? _artifactsDirectory;

    /// <summary>
    /// Callback invoked when this tree node is selected.
    /// Passes the first original block ID for page overlay highlighting.
    /// Set by AnalysisViewModel during tree construction.
    /// </summary>
    public Action<int?>? OnBlockSelected { get; set; }

    public TreeNodeViewModel(AnalysisTreeNode node, string? artifactsDirectory = null)
    {
        _node = node;
        _artifactsDirectory = artifactsDirectory;
        foreach (var child in node.Children)
        {
            Children.Add(new TreeNodeViewModel(child, artifactsDirectory));
        }
    }

    public string Type => _node.Type;
    public string Title => _node.Title;
    public string Metadata => _node.Metadata;
    public string Content => _node.Content;
    public int Level => _node.Level;
    public List<int> BlockIds => _node.BlockIds;

    /// <summary>
    /// Whether this node represents an image.
    /// </summary>
    public bool IsImage => Type == "image";

    /// <summary>
    /// Bitmap image for display (only for image nodes).
    /// </summary>
    public Bitmap? ImageBitmap
    {
        get
        {
            if (Type != "image" || _artifactsDirectory is null)
                return null;

            // Try to find image file in artifacts directory
            // MinerU stores images in subdirectories like images/ or figure/
            var imageExtensions = new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };
            var searchDirs = new[] { _artifactsDirectory, Path.Combine(_artifactsDirectory, "images"), Path.Combine(_artifactsDirectory, "figure") };

            foreach (var dir in searchDirs)
            {
                if (!Directory.Exists(dir))
                    continue;

                // Search for image files that match block IDs or content reference
                var files = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories)
                    .Where(f => imageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));

                foreach (var file in files)
                {
                    try
                    {
                        return new Bitmap(file);
                    }
                    catch
                    {
                        // Try next file
                    }
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Whether a valid image is available for display.
    /// </summary>
    public bool HasImage => Type == "image" && _artifactsDirectory is not null;

    /// <summary>
    /// Whether to show the content preview (collapsed, non-image nodes).
    /// </summary>
    public bool ShowContentPreview => !IsContentExpanded && !IsImage;

    /// <summary>
    /// Whether to show the full content (expanded, non-image nodes).
    /// </summary>
    public bool ShowFullContent => IsContentExpanded && !IsImage;

    [ObservableProperty]
    private ObservableCollection<TreeNodeViewModel> _children = new();

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isContentExpanded = true;

    /// <summary>
    /// Display title (truncated to 50 chars).
    /// </summary>
    public string DisplayTitle
    {
        get
        {
            // Filter out API placeholder titles
            var rawTitle = Title;
            if (string.IsNullOrEmpty(rawTitle) ||
                rawTitle == "Default Title")
            {
                rawTitle = $"[{Type}]";
            }
            return rawTitle.Length > 50 ? rawTitle.Substring(0, 50) + "..." : rawTitle;
        }
    }

    /// <summary>
    /// Whether the node has a non-empty title.
    /// </summary>
    public bool HasTitle => !string.IsNullOrEmpty(Title);

    /// <summary>
    /// Type icon character for display.
    /// </summary>
    public string TypeIcon
    {
        get
        {
            return Type switch
            {
                "text" => "\uD83D\uDCD4",       // 📄
                "page_number" => "\uD83D\uDD22", // 🔢
                "image" => "\uD83D\uDD92",      // 🖼️
                "table" => "\uD83D\uDCCA",      // 📊
                "title" => "\uD83D\uDCCC",      // 📌
                "root" => "\uD83C\uDF33",       // 🌳
                _ => "\uD83D\uDCC1",            // 📁
            };
        }
    }

    /// <summary>
    /// Type color for card border/accent.
    /// </summary>
    public string TypeColorHex
    {
        get
        {
            return Type switch
            {
                "text" => "#4CAF50",      // Green
                "page_number" => "#9E9E9E", // Gray
                "image" => "#2196F3",     // Blue
                "table" => "#FF9800",     // Orange
                "title" => "#9C27B0",     // Purple
                "root" => "#795548",      // Brown
                _ => "#607D8B",           // Blue Gray
            };
        }
    }

    /// <summary>
    /// Block count shorthand.
    /// </summary>
    public int BlockCount => BlockIds.Count;

    /// <summary>
    /// Page range string like "P1-4" or "P2".
    /// </summary>
    public string PageRange
    {
        get
        {
            var pages = _node.Location.Select(l => l.Page).Distinct().OrderBy(p => p).ToList();
            if (pages.Count == 0) return "";
            if (pages.Count == 1) return $"P{pages[0]}";
            return $"P{pages.First()}-{pages.Last()}";
        }
    }

    /// <summary>
    /// Unique page count.
    /// </summary>
    public int UniquePageCount
    {
        get
        {
            return _node.Location.Select(l => l.Page).Distinct().Count();
        }
    }

    /// <summary>
    /// Content preview (truncated to 120 chars).
    /// </summary>
    public string ContentPreview
    {
        get
        {
            if (string.IsNullOrEmpty(Content))
                return string.Empty;

            // Remove <|txt_split|> markers for cleaner preview
            var clean = Content.Replace("<|txt_split|>", "\n");
            return clean.Length > 120 ? clean[..120] + "..." : clean;
        }
    }

    /// <summary>
    /// Clean content for display (without <|txt_split|> markers).
    /// </summary>
    public string DisplayContent
    {
        get
        {
            if (string.IsNullOrEmpty(Content))
                return string.Empty;
            return Content.Replace("<|txt_split|>", "\n");
        }
    }

    /// <summary>
    /// Whether content is long and needs collapsible display.
    /// </summary>
    public bool IsContentLong
    {
        get
        {
            var clean = DisplayContent;
            return clean.Length > 120;
        }
    }

    /// <summary>
    /// Toggle content expand/collapse.
    /// </summary>
    [RelayCommand]
    private void ToggleContentExpand()
    {
        IsContentExpanded = !IsContentExpanded;
    }

    /// <summary>
    /// Display info string with level and block count.
    /// </summary>
    public string DisplayInfo => $"level:{Level} blocks:[{string.Join(",", BlockIds)}]";

    /// <summary>
    /// Block ids display (first 5 + count).
    /// </summary>
    public string BlockIdsDisplay
    {
        get
        {
            if (BlockIds.Count <= 5)
                return string.Join(",", BlockIds);
            return string.Join(",", BlockIds.Take(5)) + $"... ({BlockIds.Count} total)";
        }
    }

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

        // Notify AnalysisViewModel to highlight corresponding page blocks
        if (IsSelected)
        {
            var firstId = BlockIds.Count > 0 ? BlockIds[0] : (int?)null;
            OnBlockSelected?.Invoke(firstId);
        }
        else
        {
            OnBlockSelected?.Invoke(null);
        }
    }
}