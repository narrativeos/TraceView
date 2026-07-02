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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Caly.Core.Models;
using Caly.Core.Utilities;
using System;
using System.Collections.Generic;
using System.Globalization;
using UglyToad.PdfPig.Core;

namespace Caly.Core.Controls;

/// <summary>
/// Control that overlays block bounding boxes on a PDF page.
/// Draws colored rectangles for each Popo block on the page.
/// </summary>
public sealed class BlockOverlayControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<MinerUBlock>?> BlocksProperty =
        AvaloniaProperty.Register<BlockOverlayControl, IReadOnlyList<MinerUBlock>?>(nameof(Blocks));

    public static readonly StyledProperty<Rect?> VisibleAreaProperty =
        AvaloniaProperty.Register<BlockOverlayControl, Rect?>(nameof(VisibleArea));

    public static readonly StyledProperty<int?> HighlightBlockIdProperty =
        AvaloniaProperty.Register<BlockOverlayControl, int?>(nameof(HighlightBlockId));

    public static readonly StyledProperty<bool> ShowLabelsProperty =
        AvaloniaProperty.Register<BlockOverlayControl, bool>(nameof(ShowLabels), true);

    /// <summary>
    /// PDF page size for coordinate conversion.
    /// Required to convert normalized (0-1) block coordinates to PDF pixel coordinates.
    /// </summary>
    public static readonly StyledProperty<Size> PageSizeProperty =
        AvaloniaProperty.Register<BlockOverlayControl, Size>(nameof(PageSize));

    static BlockOverlayControl()
    {
        AffectsRender<BlockOverlayControl>(BlocksProperty, VisibleAreaProperty,
            HighlightBlockIdProperty, ShowLabelsProperty, PageSizeProperty);
    }

    public IReadOnlyList<MinerUBlock>? Blocks
    {
        get => GetValue(BlocksProperty);
        set => SetValue(BlocksProperty, value);
    }

    public Rect? VisibleArea
    {
        get => GetValue(VisibleAreaProperty);
        set => SetValue(VisibleAreaProperty, value);
    }

    public int? HighlightBlockId
    {
        get => GetValue(HighlightBlockIdProperty);
        set => SetValue(HighlightBlockIdProperty, value);
    }

    public bool ShowLabels
    {
        get => GetValue(ShowLabelsProperty);
        set => SetValue(ShowLabelsProperty, value);
    }

    public Size PageSize
    {
        get => GetValue(PageSizeProperty);
        set => SetValue(PageSizeProperty, value);
    }

    // Cache for geometries
    private StreamGeometry[]? _blockGeometries;
    private bool _geometriesDirty = true;

    // Fill colors (alpha = 0.15)
    private static readonly ImmutableSolidColorBrush TitleFill = new(Colors.Blue, 0.15);
    private static readonly ImmutableSolidColorBrush TextFill = new(Colors.Green, 0.15);
    private static readonly ImmutableSolidColorBrush ImageFill = new(Colors.Orange, 0.15);
    private static readonly ImmutableSolidColorBrush TableFill = new(Colors.Purple, 0.15);
    private static readonly ImmutableSolidColorBrush CaptionFill = new(Colors.Gray, 0.15);
    private static readonly ImmutableSolidColorBrush DefaultFill = new(Colors.LightGray, 0.15);

    // Stroke colors (alpha = 0.6)
    private static readonly ImmutableSolidColorBrush TitleStroke = new(Colors.Blue, 0.6);
    private static readonly ImmutableSolidColorBrush TextStroke = new(Colors.Green, 0.6);
    private static readonly ImmutableSolidColorBrush ImageStroke = new(Colors.Orange, 0.6);
    private static readonly ImmutableSolidColorBrush TableStroke = new(Colors.Purple, 0.6);
    private static readonly ImmutableSolidColorBrush CaptionStroke = new(Colors.Gray, 0.6);
    private static readonly ImmutableSolidColorBrush DefaultStroke = new(Colors.LightGray, 0.6);

    // Highlight stroke (yellow/amber)
    private static readonly ImmutableSolidColorBrush HighlightStroke = new(Color.Parse("#FFD600"), 1.0);

    private static readonly ImmutablePen DefaultPen = new(DefaultStroke, 1.5);
    private static readonly ImmutablePen HighlightPen = new(HighlightStroke, 3.0);

    // Cached pens per type (avoid per-frame allocation)
    private static readonly ImmutablePen TitlePen = new(TitleStroke, 1.5);
    private static readonly ImmutablePen TextPen = new(TextStroke, 1.5);
    private static readonly ImmutablePen ImagePen = new(ImageStroke, 1.5);
    private static readonly ImmutablePen TablePen = new(TableStroke, 1.5);
    private static readonly ImmutablePen CaptionPen = new(CaptionStroke, 1.5);

    // Cached highlight fill brushes per type color (avoid per-frame allocation)
    private static readonly ImmutableSolidColorBrush TitleHighlightFill = new(Colors.Blue, 0.35);
    private static readonly ImmutableSolidColorBrush TextHighlightFill = new(Colors.Green, 0.35);
    private static readonly ImmutableSolidColorBrush ImageHighlightFill = new(Colors.Orange, 0.35);
    private static readonly ImmutableSolidColorBrush TableHighlightFill = new(Colors.Purple, 0.35);
    private static readonly ImmutableSolidColorBrush CaptionHighlightFill = new(Colors.Gray, 0.35);
    private static readonly ImmutableSolidColorBrush DefaultHighlightFill = new(Colors.LightGray, 0.35);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == BlocksProperty || change.Property == PageSizeProperty)
        {
            _geometriesDirty = true;
        }
    }

    private StreamGeometry[] EnsureGeometries()
    {
        if (!_geometriesDirty)
            return _blockGeometries!;

        var blocks = Blocks;
        if (blocks is null || blocks.Count == 0)
        {
            _blockGeometries = System.Array.Empty<StreamGeometry>();
            _geometriesDirty = false;
            return _blockGeometries;
        }

        var pageSize = PageSize;

        // Reuse existing array if count matches to reduce allocations
        if (_blockGeometries is null || _blockGeometries.Length != blocks.Count)
        {
            _blockGeometries = new StreamGeometry[blocks.Count];
        }

        for (int i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            var bbox = block.Bbox;
            // Convert block bbox (may be normalized 0-1 or absolute pixels) to PdfRectangle
            // Use original PDF coordinates - the Render() context is in the control's local coordinate space
            var pdfRect = BboxToAvaloniaRectangle(bbox, pageSize, block.IsBboxNormalized);
            _blockGeometries[i] = PdfWordHelpers.GetGeometry(pdfRect, false);
        }

        _geometriesDirty = false;
        return _blockGeometries;
    }

    /// <summary>
    /// Converts a MinerUBlock bbox to Avalonia coordinates (top-left origin, Y down).
    /// The Render() method uses Avalonia's coordinate system, not PDF's (bottom-left origin).
    /// </summary>
    private static PdfRectangle BboxToAvaloniaRectangle(Rect bbox, Size pageSize, bool isNormalized)
    {
        // Use explicit IsBboxNormalized flag set by the parsing service
        // instead of heuristic coordinate range checks.

        double x0, y0, x1, y1;

        if (isNormalized && pageSize.Width > 0 && pageSize.Height > 0)
        {
            // Convert normalized coordinates to pixel coordinates (Avalonia: top-left origin, Y down)
            x0 = bbox.X * pageSize.Width;
            y0 = bbox.Y * pageSize.Height;
            x1 = (bbox.X + bbox.Width) * pageSize.Width;
            y1 = (bbox.Y + bbox.Height) * pageSize.Height;
        }
        else
        {
            // Already in pixel coordinates (Avalonia)
            x0 = bbox.X;
            y0 = bbox.Y;
            x1 = bbox.Right;
            y1 = bbox.Bottom;
        }

        // Create PdfRectangle with Avalonia coordinates (top-left origin)
        // PdfRectangle constructor: (bottomLeftX, bottomLeftY, topRightX, topRightY)
        // In Avalonia: bottomLeft = (x0, y1), topRight = (x1, y0)
        return new PdfRectangle(x0, y1, x1, y0);
    }

    private static (ImmutableSolidColorBrush fill, ImmutablePen pen) GetBlockStyle(MinerUBlock block, bool isHighlighted)
    {
        ImmutableSolidColorBrush fill;
        ImmutablePen pen;

        if (isHighlighted)
        {
            pen = HighlightPen;
            fill = block.Type switch
            {
                "title" => TitleHighlightFill,
                "text" => TextHighlightFill,
                "image" => ImageHighlightFill,
                "table" => TableHighlightFill,
                "caption" => CaptionHighlightFill,
                _ => DefaultHighlightFill
            };
        }
        else
        {
            (fill, pen) = block.Type switch
            {
                "title" => (TitleFill, TitlePen),
                "text" => (TextFill, TextPen),
                "image" => (ImageFill, ImagePen),
                "table" => (TableFill, TablePen),
                "caption" => (CaptionFill, CaptionPen),
                _ => (DefaultFill, DefaultPen)
            };
        }

        return (fill, pen);
    }

    public override void Render(DrawingContext context)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        if (!VisibleArea.HasValue || VisibleArea.Value.Width <= 0 || VisibleArea.Value.Height <= 0)
            return;

        // Fill transparent to receive pointer events
        context.FillRectangle(Brushes.Transparent, Bounds);

        var blocks = Blocks;
        if (blocks is null || blocks.Count == 0)
            return;

        var geometries = EnsureGeometries();

        for (int i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            var geometry = geometries[i];

            // Cull blocks outside visible area
            if (!geometry.Bounds.Intersects(VisibleArea.Value))
                continue;

            bool isHighlighted = HighlightBlockId.HasValue && block.Id == HighlightBlockId.Value;
            var (fill, pen) = GetBlockStyle(block, isHighlighted);

            context.DrawGeometry(fill, pen, geometry);

            // Draw block type/content label for OCR verification
            if (ShowLabels && !string.IsNullOrEmpty(block.Content))
            {
                var label = $"{block.Type}: {Truncate(block.Content, 60)}";
                var formattedText = new FormattedText(
                    label,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    Typeface.Default,
                    10,
                    Brushes.Black);

                // Position label at top-left of block geometry
                var labelPos = geometry.Bounds.TopLeft;
                context.DrawText(formattedText, labelPos);
            }
        }
    }

    /// <summary>
    /// Truncates text to a maximum length, adding "..." if needed.
    /// </summary>
    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;
        return text.Substring(0, maxLength - 3) + "...";
    }
}
