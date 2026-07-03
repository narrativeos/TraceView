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
    private readonly int _imageIndex; // Index among all image nodes (for order-based matching)
    private readonly List<ImageMapEntry>? _imagePathMap; // Image map entries from middle.json

    /// <summary>
    /// Callback invoked when this tree node is selected.
    /// Passes the first original block ID for page overlay highlighting.
    /// Set by AnalysisViewModel during tree construction.
    /// </summary>
    public Action<int?>? OnBlockSelected { get; set; }

    public TreeNodeViewModel(AnalysisTreeNode node, string? artifactsDirectory = null, StructureDocument? structureDocument = null)
        : this(node, artifactsDirectory, structureDocument, -1)
    {
    }

    private TreeNodeViewModel(AnalysisTreeNode node, string? artifactsDirectory, StructureDocument? structureDocument, int imageIndex)
    {
        _node = node;
        _artifactsDirectory = artifactsDirectory;
        _imageIndex = imageIndex;

        // Build a lookup dictionary from block ID to MinerUBlock for accurate image matching
        if (structureDocument is not null)
        {
            _blockLookup = new System.Collections.Generic.Dictionary<int, MinerUBlock?>();
            foreach (var block in structureDocument.GetAllBlocks())
            {
                _blockLookup[block.Id] = block;
            }
        }

        // Build image map from middle.json for image matching
        if (artifactsDirectory is not null)
        {
            _imagePathMap = BuildImagePathMap(artifactsDirectory);
        }

        // Assign image indices to children in document order
        int childImageIndex = Type == "image" ? imageIndex + 1 : imageIndex;
        foreach (var child in node.Children)
        {
            if (child.Type == "image")
                childImageIndex++;
            Children.Add(new TreeNodeViewModel(child, artifactsDirectory, structureDocument, childImageIndex));
        }
    }

    /// <summary>
    /// Image map entry containing image_path and page dimensions.
    /// </summary>
    private sealed class ImageMapEntry
    {
        public string ImagePath;
        public int PageIdx;
        public double PageWidth;
        public double PageHeight;
        public double BboxX1; // Absolute x1
        public double BboxY1; // Absolute y1
        public double BboxX2; // Absolute x2
        public double BboxY2; // Absolute y2

        public ImageMapEntry(string imagePath, int pageIdx, double pageWidth, double pageHeight, double x1, double y1, double x2, double y2)
        {
            ImagePath = imagePath;
            PageIdx = pageIdx;
            PageWidth = pageWidth;
            PageHeight = pageHeight;
            BboxX1 = x1;
            BboxY1 = y1;
            BboxX2 = x2;
            BboxY2 = y2;
        }
    }

    /// <summary>
    /// Builds a list of image map entries by parsing the middle.json.
    /// </summary>
    private static List<ImageMapEntry>? BuildImagePathMap(string artifactsDir)
    {
        var middleJsons = Directory.GetFiles(artifactsDir, "*_middle.json", SearchOption.AllDirectories);
        if (middleJsons.Length == 0)
            return null;

        var entries = new List<ImageMapEntry>();
        foreach (var jsonPath in middleJsons)
        {
            try
            {
                var json = File.ReadAllText(jsonPath);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                ParseMiddleJsonForImages(doc.RootElement, entries);
            }
            catch { }
        }
        return entries.Count > 0 ? entries : null;
    }

    private static void ParseMiddleJsonForImages(System.Text.Json.JsonElement elem, List<ImageMapEntry> entries)
    {
        if (elem.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            if (elem.TryGetProperty("pdf_info", out var pdfInfo))
            {
                foreach (var page in pdfInfo.EnumerateArray())
                {
                    var pageIdx = 0;
                    if (page.TryGetProperty("page_idx", out var pi))
                        pageIdx = pi.GetInt32();

                    var pageW = 1000.0;
                    var pageH = 1000.0;
                    if (page.TryGetProperty("page_size", out var ps))
                    {
                        var psArr = ps.EnumerateArray().Select(v => v.GetDouble()).ToArray();
                        if (psArr.Length >= 2)
                        {
                            pageW = psArr[0];
                            pageH = psArr[1];
                        }
                    }

                    if (page.TryGetProperty("para_blocks", out var blocks))
                    {
                        foreach (var block in blocks.EnumerateArray())
                        {
                            ExtractImagePathFromBlock(block, entries, pageIdx, pageW, pageH);
                        }
                    }
                }
            }
            else if (elem.TryGetProperty("blocks", out var directBlocks))
            {
                foreach (var block in directBlocks.EnumerateArray())
                {
                    ExtractImagePathFromBlock(block, entries, 0, 1000.0, 1000.0);
                }
            }
        }
        else if (elem.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var item in elem.EnumerateArray())
            {
                ExtractImagePathFromBlock(item, entries, 0, 1000.0, 1000.0);
            }
        }
    }

    private static void ExtractImagePathFromBlock(System.Text.Json.JsonElement block, List<ImageMapEntry> entries, int pageIdx, double pageW, double pageH)
    {
        if (block.TryGetProperty("type", out var type) && type.GetString() == "image")
        {
            if (block.TryGetProperty("bbox", out var bbox))
            {
                var bboxArr = bbox.EnumerateArray().Select(v => v.GetDouble()).ToArray();
                if (bboxArr.Length >= 4)
                {
                    string? imagePath = null;
                    if (block.TryGetProperty("blocks", out var nestedBlocks))
                    {
                        imagePath = FindImagePathInBlocks(nestedBlocks);
                    }
                    if (imagePath is not null)
                    {
                        entries.Add(new ImageMapEntry(imagePath, pageIdx, pageW, pageH, bboxArr[0], bboxArr[1], bboxArr[2], bboxArr[3]));
                    }
                }
            }
        }
    }

    private static string? FindImagePathInBlocks(System.Text.Json.JsonElement blocks)
    {
        foreach (var block in blocks.EnumerateArray())
        {
            if (block.TryGetProperty("lines", out var lines))
            {
                foreach (var line in lines.EnumerateArray())
                {
                    if (line.TryGetProperty("spans", out var spans))
                    {
                        foreach (var span in spans.EnumerateArray())
                        {
                            if (span.TryGetProperty("image_path", out var imgPath))
                            {
                                return imgPath.GetString();
                            }
                        }
                    }
                }
            }
        }
        return null;
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
    /// Cached bitmap to avoid reloading on every access.
    /// </summary>
    private Bitmap? _cachedBitmap;

    /// <summary>
    /// Bitmap image for display (only for image nodes).
    /// Uses BlockIds to look up MinerUBlock, or falls back to order-based matching.
    /// </summary>
    public Bitmap? ImageBitmap
    {
        get
        {
            if (Type != "image" || _artifactsDirectory is null)
                return null;

            if (_cachedBitmap is not null)
                return _cachedBitmap;

            // Get all image files once
            var imageExtensions = new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };
            var allImageFiles = Directory.GetFiles(_artifactsDirectory, "*.*", SearchOption.AllDirectories)
                .Where(f => imageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => f) // Sort for consistent order
                .ToList();

            if (allImageFiles.Count == 0)
                return null;

            // Strategy 1: Use BlockIds to look up MinerUBlock and get image filename
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
                            foreach (var file in allImageFiles)
                            {
                                if (Path.GetFileName(file).Equals(contentName, StringComparison.OrdinalIgnoreCase))
                                {
                                    try { _cachedBitmap = new Bitmap(file); return _cachedBitmap; }
                                    catch { }
                                }
                            }
                            foreach (var file in allImageFiles)
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

            // Strategy 2: Match by Content
            if (!string.IsNullOrEmpty(Content))
            {
                var contentName = Path.GetFileName(Content);
                foreach (var file in allImageFiles)
                {
                    if (Path.GetFileName(file).Equals(contentName, StringComparison.OrdinalIgnoreCase))
                    {
                        try { _cachedBitmap = new Bitmap(file); return _cachedBitmap; }
                        catch { }
                    }
                }
            }

            // Strategy 3: Bbox-based matching using middle.json image_path map
            // Match Popo tree node's normalized bbox against middle.json's absolute bboxes
            if (_imagePathMap is not null && _node.Location.Count > 0)
            {
                var loc = _node.Location[0];
                var normX = loc.Bbox.X; // normalized 0-1
                var normY = loc.Bbox.Y; // normalized 0-1
                var treePage = loc.Page; // page number from Popo tree (1-based)
                // Find the best matching entry
                double bestScore = double.MaxValue;
                ImageMapEntry? bestEntry = null;
                foreach (var entry in _imagePathMap)
                {
                    // Match by page first (Popo uses 1-based page numbers, middle.json uses 0-based page_idx)
                    if (entry.PageIdx + 1 != treePage)
                        continue;
                    var absNormX = entry.BboxX1 / entry.PageWidth;
                    var absNormY = entry.BboxY1 / entry.PageHeight;
                    var dx = normX - absNormX;
                    var dy = normY - absNormY;
                    var score = dx * dx + dy * dy;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestEntry = entry;
                    }
                }
                // If we found a close match (within threshold)
                if (bestEntry is not null && bestScore < 0.01)
                {
                    var imageName = Path.GetFileName(bestEntry.ImagePath);
                    foreach (var file in allImageFiles)
                    {
                        if (Path.GetFileName(file).Equals(imageName, StringComparison.OrdinalIgnoreCase))
                        {
                            try { _cachedBitmap = new Bitmap(file); return _cachedBitmap; }
                            catch { }
                        }
                    }
                }
            }

            // Strategy 4: Order-based matching
            // When BlockIds are empty and Content is empty (common in Popo API output),
            // match by the image index among all image nodes in the tree.
            if (_imageIndex >= 0 && _imageIndex < allImageFiles.Count)
            {
                var file = allImageFiles[_imageIndex];
                try { _cachedBitmap = new Bitmap(file); return _cachedBitmap; }
                catch { }
            }

            // No fallback - return null if image not found
            return null;
        }
    }

    /// <summary>
    /// Whether a valid image is available for display.
    /// </summary>
    public bool HasImage => Type == "image" && _artifactsDirectory is not null;

    /// <summary>
    /// Whether the image bitmap was successfully loaded.
    /// </summary>
    public bool HasImageBitmap => _cachedBitmap is not null;

    /// <summary>
    /// Image path text for display when image is not found.
    /// Shows the expected image filename from MinerUBlock or Content.
    /// </summary>
    public string ImagePathText
    {
        get
        {
            if (Type != "image")
                return string.Empty;

            // Try to get filename from MinerUBlock
            if (_blockLookup is not null && BlockIds.Count > 0)
            {
                foreach (var blockId in BlockIds)
                {
                    if (_blockLookup.TryGetValue(blockId, out var block) && block is not null
                        && !string.IsNullOrEmpty(block.Content))
                    {
                        return $"[图片] {Path.GetFileName(block.Content)}";
                    }
                }
            }

            // Try Content
            if (!string.IsNullOrEmpty(Content))
            {
                return $"[图片] {Path.GetFileName(Content)}";
            }

            // Try Metadata
            if (!string.IsNullOrEmpty(Metadata))
            {
                return $"[图片] {Path.GetFileName(Metadata)}";
            }

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