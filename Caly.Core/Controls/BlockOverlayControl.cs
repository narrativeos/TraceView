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
using Avalonia.Input;
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

    /// <summary>
    /// PPI scale factor for converting PDF points to display pixels.
    /// Used when block coordinates are in PDF point space (not normalized).
    /// Defaults to 1.0 (no scaling). Typically 2.0 on high-DPI displays.
    /// </summary>
    public static readonly StyledProperty<double> PpiScaleProperty =
        AvaloniaProperty.Register<BlockOverlayControl, double>(nameof(PpiScale), 1.0);

    /// <summary>
    /// Current zoom level from PageItemsControl.
    /// Used to compensate label font size so text stays readable when zoomed out.
    /// </summary>
    public static readonly StyledProperty<double> ZoomLevelProperty =
        AvaloniaProperty.Register<BlockOverlayControl, double>(nameof(ZoomLevel), 1.0);

    static BlockOverlayControl()
    {
        AffectsRender<BlockOverlayControl>(BlocksProperty, VisibleAreaProperty,
            HighlightBlockIdProperty, ShowLabelsProperty, PageSizeProperty,
            PpiScaleProperty, ZoomLevelProperty);
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

    public double PpiScale
    {
        get => GetValue(PpiScaleProperty);
        set => SetValue(PpiScaleProperty, value);
    }

    public double ZoomLevel
    {
        get => GetValue(ZoomLevelProperty);
        set => SetValue(ZoomLevelProperty, value);
    }

    // Cache for geometries
    private StreamGeometry[]? _blockGeometries;
    private bool _geometriesDirty = true;

    // Hover tracking for label display
    private int _hoveredBlockIndex = -1;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    public BlockOverlayControl()
    {
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var pos = e.GetPosition(this);
        int prev = _hoveredBlockIndex;
        _hoveredBlockIndex = -1;

        var geometries = _blockGeometries;
        if (geometries is not null)
        {
            for (int i = 0; i < geometries.Length; i++)
            {
                if (geometries[i].Bounds.Contains(pos))
                {
                    _hoveredBlockIndex = i;
                    break;
                }
            }
        }

        if (_hoveredBlockIndex != prev)
            InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);

        if (_hoveredBlockIndex != -1)
        {
            _hoveredBlockIndex = -1;
            InvalidateVisual();
        }
    }

    // Fill colors (alpha = 0.30)
    private static readonly ImmutableSolidColorBrush TitleFill = new(Colors.Blue, 0.30);
    private static readonly ImmutableSolidColorBrush TextFill = new(Colors.Green, 0.30);
    private static readonly ImmutableSolidColorBrush ImageFill = new(Colors.Orange, 0.30);
    private static readonly ImmutableSolidColorBrush TableFill = new(Colors.Purple, 0.30);
    private static readonly ImmutableSolidColorBrush CaptionFill = new(Colors.Gray, 0.30);
    private static readonly ImmutableSolidColorBrush DefaultFill = new(Colors.LightGray, 0.30);

    // Stroke colors (alpha = 0.85)
    private static readonly ImmutableSolidColorBrush TitleStroke = new(Colors.Blue, 0.85);
    private static readonly ImmutableSolidColorBrush TextStroke = new(Colors.Green, 0.85);
    private static readonly ImmutableSolidColorBrush ImageStroke = new(Colors.Orange, 0.85);
    private static readonly ImmutableSolidColorBrush TableStroke = new(Colors.Purple, 0.85);
    private static readonly ImmutableSolidColorBrush CaptionStroke = new(Colors.Gray, 0.85);
    private static readonly ImmutableSolidColorBrush DefaultStroke = new(Colors.LightGray, 0.85);

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

    // Hover pens (thicker, solid)
    private static readonly ImmutablePen TitleHoverPen = new(TitleHoverStroke, 2.5);
    private static readonly ImmutablePen TextHoverPen = new(TextHoverStroke, 2.5);
    private static readonly ImmutablePen ImageHoverPen = new(ImageHoverStroke, 2.5);
    private static readonly ImmutablePen TableHoverPen = new(TableHoverStroke, 2.5);
    private static readonly ImmutablePen CaptionHoverPen = new(CaptionHoverStroke, 2.5);
    private static readonly ImmutablePen DefaultHoverPen = new(DefaultHoverStroke, 2.5);

    // Cached highlight fill brushes per type color (avoid per-frame allocation)
    private static readonly ImmutableSolidColorBrush TitleHighlightFill = new(Colors.Blue, 0.45);
    private static readonly ImmutableSolidColorBrush TextHighlightFill = new(Colors.Green, 0.45);
    private static readonly ImmutableSolidColorBrush ImageHighlightFill = new(Colors.Orange, 0.45);
    private static readonly ImmutableSolidColorBrush TableHighlightFill = new(Colors.Purple, 0.45);
    private static readonly ImmutableSolidColorBrush CaptionHighlightFill = new(Colors.Gray, 0.45);
    private static readonly ImmutableSolidColorBrush DefaultHighlightFill = new(Colors.LightGray, 0.45);

    // Hover fill colors (alpha = 0.45 — midway between normal fill and highlight)
    private static readonly ImmutableSolidColorBrush TitleHoverFill = new(Colors.Blue, 0.45);
    private static readonly ImmutableSolidColorBrush TextHoverFill = new(Colors.Green, 0.45);
    private static readonly ImmutableSolidColorBrush ImageHoverFill = new(Colors.Orange, 0.45);
    private static readonly ImmutableSolidColorBrush TableHoverFill = new(Colors.Purple, 0.45);
    private static readonly ImmutableSolidColorBrush CaptionHoverFill = new(Colors.Gray, 0.45);
    private static readonly ImmutableSolidColorBrush DefaultHoverFill = new(Colors.LightGray, 0.45);

    // Hover stroke colors (alpha = 1.0)
    private static readonly ImmutableSolidColorBrush TitleHoverStroke = new(Colors.Blue, 1.0);
    private static readonly ImmutableSolidColorBrush TextHoverStroke = new(Colors.Green, 1.0);
    private static readonly ImmutableSolidColorBrush ImageHoverStroke = new(Colors.Orange, 1.0);
    private static readonly ImmutableSolidColorBrush TableHoverStroke = new(Colors.Purple, 1.0);
    private static readonly ImmutableSolidColorBrush CaptionHoverStroke = new(Colors.Gray, 1.0);
    private static readonly ImmutableSolidColorBrush DefaultHoverStroke = new(Colors.LightGray, 1.0);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == BlocksProperty || change.Property == PageSizeProperty ||
            change.Property == PpiScaleProperty)
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
        var ppiScale = PpiScale;

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
            var pdfRect = BboxToAvaloniaRectangle(bbox, pageSize, block.IsBboxNormalized, ppiScale);
            _blockGeometries[i] = PdfWordHelpers.GetGeometry(pdfRect, false);
        }

        _geometriesDirty = false;
        return _blockGeometries;
    }

    /// <summary>
    /// Converts a MinerUBlock bbox to Avalonia coordinates (top-left origin, Y down).
    /// The Render() method uses Avalonia's coordinate system, not PDF's (bottom-left origin).
    /// For non-normalized coordinates (assumed to be in PDF point space), multiplies by PpiScale
    /// to match the display coordinate space.
    /// </summary>
    private static PdfRectangle BboxToAvaloniaRectangle(Rect bbox, Size pageSize, bool isNormalized, double ppiScale)
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
            // Non-normalized: coordinates are in PDF point space (or absolute pixels at 1x scale).
            // Multiply by PpiScale to convert to display pixel space.
            x0 = bbox.X * ppiScale;
            y0 = bbox.Y * ppiScale;
            x1 = bbox.Right * ppiScale;
            y1 = bbox.Bottom * ppiScale;
        }

        // Create PdfRectangle with Avalonia coordinates (top-left origin)
        // PdfRectangle constructor: (bottomLeftX, bottomLeftY, topRightX, topRightY)
        // In Avalonia: bottomLeft = (x0, y1), topRight = (x1, y0)
        return new PdfRectangle(x0, y1, x1, y0);
    }

    private static (ImmutableSolidColorBrush fill, ImmutablePen pen) GetBlockStyle(MinerUBlock block, bool isHighlighted, bool isHovered)
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
        else if (isHovered)
        {
            (fill, pen) = block.Type switch
            {
                "title" => (TitleHoverFill, TitleHoverPen),
                "text" => (TextHoverFill, TextHoverPen),
                "image" => (ImageHoverFill, ImageHoverPen),
                "table" => (TableHoverFill, TableHoverPen),
                "caption" => (CaptionHoverFill, CaptionHoverPen),
                _ => (DefaultHoverFill, DefaultHoverPen)
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
            bool isHovered = i == _hoveredBlockIndex;
            var (fill, pen) = GetBlockStyle(block, isHighlighted, isHovered);

            context.DrawGeometry(fill, pen, geometry);

            // Draw block label only when hovered (or highlighted in analysis panel)
            if (ShowLabels && (isHovered || isHighlighted) && !string.IsNullOrEmpty(block.Content))
            {
                var label = $"{block.Type}: {Truncate(block.Content, 60)}";
                // Font size in screen pixels (fixed), inversely scaled by zoom
                // so text stays readable regardless of zoom level.
                // E.g. 11 / 0.08 = 137.5 → rendered at 137.5×0.08 ≈ 11 screen px
                double zoom = Math.Max(ZoomLevel, 0.01);
                double fontSize = 11.0 / zoom;
                var formattedText = new FormattedText(
                    label,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    Typeface.Default,
                    fontSize,
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
