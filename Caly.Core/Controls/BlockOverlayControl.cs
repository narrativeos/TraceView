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
using System.Collections.Generic;
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
        var geometries = new StreamGeometry[blocks.Count];
        for (int i = 0; i < blocks.Count; i++)
        {
            var bbox = blocks[i].Bbox;
            // Convert block bbox (may be normalized 0-1 or absolute pixels) to PdfRectangle
            var pdfRect = BboxToPdfRectangle(bbox, pageSize);
            geometries[i] = PdfWordHelpers.GetGeometry(pdfRect, false);
        }

        _blockGeometries = geometries;
        _geometriesDirty = false;
        return geometries;
    }

    /// <summary>
    /// Converts a MinerUBlock bbox to PdfRectangle coordinates.
    /// Handles both normalized (0-1) and absolute pixel coordinates,
    /// and performs Y-axis flip from Avalonia (top-left origin) to PDF (bottom-left origin).
    /// </summary>
    private static PdfRectangle BboxToPdfRectangle(Rect bbox, Size pageSize)
    {
        // Determine if coordinates are normalized (0-1 range)
        // MinerUBlock.Bbox from MinerUJsonService is normalized when page_size is available in middle.json
        bool isNormalized = bbox.X <= 1.0 && bbox.Y <= 1.0 && bbox.Width <= 1.0 && bbox.Height <= 1.0;

        double x0, y0, x1, y1;

        if (isNormalized && pageSize.Width > 0 && pageSize.Height > 0)
        {
            // Convert normalized coordinates to pixel coordinates
            double pixelX0 = bbox.X * pageSize.Width;
            double pixelY0 = bbox.Y * pageSize.Height; // Avalonia Y (down)
            double pixelX1 = (bbox.X + bbox.Width) * pageSize.Width;
            double pixelY1 = (bbox.Y + bbox.Height) * pageSize.Height; // Avalonia Y (down)

            // Flip Y-axis: Avalonia (top-left origin, Y down) -> PDF (bottom-left origin, Y up)
            x0 = pixelX0;
            y0 = pageSize.Height - pixelY1; // Bottom in Avalonia becomes bottom in PDF (smaller Y)
            x1 = pixelX1;
            y1 = pageSize.Height - pixelY0; // Top in Avalonia becomes top in PDF (larger Y)
        }
        else
        {
            // Already in pixel coordinates, just flip Y-axis
            x0 = bbox.X;
            y0 = pageSize.Height - bbox.Bottom;
            x1 = bbox.Right;
            y1 = pageSize.Height - bbox.Y;
        }

        return new PdfRectangle(x0, y0, x1, y1);
    }

    private (ImmutableSolidColorBrush fill, ImmutablePen pen) GetBlockStyle(MinerUBlock block, bool isHighlighted)
    {
        if (isHighlighted)
        {
            return (new ImmutableSolidColorBrush(block.TypeColor, 0.35), HighlightPen);
        }

        return block.Type switch
        {
            "title" => (TitleFill, new ImmutablePen(TitleStroke, 1.5)),
            "text" => (TextFill, new ImmutablePen(TextStroke, 1.5)),
            "image" => (ImageFill, new ImmutablePen(ImageStroke, 1.5)),
            "table" => (TableFill, new ImmutablePen(TableStroke, 1.5)),
            "caption" => (CaptionFill, new ImmutablePen(CaptionStroke, 1.5)),
            _ => (DefaultFill, DefaultPen)
        };
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
        }
    }
}
