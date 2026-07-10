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
using Caly.Core.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Caly.Core.Controls;

/// <summary>
/// Custom control that draws connecting lines between preproc_blocks (PDF overlay)
/// and para/discarded blocks (MinerU Blocks column).
/// Each preproc_block is connected to its matched para/discarded block via RelatedBlockIds.
/// </summary>
public sealed class ConnectionLinesControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<MinerUBlock>?> PreprocBlocksProperty =
        AvaloniaProperty.Register<ConnectionLinesControl, IReadOnlyList<MinerUBlock>?>(nameof(PreprocBlocks));

    /// <summary>
    /// Preproc blocks grouped by page number with per-page sizes. When set, Render() iterates
    /// over each page and draws connection lines with proper Y offset for each page.
    /// </summary>
    public static readonly StyledProperty<IDictionary<int, (IReadOnlyList<MinerUBlock> Blocks, Size PageSize)>?> PreprocBlocksByPageProperty =
        AvaloniaProperty.Register<ConnectionLinesControl, IDictionary<int, (IReadOnlyList<MinerUBlock> Blocks, Size PageSize)>?>(nameof(PreprocBlocksByPage));

    public static readonly StyledProperty<IReadOnlyList<MinerUBlockViewModel>?> MinerUBlocksProperty =
        AvaloniaProperty.Register<ConnectionLinesControl, IReadOnlyList<MinerUBlockViewModel>?>(nameof(MinerUBlocks));

    public static readonly StyledProperty<Size> PageSizeProperty =
        AvaloniaProperty.Register<ConnectionLinesControl, Size>(nameof(PageSize));

    public static readonly StyledProperty<double> ZoomLevelProperty =
        AvaloniaProperty.Register<ConnectionLinesControl, double>(nameof(ZoomLevel), 1.0);

    public static readonly StyledProperty<double> PdfScrollOffsetYProperty =
        AvaloniaProperty.Register<ConnectionLinesControl, double>(nameof(PdfScrollOffsetY));

    public static readonly StyledProperty<double> MinerUScrollOffsetYProperty =
        AvaloniaProperty.Register<ConnectionLinesControl, double>(nameof(MinerUScrollOffsetY));

    public static readonly StyledProperty<double> PdfColumnRightEdgeProperty =
        AvaloniaProperty.Register<ConnectionLinesControl, double>(nameof(PdfColumnRightEdge));

    public static readonly StyledProperty<double> MinerUColumnLeftEdgeProperty =
        AvaloniaProperty.Register<ConnectionLinesControl, double>(nameof(MinerUColumnLeftEdge));

    public static readonly StyledProperty<double> PdfPageTopOffsetProperty =
        AvaloniaProperty.Register<ConnectionLinesControl, double>(nameof(PdfPageTopOffset));

    public static readonly StyledProperty<double> MinerUListTopOffsetProperty =
        AvaloniaProperty.Register<ConnectionLinesControl, double>(nameof(MinerUListTopOffset));

    public static readonly StyledProperty<double> MinerUBlockItemHeightProperty =
        AvaloniaProperty.Register<ConnectionLinesControl, double>(nameof(MinerUBlockItemHeight), 60.0);

    public static readonly StyledProperty<string?> SelectedBlockIdProperty =
        AvaloniaProperty.Register<ConnectionLinesControl, string?>(nameof(SelectedBlockId));

    public static readonly StyledProperty<bool> ShowConnectionsProperty =
        AvaloniaProperty.Register<ConnectionLinesControl, bool>(nameof(ShowConnections), false);

    static ConnectionLinesControl()
    {
        AffectsRender<ConnectionLinesControl>(
            PreprocBlocksProperty,
            PreprocBlocksByPageProperty,
            MinerUBlocksProperty,
            PageSizeProperty,
            ZoomLevelProperty,
            PdfScrollOffsetYProperty,
            MinerUScrollOffsetYProperty,
            PdfColumnRightEdgeProperty,
            MinerUColumnLeftEdgeProperty,
            PdfPageTopOffsetProperty,
            MinerUListTopOffsetProperty,
            MinerUBlockItemHeightProperty,
            SelectedBlockIdProperty,
            ShowConnectionsProperty);
    }

    public IReadOnlyList<MinerUBlock>? PreprocBlocks
    {
        get => GetValue(PreprocBlocksProperty);
        set => SetValue(PreprocBlocksProperty, value);
    }

    public IDictionary<int, (IReadOnlyList<MinerUBlock> Blocks, Size PageSize)>? PreprocBlocksByPage
    {
        get => GetValue(PreprocBlocksByPageProperty);
        set => SetValue(PreprocBlocksByPageProperty, value);
    }

    public IReadOnlyList<MinerUBlockViewModel>? MinerUBlocks
    {
        get => GetValue(MinerUBlocksProperty);
        set => SetValue(MinerUBlocksProperty, value);
    }

    public Size PageSize
    {
        get => GetValue(PageSizeProperty);
        set => SetValue(PageSizeProperty, value);
    }

    public double ZoomLevel
    {
        get => GetValue(ZoomLevelProperty);
        set => SetValue(ZoomLevelProperty, value);
    }

    public double PdfScrollOffsetY
    {
        get => GetValue(PdfScrollOffsetYProperty);
        set => SetValue(PdfScrollOffsetYProperty, value);
    }

    public double MinerUScrollOffsetY
    {
        get => GetValue(MinerUScrollOffsetYProperty);
        set => SetValue(MinerUScrollOffsetYProperty, value);
    }

    public double PdfColumnRightEdge
    {
        get => GetValue(PdfColumnRightEdgeProperty);
        set => SetValue(PdfColumnRightEdgeProperty, value);
    }

    public double MinerUColumnLeftEdge
    {
        get => GetValue(MinerUColumnLeftEdgeProperty);
        set => SetValue(MinerUColumnLeftEdgeProperty, value);
    }

    public double PdfPageTopOffset
    {
        get => GetValue(PdfPageTopOffsetProperty);
        set => SetValue(PdfPageTopOffsetProperty, value);
    }

    public double MinerUListTopOffset
    {
        get => GetValue(MinerUListTopOffsetProperty);
        set => SetValue(MinerUListTopOffsetProperty, value);
    }

    public double MinerUBlockItemHeight
    {
        get => GetValue(MinerUBlockItemHeightProperty);
        set => SetValue(MinerUBlockItemHeightProperty, value);
    }

    public string? SelectedBlockId
    {
        get => GetValue(SelectedBlockIdProperty);
        set => SetValue(SelectedBlockIdProperty, value);
    }

    public bool ShowConnections
    {
        get => GetValue(ShowConnectionsProperty);
        set => SetValue(ShowConnectionsProperty, value);
    }

    // High-visibility brushes (precise matches)
    private static readonly ImmutableSolidColorBrush AdoptedLineBrush =
        new(Color.Parse(MinerUConstants.AdoptedColor), 0.8);
    private static readonly ImmutableSolidColorBrush DiscardedLineBrush =
        new(Color.Parse(MinerUConstants.DiscardedColor), 0.8);
    private static readonly ImmutableSolidColorBrush DefaultLineBrush =
        new(Color.Parse(MinerUConstants.DefaultColor), 0.6);

    // Faded brushes (fallback matches - lighter/transparent)
    private static readonly ImmutableSolidColorBrush FadedAdoptedLineBrush =
        new(Color.Parse(MinerUConstants.AdoptedColor), 0.25);
    private static readonly ImmutableSolidColorBrush FadedDiscardedLineBrush =
        new(Color.Parse(MinerUConstants.DiscardedColor), 0.25);
    private static readonly ImmutableSolidColorBrush FadedDefaultLineBrush =
        new(Color.Parse(MinerUConstants.DefaultColor), 0.2);

    private static readonly ImmutablePen AdoptedPen = new(AdoptedLineBrush, 2.0);
    private static readonly ImmutablePen DiscardedPen = new(DiscardedLineBrush, 2.0);
    private static readonly ImmutablePen DefaultPen = new(DefaultLineBrush, 1.5);

    private static readonly ImmutablePen FadedAdoptedPen = new(FadedAdoptedLineBrush, 1.0);
    private static readonly ImmutablePen FadedDiscardedPen = new(FadedDiscardedLineBrush, 1.0);
    private static readonly ImmutablePen FadedDefaultPen = new(FadedDefaultLineBrush, 1.0);

    public override void Render(DrawingContext context)
    {
        if (!ShowConnections)
            return;
            
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        var minerUBlocks = MinerUBlocks;
        if (minerUBlocks is null || minerUBlocks.Count == 0)
        {
            System.Diagnostics.Debug.WriteLine($"[ConnectionLines.Render] Early exit: mineru={minerUBlocks?.Count ?? -1}");
            return;
        }

        // Build BlockId (UUID string) -> index map for MinerU blocks
        var minerUIdToIndex = new Dictionary<string, int>();
        for (int i = 0; i < minerUBlocks.Count; i++)
        {
            if (!string.IsNullOrEmpty(minerUBlocks[i].BlockId) && !minerUIdToIndex.ContainsKey(minerUBlocks[i].BlockId))
                minerUIdToIndex[minerUBlocks[i].BlockId] = i;
        }

        // If PreprocBlocksByPage is set, render per page with proper Y offset
        if (PreprocBlocksByPage is not null && PreprocBlocksByPage.Count > 0)
        {
            RenderMultiPageConnections(context, PreprocBlocksByPage, minerUIdToIndex);
        }
        else
        {
            // Legacy single page mode
            var preprocBlocks = PreprocBlocks;
            if (preprocBlocks is null || preprocBlocks.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine($"[ConnectionLines.Render] Early exit: preproc={preprocBlocks?.Count ?? -1}");
                return;
            }
            RenderSinglePageConnections(context, preprocBlocks, minerUIdToIndex, 0);
        }
    }

    /// <summary>
    /// Renders connection lines for multiple pages. Each page's preproc_blocks are rendered
    /// with a Y offset calculated cumulatively from previous pages' actual heights.
    /// </summary>
    private void RenderMultiPageConnections(
        DrawingContext context,
        IDictionary<int, (IReadOnlyList<MinerUBlock> Blocks, Size PageSize)> preprocBlocksByPage,
        Dictionary<string, int> minerUIdToIndex)
    {
        System.Diagnostics.Debug.WriteLine($"[ConnectionLines.Render] Multi-page: {preprocBlocksByPage.Count} pages");
        
        int totalConnections = 0;
        double cumulativeYOffset = 0;

        // Process each page in order
        foreach (var pageNumber in preprocBlocksByPage.Keys.OrderBy(p => p))
        {
            var (preprocBlocks, pageSize) = preprocBlocksByPage[pageNumber];
            if (preprocBlocks.Count == 0)
                continue;

            System.Diagnostics.Debug.WriteLine($"[ConnectionLines] Page {pageNumber}: PreprocBlocks={preprocBlocks.Count}, PageSize={pageSize}, YOffset={cumulativeYOffset}");

            var count = RenderSinglePageConnections(context, preprocBlocks, minerUIdToIndex, cumulativeYOffset, pageSize);
            totalConnections += count;

            // Accumulate Y offset for next page
            cumulativeYOffset += pageSize.Height * ZoomLevel;
        }

        System.Diagnostics.Debug.WriteLine($"[ConnectionLines.Render] Multi-page total: {totalConnections} connections");
    }

    /// <summary>
    /// Renders connection lines for a single page's preproc_blocks.
    /// Returns the number of connections drawn.
    /// </summary>
    private int RenderSinglePageConnections(
        DrawingContext context,
        IReadOnlyList<MinerUBlock> preprocBlocks,
        Dictionary<string, int> minerUIdToIndex,
        double pageYOffset,
        Size? pageSize = null)
    {
        var minerUBlocks = MinerUBlocks;
        if (minerUBlocks is null || minerUBlocks.Count == 0)
            return 0;

        // Filter to preproc_blocks that have a DestinationType (matched)
        var matchedPreproc = preprocBlocks.Where(b => !string.IsNullOrEmpty(b.DestinationType)).ToList();

        if (matchedPreproc.Count == 0)
            return 0;

        // Determine which connections to draw
        List<(MinerUBlock preproc, MinerUBlockViewModel target)> connections;

        if (!string.IsNullOrEmpty(SelectedBlockId))
        {
            var selId = SelectedBlockId;
            connections = new List<(MinerUBlock, MinerUBlockViewModel)>();

            if (minerUIdToIndex.TryGetValue(selId, out var minerUIdx))
            {
                foreach (var preproc in matchedPreproc)
                {
                    if (preproc.RelatedBlockIds.Contains(selId))
                    {
                        connections.Add((preproc, minerUBlocks[minerUIdx]));
                    }
                }
            }

            if (connections.Count == 0)
                return 0;
        }
        else
        {
            connections = new List<(MinerUBlock, MinerUBlockViewModel)>();
            foreach (var preproc in matchedPreproc)
            {
                if (preproc.RelatedBlockIds.Count > 0)
                {
                    var targetId = preproc.RelatedBlockIds[0];
                    if (minerUIdToIndex.TryGetValue(targetId, out var minerUIdx))
                    {
                        connections.Add((preproc, minerUBlocks[minerUIdx]));
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"  [WARN] targetId={targetId} not found in minerUIdToIndex! Available keys: {string.Join(",", minerUIdToIndex.Keys.Take(10))}...");
                    }
                }
            }

            if (connections.Count == 0)
                return 0;
        }

        // Debug: log first few connections with block IDs and pages
        foreach (var (p, t) in connections.Take(3))
        {
            System.Diagnostics.Debug.WriteLine($"  connection: preproc blockId={p.BlockId}(page={p.Page}) -> target blockId={t.BlockId}(page={t.Page}, idx={minerUIdToIndex.GetValueOrDefault(t.BlockId, -1)})");
        }

        // Draw connections with page Y offset
        DrawConnectionsWithOffset(context, connections, minerUIdToIndex, pageYOffset, pageSize);
        return connections.Count;
    }

    /// <summary>
    /// Draws connection lines with an additional Y offset for the page position.
    /// </summary>
    private void DrawConnectionsWithOffset(
        DrawingContext context,
        List<(MinerUBlock preproc, MinerUBlockViewModel target)> connections,
        Dictionary<string, int> minerUIdToIndex,
        double pageYOffset,
        Size? pageSize = null)
    {
        foreach (var (preproc, target) in connections)
        {
            var pdfBlockStart = CalculatePdfBlockStartPointWithOffset(preproc, pageYOffset, pageSize);
            var minerUIdx = minerUIdToIndex.TryGetValue(target.BlockId, out var idx) ? idx : -1;
            var minerUBlockEndPoint = CalculateMinerUBlockEndPoint(minerUIdx);

            if (IsOutsideVisibleArea(pdfBlockStart.Y, minerUBlockEndPoint.Y))
                continue;

            var startPoint = pdfBlockStart;
            var endPoint = minerUBlockEndPoint;

            var horizontalGap = endPoint.X - startPoint.X;
            if (horizontalGap <= 0)
                continue;

            var cpOffset = horizontalGap * 0.4;
            var controlPoint1 = new Point(startPoint.X + cpOffset, startPoint.Y);
            var controlPoint2 = new Point(endPoint.X - cpOffset, endPoint.Y);

            var isFallback = preproc.IsFallbackMatch || target.IsFallbackMatch;

            var pen = (preproc.DestinationType, isFallback) switch
            {
                (MinerUConstants.DestPara, false) => AdoptedPen,
                (MinerUConstants.DestPara, true) => FadedAdoptedPen,
                (MinerUConstants.DestDiscarded, false) => DiscardedPen,
                (MinerUConstants.DestDiscarded, true) => FadedDiscardedPen,
                (_, false) => DefaultPen,
                (_, true) => FadedDefaultPen
            };

            var sg = new StreamGeometry();
            using (var ctx = sg.Open())
            {
                ctx.BeginFigure(startPoint, false);
                ctx.CubicBezierTo(controlPoint1, controlPoint2, endPoint);
                ctx.EndFigure(false);
            }

            context.DrawGeometry(null, pen, sg);
        }
    }

    /// <summary>
    /// Like CalculatePdfBlockStartPoint but adds a page Y offset for multi-page rendering.
    /// pageYOffset is in pixels (already includes ZoomLevel), so we don't multiply it again.
    /// If pageSize is provided, uses it instead of the global PageSize.
    /// </summary>
    private Point CalculatePdfBlockStartPointWithOffset(MinerUBlock block, double pageYOffset, Size? pageSize = null)
    {
        var page = pageSize ?? PageSize;
        var bbox = block.Bbox;
        double blockX, blockY;

        if (block.IsBboxNormalized && page.Width > 0 && page.Height > 0)
        {
            blockX = (bbox.X + bbox.Width) * page.Width;
            blockY = (bbox.Y + bbox.Height / 2.0) * page.Height;
        }
        else
        {
            blockX = bbox.Right;
            blockY = bbox.Y + bbox.Height / 2.0;
        }

        var screenX = blockX * ZoomLevel;
        // Y: blockY * ZoomLevel + pageYOffset (already in pixels) - scroll offset
        var screenY = PdfPageTopOffset + blockY * ZoomLevel + pageYOffset - PdfScrollOffsetY;
        return new Point(screenX, screenY);
    }

    private void DrawConnections(
        DrawingContext context,
        List<(MinerUBlock preproc, MinerUBlockViewModel target)> connections,
        Dictionary<string, int> minerUIdToIndex)
    {
        var minerULeft = MinerUColumnLeftEdge;

        foreach (var (preproc, target) in connections)
        {
            var pdfBlockStart = CalculatePdfBlockStartPoint(preproc);
            var minerUIdx = minerUIdToIndex.TryGetValue(target.BlockId, out var idx) ? idx : -1;
            var minerUBlockEndPoint = CalculateMinerUBlockEndPoint(minerUIdx);

            if (IsOutsideVisibleArea(pdfBlockStart.Y, minerUBlockEndPoint.Y))
                continue;

            var startPoint = pdfBlockStart;
            var endPoint = minerUBlockEndPoint;

            // Use bezier control points for smooth S-curve
            // Control points are horizontally offset from start/end for smooth transitions
            var horizontalGap = endPoint.X - startPoint.X;
            if (horizontalGap <= 0)
                continue;

            var cpOffset = horizontalGap * 0.4;
            var controlPoint1 = new Point(startPoint.X + cpOffset, startPoint.Y);
            var controlPoint2 = new Point(endPoint.X - cpOffset, endPoint.Y);

            // Determine if this is a fallback match (either preproc or target has IsFallbackMatch)
            var isFallback = preproc.IsFallbackMatch || target.IsFallbackMatch;

            // Use DestinationType to determine color, and IsFallbackMatch to determine opacity
            var pen = (preproc.DestinationType, isFallback) switch
            {
                (MinerUConstants.DestPara, false) => AdoptedPen,
                (MinerUConstants.DestPara, true) => FadedAdoptedPen,
                (MinerUConstants.DestDiscarded, false) => DiscardedPen,
                (MinerUConstants.DestDiscarded, true) => FadedDiscardedPen,
                (_, false) => DefaultPen,
                (_, true) => FadedDefaultPen
            };

            var sg = new StreamGeometry();
            using (var ctx = sg.Open())
            {
                ctx.BeginFigure(startPoint, false);
                // Use native CubicBezierTo for smooth rendering
                ctx.CubicBezierTo(controlPoint1, controlPoint2, endPoint);
                ctx.EndFigure(false);
            }

            context.DrawGeometry(null, pen, sg);
        }
    }

    /// <summary>
    /// Calculates the screen position of the RIGHT EDGE of a preproc_block on the PDF page.
    /// Returns the center-right point of the block's bounding box in screen coordinates.
    /// </summary>
    private Point CalculatePdfBlockStartPoint(MinerUBlock block)
    {
        var bbox = block.Bbox;
        double blockX, blockY;

        if (block.IsBboxNormalized && PageSize.Width > 0 && PageSize.Height > 0)
        {
            // Use the RIGHT EDGE of the block (x + width), Y is the center
            blockX = (bbox.X + bbox.Width) * PageSize.Width;
            blockY = (bbox.Y + bbox.Height / 2.0) * PageSize.Height;
        }
        else
        {
            // Use the RIGHT EDGE of the block, Y is the center
            blockX = bbox.Right;
            blockY = bbox.Y + bbox.Height / 2.0;
        }

        // X: apply zoom level (PDF page starts at left edge of the PDF column)
        var screenX = blockX * ZoomLevel;
        // Y: apply top offset, zoom, and subtract scroll offset
        var screenY = PdfPageTopOffset + blockY * ZoomLevel - PdfScrollOffsetY;
        return new Point(screenX, screenY);
    }

    /// <summary>
    /// Calculates the screen position of the LEFT EDGE of a MinerU block item in the list.
    /// Returns the center-left point of the block item in screen coordinates.
    /// Uses actual rendered heights from view models for accurate positioning.
    /// </summary>
    private Point CalculateMinerUBlockEndPoint(int blockIndex)
    {
        if (blockIndex < 0)
            return new Point(MinerUColumnLeftEdge, MinerUListTopOffset);

        // Calculate cumulative Y position using actual rendered heights
        double itemTop = 0;
        for (int i = 0; i < blockIndex && i < MinerUBlocks!.Count; i++)
        {
            // Use actual rendered height if available, otherwise use default
            var h = MinerUBlocks[i].ActualHeight;
            if (h <= 0)
                h = MinerUBlockItemHeight;
            itemTop += h;
            // Add margin between items (Margin="0,2" in XAML = 4px total top+bottom)
            itemTop += 4.0;
        }

        var currentBlock = blockIndex < MinerUBlocks!.Count ? MinerUBlocks[blockIndex] : null;
        var itemHeight = currentBlock?.ActualHeight ?? MinerUBlockItemHeight;
        if (itemHeight <= 0)
            itemHeight = MinerUBlockItemHeight;
        var itemCenterY = itemTop + itemHeight / 2.0;

        // The left edge of the block item is the column's left edge plus padding
        // Border Padding=8 + item Border Padding=6 = 14px offset from column edge
        var itemLeftX = MinerUColumnLeftEdge + 14.0;
        var screenY = MinerUListTopOffset + itemCenterY - MinerUScrollOffsetY;

        return new Point(itemLeftX, screenY);
    }

    private double CalculatePdfBlockY(MinerUBlock block)
    {
        var bbox = block.Bbox;
        double blockY;

        if (block.IsBboxNormalized && PageSize.Width > 0 && PageSize.Height > 0)
        {
            blockY = (bbox.Y + bbox.Height / 2.0) * PageSize.Height;
        }
        else
        {
            blockY = bbox.Y + bbox.Height / 2.0;
        }

        var screenY = PdfPageTopOffset + blockY * ZoomLevel - PdfScrollOffsetY;
        return screenY;
    }

    private double CalculateMinerUBlockY(int blockIndex)
    {
        if (blockIndex < 0)
            return MinerUListTopOffset;

        var itemTop = blockIndex * MinerUBlockItemHeight;
        var itemCenter = itemTop + MinerUBlockItemHeight / 2.0;

        var screenY = MinerUListTopOffset + itemCenter - MinerUScrollOffsetY;
        return screenY;
    }

    private bool IsOutsideVisibleArea(double pdfY, double minerUY)
    {
        var boundsTop = -20;
        var boundsBottom = Bounds.Height + 20;

        var bothAbove = pdfY < boundsTop && minerUY < boundsTop;
        var bothBelow = pdfY > boundsBottom && minerUY > boundsBottom;

        return bothAbove || bothBelow;
    }
}