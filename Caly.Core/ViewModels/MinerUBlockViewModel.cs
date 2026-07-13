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

using Avalonia.Media;
using Avalonia.Media.Imaging;
using Caly.Core.Models;
using Caly.Core.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.IO;
using System.Linq;

namespace Caly.Core.ViewModels;

/// <summary>
/// View model for displaying a raw MinerU block in the middle column of the three-column layout.
/// Wraps MinerUMiddlePageBlock for UI binding.
/// </summary>
public partial class MinerUBlockViewModel : ObservableObject
{
    private readonly MinerUMiddlePageBlock _block;
    private readonly string? _artifactsDirectory;
    private Bitmap? _cachedBitmap;

    public MinerUBlockViewModel(MinerUMiddlePageBlock block, string? artifactsDirectory = null)
    {
        _block = block;
        _artifactsDirectory = artifactsDirectory;
    }

    public int Id => _block.Id;
    public string BlockId => _block.BlockId;
    public int Page => _block.Page;
    public string Type => _block.Type;
    public string Content => _block.Content;
    public string SourceLabel => _block.SourceLabel;
    public int Contd => _block.Contd;
    public int Level => _block.Level;
    public int Image => _block.Image;
    public double[] Bbox => _block.Bbox;

    /// <summary>
    /// Related block IDs (UUID strings) for cross-highlighting between MinerU Blocks column and PDF overlay.
    /// Returns a read-only view to prevent external modification.
    /// </summary>
    public System.Collections.Generic.IReadOnlyList<string> RelatedBlockIds => _block.RelatedBlockIds;

    /// <summary>
    /// Source block IDs (UUID strings) referenced by this block.
    /// For para_blocks with sub-blocks, these are the inherited parent block_ids.
    /// Used for matching to Popo tree nodes.
    /// </summary>
    public System.Collections.Generic.IReadOnlyList<string> SourceBlockIds => _block.SourceBlockIds;

    /// <summary>
    /// Destination type: "para" (adopted), "discarded" (rejected), or empty.
    /// </summary>
    public string DestinationType => _block.DestinationType;

    /// <summary>
    /// Block source: "para" (adopted), "discarded" (rejected), or empty.
    /// </summary>
    public string BlockSource => _block.BlockSource;

    /// <summary>
    /// Whether this block's match to its target was a fallback match.
    /// </summary>
    public bool IsFallbackMatch => _block.IsFallbackMatch;

    /// <summary>
    /// Whether this block is from para_blocks (adopted/merged).
    /// </summary>
    public bool IsParaBlock => BlockSource == MinerUConstants.SourcePara;

    /// <summary>
    /// Whether this block is from discarded_blocks (rejected).
    /// </summary>
    public bool IsDiscardedBlock => BlockSource == MinerUConstants.SourceDiscarded;

    /// <summary>
    /// Display badge text for the block's fate.
    /// </summary>
    public string? SourceBadgeText
    {
        get
        {
            return BlockSource switch
            {
                MinerUConstants.SourcePara => MinerUConstants.AdoptedBadge,
                MinerUConstants.SourceDiscarded => MinerUConstants.DiscardedBadge,
                _ => null
            };
        }
    }

    /// <summary>
    /// Whether this block has a source badge to display (para or discarded).
    /// </summary>
    public bool HasSourceBadge => !string.IsNullOrEmpty(BlockSource);

    /// <summary>
    /// Border brush color based on block source.
    /// Uses pre-allocated static brushes from MinerUConstants to avoid per-access allocation.
    /// </summary>
    public IBrush BorderBrush
    {
        get
        {
            return BlockSource switch
            {
                MinerUConstants.SourcePara => MinerUConstants.AdoptedBrush,
                MinerUConstants.SourceDiscarded => MinerUConstants.DiscardedBrush,
                _ => MinerUConstants.DefaultBrush
            };
        }
    }

    /// <summary>
    /// Badge background color. Reuses the same brush as BorderBrush.
    /// </summary>
    public IBrush BadgeBackground => BorderBrush;

    /// <summary>
    /// Gets a short preview of the content for display in the list.
    /// </summary>
    public string ContentPreview
    {
        get
        {
            if (string.IsNullOrEmpty(Content))
                return "[empty]";
            return Content.Length > 100 ? Content.Substring(0, 100) + "..." : Content;
        }
    }

    /// <summary>
    /// Gets a display label combining type and source_label.
    /// </summary>
    public string TypeLabel
    {
        get
        {
            var parts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(Type))
                parts.Add(Type);
            if (!string.IsNullOrEmpty(SourceLabel) && SourceLabel != Type)
                parts.Add(SourceLabel);
            return string.Join(" / ", parts);
        }
    }

    /// <summary>
    /// Gets a color key for the block type (used for visual distinction).
    /// </summary>
    public string TypeColorKey
    {
        get
        {
            return Type.ToLowerInvariant() switch
            {
                "text" or "paragraph" => "Blue",
                "title" or "heading" => "Purple",
                "image" or "figure" => "Green",
                "table" => "Orange",
                "caption" => "Gray",
                _ => "Default"
            };
        }
    }

    /// <summary>
    /// Whether this block represents an image.
    /// </summary>
    public bool IsImage => Type == "image";

    /// <summary>
    /// Bitmap image for display (only for image blocks).
    /// Loads the image from the artifacts directory using the Content field as the image path.
    /// </summary>
    public Bitmap? ImageBitmap
    {
        get
        {
            if (Type != "image")
                return null;

            if (_cachedBitmap is not null)
                return _cachedBitmap;

            if (_artifactsDirectory is null || string.IsNullOrEmpty(Content))
                return null;

            // Get the image filename from Content (e.g., "images/20/20_0.png" or just "20_0.png")
            var imageFileName = Path.GetFileName(Content);
            if (string.IsNullOrEmpty(imageFileName))
                return null;

            var imageExtensions = new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };
            var ext = Path.GetExtension(imageFileName).ToLowerInvariant();
            if (!imageExtensions.Contains(ext))
                return null;

            // Search for the filename in artifacts directory
            try
            {
                var allImageFiles = Directory.GetFiles(_artifactsDirectory, "*.*", SearchOption.AllDirectories)
                    .Where(f => imageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .ToList();

                foreach (var file in allImageFiles)
                {
                    if (Path.GetFileName(file).Equals(imageFileName, StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            _cachedBitmap = new Bitmap(file);
                            return _cachedBitmap;
                        }
                        catch
                        {
                            // Try next file
                        }
                    }
                }
            }
            catch
            {
                // Directory access failed
            }

            return null;
        }
    }

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

            // Strategy 1: Try Content field (may contain image path)
            if (!string.IsNullOrEmpty(Content))
                return Content;

            // Strategy 2: Try SourceLabel
            if (!string.IsNullOrEmpty(SourceLabel))
                return SourceLabel;

            // No path information available from any source
            return "[图片未找到]";
        }
    }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>
    /// Actual rendered height of this block item in the UI.
    /// Set by the view after layout to enable accurate connection line positioning.
    /// </summary>
    [ObservableProperty]
    private double _actualHeight = 60.0;  // Default fallback height
}
