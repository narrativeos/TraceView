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

    /// <summary>
    /// Additional block IDs (UUID strings) to highlight (from MinerU Blocks column selection).
    /// When a user selects a block in the MinerU Blocks column, its RelatedBlockIds are set here
    /// to highlight the corresponding preproc_blocks on the PDF overlay.
    /// Uses IReadOnlySet<string> for O(1) Contains performance in the render loop.
    /// </summary>
    public static readonly StyledProperty<System.Collections.Generic.IReadOnlySet<string>?> RelatedHighlightBlockIdsProperty =
        AvaloniaProperty.Register<BlockOverlayControl, System.Collections.Generic.IReadOnlySet<string>?>(nameof(RelatedHighlightBlockIds));

    /// <summary>
    /// Destination type of the selected block ("para" = adopted/green, "discarded" = red).
    /// Used to determine the highlight color for related blocks.
    /// </summary>
    public static readonly StyledProperty<string?> RelatedHighlightDestinationTypeProperty =
        AvaloniaProperty.Register<BlockOverlayControl, string?>(nameof(RelatedHighlightDestinationType));

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
            HighlightBlockIdProperty, RelatedHighlightBlockIdsProperty, RelatedHighlightDestinationTypeProperty,
            ShowLabelsProperty, PageSizeProperty, PpiScaleProperty, ZoomLevelProperty);
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

    public System.Collections.Generic.IReadOnlySet<string>? RelatedHighlightBlockIds
    {
        get => GetValue(RelatedHighlightBlockIdsProperty);
        set => SetValue(RelatedHighlightBlockIdsProperty, value);
    }

    public string? RelatedHighlightDestinationType
    {
        get => GetValue(RelatedHighlightDestinationTypeProperty);
        set => SetValue(RelatedHighlightDestinationTypeProperty, value);
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

    /// <summary>
    /// Spatial grid for fast visibility culling. Each cell holds indices into the blocks array.
    /// Lazily built when Blocks/PageSize/PpiScale change, reused across renders.
    /// </summary>
    private GridCell[]? _gridCells;
    private int _gridColumns = 0;
    private int _gridRows = 0;

    /// <summary>
    /// Number of grid cells per dimension. Tuned for typical block counts per page.
    /// A 4x4 grid provides good granularity without excessive per-cell overhead.
    /// </summary>
    private const int GridResolution = 4;

    /// <summary>
    /// Cached visible area from the last render. Used to skip redraws when the visible area
    /// changes but covers the same set of grid cells (sub-cell scrolls don't change which blocks are drawn).
    /// </summary>
    private Rect? _lastRenderedVisibleArea;

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

        // First ensure geometries are built (they may not be if Render hasn't been called yet)
        EnsureGeometries();

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

    // Destination-based stroke colors (to show block fate)
    // Adopted (para): green solid border
    private static readonly ImmutableSolidColorBrush AdoptedStroke = new(Color.Parse(MinerUConstants.AdoptedColor), 1.0);
    // Discarded: red solid border
    private static readonly ImmutableSolidColorBrush DiscardedStroke = new(Color.Parse(MinerUConstants.DiscardedColor), 1.0);

    // Highlight stroke (yellow/amber)
    private static readonly ImmutableSolidColorBrush HighlightStroke = new(Color.Parse(MinerUConstants.HighlightColor), 1.0);

    private static readonly ImmutablePen DefaultPen = new(DefaultStroke, 2.5);
    private static readonly ImmutablePen HighlightPen = new(HighlightStroke, 4.0);

    // Destination-based pens
    private static readonly ImmutablePen AdoptedPen = new(AdoptedStroke, 3.0);
    private static readonly ImmutablePen DiscardedPen = new(DiscardedStroke, 2.5);

    // Cached related-highlight brushes (avoid per-frame allocation in GetRelatedHighlightStyle)
    private static readonly ImmutablePen AdoptedHighlightPen = new(
        new ImmutableSolidColorBrush(Color.Parse(MinerUConstants.AdoptedColor), 1.0), 4.0);
    private static readonly ImmutablePen DiscardedHighlightPen = new(
        new ImmutableSolidColorBrush(Color.Parse(MinerUConstants.DiscardedColor), 1.0), 4.0);
    private static readonly ImmutableSolidColorBrush AdoptedHighlightFill = new(
        Color.Parse(MinerUConstants.AdoptedColor), 0.50);
    private static readonly ImmutableSolidColorBrush DiscardedHighlightFill = new(
        Color.Parse(MinerUConstants.DiscardedColor), 0.50);
    private static readonly ImmutableSolidColorBrush DefaultRelatedHighlightFill = new(
        Color.Parse(MinerUConstants.HighlightColor), 0.50);

    // Cached pens per type (avoid per-frame allocation)
    private static readonly ImmutablePen TitlePen = new(TitleStroke, 2.5);
    private static readonly ImmutablePen TextPen = new(TextStroke, 2.5);
    private static readonly ImmutablePen ImagePen = new(ImageStroke, 2.5);
    private static readonly ImmutablePen TablePen = new(TableStroke, 2.5);
    private static readonly ImmutablePen CaptionPen = new(CaptionStroke, 2.5);

    // Hover stroke colors (alpha = 1.0, solid colors for maximum visibility)
    // MUST be defined before Hover pens that depend on them
    private static readonly ImmutableSolidColorBrush TitleHoverStroke = new(Colors.Blue, 1.0);
    private static readonly ImmutableSolidColorBrush TextHoverStroke = new(Colors.Green, 1.0);
    private static readonly ImmutableSolidColorBrush ImageHoverStroke = new(Colors.Orange, 1.0);
    private static readonly ImmutableSolidColorBrush TableHoverStroke = new(Colors.Purple, 1.0);
    private static readonly ImmutableSolidColorBrush CaptionHoverStroke = new(Colors.Gray, 1.0);
    // Default hover stroke uses black for maximum contrast
    private static readonly ImmutableSolidColorBrush DefaultHoverStroke = new(Colors.Black, 1.0);

    // Hover pens (thicker, solid)
    private static readonly ImmutablePen TitleHoverPen = new(TitleHoverStroke, 5.0);
    private static readonly ImmutablePen TextHoverPen = new(TextHoverStroke, 5.0);
    private static readonly ImmutablePen ImageHoverPen = new(ImageHoverStroke, 5.0);
    private static readonly ImmutablePen TableHoverPen = new(TableHoverStroke, 5.0);
    private static readonly ImmutablePen CaptionHoverPen = new(CaptionHoverStroke, 5.0);
    private static readonly ImmutablePen DefaultHoverPen = new(DefaultHoverStroke, 5.0);

    // Cached highlight fill brushes per type color (avoid per-frame allocation)
    private static readonly ImmutableSolidColorBrush TitleHighlightFill = new(Colors.Blue, 0.45);
    private static readonly ImmutableSolidColorBrush TextHighlightFill = new(Colors.Green, 0.45);
    private static readonly ImmutableSolidColorBrush ImageHighlightFill = new(Colors.Orange, 0.45);
    private static readonly ImmutableSolidColorBrush TableHighlightFill = new(Colors.Purple, 0.45);
    private static readonly ImmutableSolidColorBrush CaptionHighlightFill = new(Colors.Gray, 0.45);
    private static readonly ImmutableSolidColorBrush DefaultHighlightFill = new(Colors.LightGray, 0.45);

    // Hover fill colors (alpha = 0.50 — more opaque for better visibility)
    private static readonly ImmutableSolidColorBrush TitleHoverFill = new(Colors.Blue, 0.50);
    private static readonly ImmutableSolidColorBrush TextHoverFill = new(Colors.Green, 0.50);
    private static readonly ImmutableSolidColorBrush ImageHoverFill = new(Colors.Orange, 0.50);
    private static readonly ImmutableSolidColorBrush TableHoverFill = new(Colors.Purple, 0.50);
    private static readonly ImmutableSolidColorBrush CaptionHoverFill = new(Colors.Gray, 0.50);
    // Default hover uses a vibrant yellow for high contrast on any background
    private static readonly ImmutableSolidColorBrush DefaultHoverFill = new(Colors.Yellow, 0.50);

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

    private static (ImmutableSolidColorBrush fill, ImmutablePen pen) GetHighlightStyle(MinerUBlock block)
    {
        return block.Type switch
        {
            "title" => (TitleHighlightFill, HighlightPen),
            "text" => (TextHighlightFill, HighlightPen),
            "image" => (ImageHighlightFill, HighlightPen),
            "table" => (TableHighlightFill, HighlightPen),
            "caption" => (CaptionHighlightFill, HighlightPen),
            _ => (DefaultHighlightFill, HighlightPen)
        };
    }

    private static (ImmutableSolidColorBrush fill, ImmutablePen pen) GetHoverStyle(MinerUBlock block)
    {
        return block.Type switch
        {
            "title" => (TitleHoverFill, TitleHoverPen),
            "text" => (TextHoverFill, TextHoverPen),
            "image" => (ImageHoverFill, ImageHoverPen),
            "table" => (TableHoverFill, TableHoverPen),
            "caption" => (CaptionHoverFill, CaptionHoverPen),
            _ => (DefaultHoverFill, DefaultHoverPen)
        };
    }

    private static (ImmutableSolidColorBrush fill, ImmutablePen pen) GetRelatedHighlightStyle(MinerUBlock block, string? destinationType)
    {
        // Use the selected block's destination type to determine highlight color.
        // Uses pre-allocated static brushes to avoid per-frame allocation.
        return destinationType switch
        {
            MinerUConstants.DestPara => (AdoptedHighlightFill, AdoptedHighlightPen),
            MinerUConstants.DestDiscarded => (DiscardedHighlightFill, DiscardedHighlightPen),
            _ => (DefaultRelatedHighlightFill, HighlightPen)
        };
    }

    private static ImmutablePen GetDefaultPen(MinerUBlock block)
    {
        // First check destination-based pen (para/discarded), then fall back to type-based pen
        var destPen = block.DestinationType switch
        {
            MinerUConstants.DestPara => AdoptedPen,
            MinerUConstants.DestDiscarded => DiscardedPen,
            _ => null
        };

        if (destPen is not null)
            return destPen;

        return block.Type switch
        {
            "title" => TitlePen,
            "text" => TextPen,
            "image" => ImagePen,
            "table" => TablePen,
            "caption" => CaptionPen,
            _ => DefaultPen
        };
    }

    private static ImmutableSolidColorBrush GetDefaultFill(MinerUBlock block)
    {
        return block.Type switch
        {
            "title" => TitleFill,
            "text" => TextFill,
            "image" => ImageFill,
            "table" => TableFill,
            "caption" => CaptionFill,
            _ => DefaultFill
        };
    }

    private static (ImmutableSolidColorBrush fill, ImmutablePen pen) GetDefaultStyle(MinerUBlock block)
    {
        return (GetDefaultFill(block), GetDefaultPen(block));
    }

    public override void Render(DrawingContext context)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        var visibleArea = VisibleArea;
        if (!visibleArea.HasValue || visibleArea.Value.Width <= 0 || visibleArea.Value.Height <= 0)
        {
            _lastRenderedVisibleArea = null;
            return;
        }

        var blocks = Blocks;
        if (blocks is null || blocks.Count == 0)
        {
            _lastRenderedVisibleArea = visibleArea.Value;
            return;
        }

        var geometries = EnsureGeometries();
        BuildSpatialGrid();

        // Fill transparent to receive pointer events
        context.FillRectangle(Brushes.Transparent, Bounds);

        // Use spatial grid to find candidate blocks, then filter by exact bounds intersection.
        // This avoids iterating all blocks when only a fraction are visible.
        if (_gridCells is not null && _gridColumns > 0)
        {
            RenderWithGrid(context, blocks, geometries, visibleArea.Value);
        }
        else
        {
            // Fallback: linear scan (grid failed to build)
            RenderLinear(context, blocks, geometries, visibleArea.Value);
        }
    }

    /// <summary>
    /// Checks whether two visible areas cover the same set of grid cells.
    /// </summary>
    private bool SameGridCells(Rect a, Rect b)
    {
        if (_gridCells is null || _gridColumns <= 0 || _gridRows <= 0)
            return false;

        double cellWidth = Bounds.Width / _gridColumns;
        double cellHeight = Bounds.Height / _gridRows;

        int aStartCol = (int)(a.Left / cellWidth);
        int aStartRow = (int)(a.Top / cellHeight);
        int aEndCol = (int)(a.Right / cellWidth);
        int aEndRow = (int)(a.Bottom / cellHeight);

        int bStartCol = (int)(b.Left / cellWidth);
        int bStartRow = (int)(b.Top / cellHeight);
        int bEndCol = (int)(b.Right / cellWidth);
        int bEndRow = (int)(b.Bottom / cellHeight);

        return aStartCol == bStartCol && aStartRow == bStartRow && aEndCol == bEndCol && aEndRow == bEndRow;
    }

    /// <summary>
    /// Renders blocks using the spatial grid for fast culling.
    /// </summary>
    private void RenderWithGrid(DrawingContext context, IReadOnlyList<MinerUBlock> blocks,
        StreamGeometry[] geometries, Rect visibleArea)
    {
        double cellWidth = Bounds.Width / _gridColumns;
        double cellHeight = Bounds.Height / _gridRows;

        int startCol = (int)(visibleArea.Left / cellWidth);
        int startRow = (int)(visibleArea.Top / cellHeight);
        int endCol = (int)(visibleArea.Right / cellWidth);
        int endRow = (int)(visibleArea.Bottom / cellHeight);

        startCol = Math.Max(0, startCol);
        startRow = Math.Max(0, startRow);
        endCol = Math.Min(_gridColumns - 1, endCol);
        endRow = Math.Min(_gridRows - 1, endRow);

        // Track visited block indices to avoid drawing the same block twice
        // (a block can span multiple grid cells)
        bool[] visited = new bool[blocks.Count];

        for (int r = startRow; r <= endRow; r++)
        {
            for (int c = startCol; c <= endCol; c++)
            {
                var cell = _gridCells![r * _gridColumns + c];
                cell.FindInRect(visibleArea, i =>
                {
                    if (visited[i])
                        return;
                    visited[i] = true;

                    var geometry = geometries[i];
                    if (!geometry.Bounds.Intersects(visibleArea))
                        return;

                    DrawBlock(context, blocks[i], geometry, i);
                });
            }
        }
    }

    /// <summary>
    /// Renders blocks using a linear scan (fallback when grid is unavailable).
    /// </summary>
    private void RenderLinear(DrawingContext context, IReadOnlyList<MinerUBlock> blocks,
        StreamGeometry[] geometries, Rect visibleArea)
    {
        for (int i = 0; i < blocks.Count; i++)
        {
            var geometry = geometries[i];
            if (!geometry.Bounds.Intersects(visibleArea))
                continue;

            DrawBlock(context, blocks[i], geometry, i);
        }
    }

    /// <summary>
    /// Draws a single block with the appropriate style (highlight, hover, default).
    /// </summary>
    private void DrawBlock(DrawingContext context, MinerUBlock block, StreamGeometry geometry, int index)
    {
        bool isHighlighted = HighlightBlockId.HasValue && block.Id == HighlightBlockId.Value;
        bool isRelatedHighlighted = RelatedHighlightBlockIds != null && RelatedHighlightBlockIds.Contains(block.BlockId);
        bool isHovered = index == _hoveredBlockIndex;

        ImmutableSolidColorBrush fill = DefaultFill;
        ImmutablePen pen = DefaultPen;

        if (isHovered)
        {
            (fill, pen) = GetHoverStyle(block);
        }
        else if (isRelatedHighlighted)
        {
            (fill, pen) = GetRelatedHighlightStyle(block, RelatedHighlightDestinationType);
        }
        else if (isHighlighted)
        {
            (fill, pen) = GetHighlightStyle(block);
        }
        else
        {
            (fill, pen) = GetDefaultStyle(block);
        }

        context.DrawGeometry(fill, pen, geometry);

        if (ShowLabels && (isHovered || isHighlighted) && !string.IsNullOrEmpty(block.Content))
        {
            var label = $"{block.Type}: {Truncate(block.Content, 60)}";
            double zoom = Math.Max(ZoomLevel, 0.01);
            double fontSize = 11.0 / zoom;
            var formattedText = new FormattedText(
                label,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                fontSize,
                Brushes.Black);

            var labelPos = geometry.Bounds.TopLeft;
            context.DrawText(formattedText, labelPos);
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

    /// <summary>
    /// A single cell in the spatial grid. Holds block indices using a small inline buffer
    /// that promotes to a dynamic array when the capacity is exceeded.
    /// </summary>
    private sealed class GridCell
    {
        private readonly int[] _smallBuffer = new int[8];
        private int _count;
        private int[]? _largeBuffer;

        public void Add(int index)
        {
            if (_count < _smallBuffer.Length)
            {
                _smallBuffer[_count++] = index;
            }
            else
            {
                if (_largeBuffer is null)
                {
                    // Promote: copy small buffer contents and add the new index
                    _largeBuffer = new int[_count + 1];
                    for (int i = 0; i < _count; i++)
                        _largeBuffer[i] = _smallBuffer[i];
                }
                else if (_count == _largeBuffer.Length)
                {
                    Array.Resize(ref _largeBuffer, _largeBuffer.Length * 2);
                }
                _largeBuffer[_count++] = index;
            }
        }

        public void FindInRect(Rect rect, Action<int> callback)
        {
            if (_largeBuffer is null)
            {
                for (int i = 0; i < _count; i++)
                    callback(_smallBuffer[i]);
            }
            else
            {
                for (int i = 0; i < _count; i++)
                    callback(_largeBuffer[i]);
            }
        }

        public int Count => _count;

        public void Clear()
        {
            _count = 0;
            _largeBuffer = null;
        }
    }

    /// <summary>
    /// Rebuilds the spatial grid. Called when Blocks/PageSize/PpiScale change.
    /// Each block is placed into every grid cell whose area it overlaps.
    /// </summary>
    private void BuildSpatialGrid()
    {
        var geometries = _blockGeometries;
        if (geometries is null || geometries.Length == 0)
        {
            _gridCells = null;
            _gridColumns = 0;
            _gridRows = 0;
            return;
        }

        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            _gridCells = null;
            _gridColumns = 0;
            _gridRows = 0;
            return;
        }

        int cols = GridResolution;
        int rows = GridResolution;
        double cellWidth = bounds.Width / cols;
        double cellHeight = bounds.Height / rows;

        // Reuse existing cells if the grid size matches
        if (_gridCells is null || _gridCells.Length != cols * rows)
        {
            _gridCells = new GridCell[cols * rows];
            for (int i = 0; i < _gridCells.Length; i++)
                _gridCells[i] = new GridCell();
        }
        else
        {
            foreach (var cell in _gridCells)
                cell.Clear();
        }

        _gridColumns = cols;
        _gridRows = rows;

        for (int i = 0; i < geometries.Length; i++)
        {
            var geomBounds = geometries[i].Bounds;

            // Determine which cells this block overlaps
            int startCol = (int)(geomBounds.Left / cellWidth);
            int startRow = (int)(geomBounds.Top / cellHeight);
            int endCol = (int)(geomBounds.Right / cellWidth);
            int endRow = (int)(geomBounds.Bottom / cellHeight);

            // Clamp to grid bounds
            startCol = Math.Max(0, startCol);
            startRow = Math.Max(0, startRow);
            endCol = Math.Min(cols - 1, endCol);
            endRow = Math.Min(rows - 1, endRow);

            for (int r = startRow; r <= endRow; r++)
            {
                for (int c = startCol; c <= endCol; c++)
                {
                    _gridCells[r * cols + c].Add(i);
                }
            }
        }
    }

}
