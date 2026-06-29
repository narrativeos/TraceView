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
    public static readonly StyledProperty<IReadOnlyList<PopoBlock>?> BlocksProperty =
        AvaloniaProperty.Register<BlockOverlayControl, IReadOnlyList<PopoBlock>?>(nameof(Blocks));

    public static readonly StyledProperty<Rect?> VisibleAreaProperty =
        AvaloniaProperty.Register<BlockOverlayControl, Rect?>(nameof(VisibleArea));

    public static readonly StyledProperty<int?> HighlightBlockIdProperty =
        AvaloniaProperty.Register<BlockOverlayControl, int?>(nameof(HighlightBlockId));

    public static readonly StyledProperty<bool> ShowLabelsProperty =
        AvaloniaProperty.Register<BlockOverlayControl, bool>(nameof(ShowLabels), true);

    static BlockOverlayControl()
    {
        AffectsRender<BlockOverlayControl>(BlocksProperty, VisibleAreaProperty,
            HighlightBlockIdProperty, ShowLabelsProperty);
    }

    public IReadOnlyList<PopoBlock>? Blocks
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

        if (change.Property == BlocksProperty)
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

        var geometries = new StreamGeometry[blocks.Count];
        for (int i = 0; i < blocks.Count; i++)
        {
            var bbox = blocks[i].Bbox;
            // Convert Avalonia Rect to PdfRectangle for PdfWordHelpers.GetGeometry
            var pdfRect = new PdfRectangle(bbox.X, bbox.Y, bbox.Right, bbox.Bottom);
            geometries[i] = PdfWordHelpers.GetGeometry(pdfRect, false);
        }

        _blockGeometries = geometries;
        _geometriesDirty = false;
        return geometries;
    }

    private (ImmutableSolidColorBrush fill, ImmutablePen pen) GetBlockStyle(PopoBlock block, bool isHighlighted)
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
