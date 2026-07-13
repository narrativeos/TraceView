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
    private readonly System.Collections.Generic.Dictionary<int, MinerUBlock?>? _blockLookup;

    /// <summary>
    /// Callback invoked when this tree node is selected.
    /// Passes the first original block ID for page overlay highlighting.
    /// Set by AnalysisViewModel during tree construction.
    /// </summary>
    public Action<int?>? OnBlockSelected { get; set; }

    public TreeNodeViewModel(AnalysisTreeNode node, string? artifactsDirectory = null, StructureDocument? structureDocument = null)
    {
        _node = node;
        _artifactsDirectory = artifactsDirectory;

        // Build a lookup dictionary from block ID to MinerUBlock for accurate image matching
        if (structureDocument is not null)
        {
            _blockLookup = new System.Collections.Generic.Dictionary<int, MinerUBlock?>();
            foreach (var block in structureDocument.GetAllBlocks())
            {
                _blockLookup[block.Id] = block;
            }
        }

        // Sort children by their first page number to ensure correct display order.
        // The Popo API sometimes places nodes out of page order (e.g., P1 images after P2/P3 text),
        // so we sort by the first page in each child's location to restore logical reading order.
        var sortedChildren = node.Children
            .OrderBy(child =>
            {
                if (child.Location.Count > 0)
                    return child.Location[0].Page;
                // Nodes without location (e.g., root) go first
                return int.MaxValue;
            })
            .ToList();

        foreach (var child in sortedChildren)
        {
            Children.Add(new TreeNodeViewModel(child, artifactsDirectory, structureDocument));
        }
    }

    public string Type => _node.Type;
    public string Title => _node.Title;
    public string Metadata => _node.Metadata;
    public string Content => _node.Content;
    public int Level => _node.Level;
    public List<int> BlockIds => _node.BlockIds;

    /// <summary>
    /// Source block IDs (UUID strings) from MinerU middle.json that compose this node.
    /// Used for tracing back to the original MinerU blocks for cross-referencing.
    /// </summary>
    public List<string> SourceBlockIds => _node.SourceBlockIds;

    /// <summary>
    /// Whether this node represents an image.
    /// </summary>
    public bool IsImage => Type == "image";

    /// <summary>
    /// Cached bitmap to avoid reloading on every access.
    /// </summary>
    private Bitmap? _cachedBitmap;

    /// <summary>
    /// Bitmap image for display (only for image nodes).
    /// Loads the image using:
    ///   1. _node.ImgPath (from popo_result.json) - resolved relative to _artifactsDirectory
    ///   2. Falls back to block-lookup matching (legacy MinerU format)
    /// </summary>
    public Bitmap? ImageBitmap
    {
        get
        {
            System.Diagnostics.Debug.WriteLine($"[ImageBitmap] GETTER called: Type={Type}, IsImage={IsImage}, cached={_cachedBitmap is not null}");
            if (Type != "image")
                return null;

            if (_cachedBitmap is not null)
                return _cachedBitmap;

            // Strategy 1: Use ImgPath from popo_result.json (most reliable)
            System.Diagnostics.Debug.WriteLine($"[ImageBitmap] ImgPath='{_node.ImgPath}', isEmpty={string.IsNullOrEmpty(_node.ImgPath)}, artifactsDir={_artifactsDirectory}");
            if (!string.IsNullOrEmpty(_node.ImgPath))
            {
                var imgFileName = Path.GetFileName(_node.ImgPath);
                System.Diagnostics.Debug.WriteLine($"[ImageBitmap] fileName={imgFileName}");
                if (!string.IsNullOrEmpty(imgFileName) && _artifactsDirectory is not null)
                {
                    var imageExtensions = new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };
                    var ext = Path.GetExtension(imgFileName).ToLowerInvariant();
                    if (imageExtensions.Contains(ext))
                    {
                        // Search for the filename in artifacts directory
                        var allImageFiles = Directory.GetFiles(_artifactsDirectory, "*.*", SearchOption.AllDirectories)
                            .Where(f => imageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                            .ToList();
                        System.Diagnostics.Debug.WriteLine($"[ImageBitmap] Found {allImageFiles.Count} image files in artifacts dir");

                        foreach (var file in allImageFiles)
                        {
                            if (Path.GetFileName(file).Equals(imgFileName, StringComparison.OrdinalIgnoreCase))
                            {
                                System.Diagnostics.Debug.WriteLine($"[ImageBitmap] Match found: {file}");
                                try
                                {
                                    _cachedBitmap = new Bitmap(file);
                                    return _cachedBitmap;
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[ImageBitmap] Failed to load: {ex.Message}");
                                }
                            }
                        }
                        System.Diagnostics.Debug.WriteLine($"[ImageBitmap] No match found for {imgFileName}");
                    }
                }
            }

            if (_artifactsDirectory is null)
                return null;

            // Get all image files once
            var imageExtensions2 = new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };
            var allImageFiles2 = Directory.GetFiles(_artifactsDirectory, "*.*", SearchOption.AllDirectories)
                .Where(f => imageExtensions2.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => f)
                .ToList();

            if (allImageFiles2.Count == 0)
                return null;

            // Strategy 2: Use BlockIds to look up MinerUBlock and get image filename
            if (_blockLookup is not null && BlockIds.Count > 0)
            {
                foreach (var blockId in BlockIds)
                {
                    if (_blockLookup.TryGetValue(blockId, out var block) && block is not null)
                    {
                        var imageContent = block.Content;
                        if (!string.IsNullOrEmpty(imageContent))
                        {
                            var contentName = Path.GetFileName(imageContent);
                            foreach (var file in allImageFiles2)
                            {
                                if (Path.GetFileName(file).Equals(contentName, StringComparison.OrdinalIgnoreCase))
                                {
                                    try { _cachedBitmap = new Bitmap(file); return _cachedBitmap; }
                                    catch { }
                                }
                            }
                            foreach (var file in allImageFiles2)
                            {
                                if (file.Contains(imageContent))
                                {
                                    try { _cachedBitmap = new Bitmap(file); return _cachedBitmap; }
                                    catch { }
                                }
                            }
                        }
                    }
                }
            }

            // Strategy 3: Match by Content
            if (!string.IsNullOrEmpty(Content))
            {
                var contentName = Path.GetFileName(Content);
                foreach (var file in allImageFiles2)
                {
                    if (Path.GetFileName(file).Equals(contentName, StringComparison.OrdinalIgnoreCase))
                    {
                        try { _cachedBitmap = new Bitmap(file); return _cachedBitmap; }
                        catch { }
                    }
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Whether a valid image is available for display.
    /// </summary>
    public bool HasImage => Type == "image" && (!string.IsNullOrEmpty(_node.ImgPath) || _artifactsDirectory is not null);

    /// <summary>
    /// Whether the image bitmap was successfully loaded.
    /// </summary>
    public bool HasImageBitmap => _cachedBitmap is not null;

    /// <summary>
    /// Image path text for display when image cannot be loaded.
    /// Always shows the image path from any available source, even if the image file doesn't exist.
    /// Only shows "[图片未找到]" when no path information is available at all.
    /// </summary>
    public string ImagePathText
    {
        get
        {
            if (Type != "image")
                return string.Empty;

            // Strategy 1: Use ImgPath from popo_result.json (always show if available)
            if (!string.IsNullOrEmpty(_node.ImgPath))
            {
                return _node.ImgPath;
            }

            // Strategy 2: Try to get path from MinerUBlock by BlockIds
            if (_blockLookup is not null && BlockIds.Count > 0)
            {
                foreach (var blockId in BlockIds)
                {
                    if (_blockLookup.TryGetValue(blockId, out var block) && block is not null
                        && !string.IsNullOrEmpty(block.Content))
                    {
                        return block.Content;
                    }
                }
            }

            // Strategy 3: Try Content field (may contain image path for some formats)
            if (!string.IsNullOrEmpty(Content))
            {
                return Content;
            }

            // Strategy 4: Try Metadata field (may contain image_footnote with path info)
            if (!string.IsNullOrEmpty(Metadata))
            {
                return Metadata;
            }

            // Strategy 5: Try Title (some Popo responses put path info in title)
            if (!string.IsNullOrEmpty(Title))
            {
                return Title;
            }

            // No path information available from any source
            return "[图片未找到]";
        }
    }

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
    /// Actual rendered height of this tree node item in the UI.
    /// Set by the view after layout to enable accurate connection line positioning.
    /// </summary>
    [ObservableProperty]
    private double _actualHeight = 80.0;  // Default fallback height

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