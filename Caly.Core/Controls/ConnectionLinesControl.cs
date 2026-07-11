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
using System.ComponentModel;
using System.Threading;

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

    public static readonly StyledProperty<double> PdfPageLeftOffsetProperty =
        AvaloniaProperty.Register<ConnectionLinesControl, double>(nameof(PdfPageLeftOffset));

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
        // NOTE: PdfScrollOffsetYProperty and MinerUScrollOffsetYProperty are NOT in AffectsRender.
        // We handle them separately via property Changed events with throttling to avoid
        // rendering on every single scroll tick, which causes severe stuttering.
        AffectsRender<ConnectionLinesControl>(
            PreprocBlocksProperty,
            PreprocBlocksByPageProperty,
            MinerUBlocksProperty,
            PageSizeProperty,
            ZoomLevelProperty,
            PdfColumnRightEdgeProperty,
            MinerUColumnLeftEdgeProperty,
            PdfPageTopOffsetProperty,
            MinerUListTopOffsetProperty,
            MinerUBlockItemHeightProperty,
            SelectedBlockIdProperty,
            ShowConnectionsProperty);

        // Subscribe to scroll offset property changes for throttled rendering
        PdfScrollOffsetYProperty.Changed.AddClassHandler<ConnectionLinesControl>((control, e) =>
        {
            control.ScheduleRender();
        });
        MinerUScrollOffsetYProperty.Changed.AddClassHandler<ConnectionLinesControl>((control, e) =>
        {
            control.ScheduleRender();
        });
    }

    private int _renderToken;
    private bool _renderPending;
    private System.Threading.Timer? _pendingTimer;

    private void ScheduleRender()
    {
        // Avoid redundant scheduling: if a render is already pending, skip
        if (_renderPending)
            return;
        
        _renderPending = true;
        int token = ++_renderToken;
        
        // Dispose previous timer if any
        _pendingTimer?.Dispose();
        
        // Use a simple timer with debounce: wait 50ms (~20fps) after the last scroll event
        // Longer debounce reduces rendering during fast scrolling, significantly improving performance
        // The int overload: dueTime (ms), period (ms)
        _pendingTimer = new System.Threading.Timer(_ =>
        {
            _renderPending = false;
            _pendingTimer = null;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (_renderToken == token)
                    InvalidateVisual();
            });
        }, null, 50, -1);
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

    public double PdfPageLeftOffset
    {
        get => GetValue(PdfPageLeftOffsetProperty);
        set => SetValue(PdfPageLeftOffsetProperty, value);
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

    // Cached cumulative Y positions for MinerU blocks (avoids O(n²) calculation in CalculateMinerUBlockEndPoint)
    private double[] _cachedMinerUCumulativeY = Array.Empty<double>();
    private bool _cumulativeYCacheValid = false;

    public override void Render(DrawingContext context)
    {
        if (!ShowConnections)
            return;

        if (Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        var minerUBlocks = MinerUBlocks;
        if (minerUBlocks is null || minerUBlocks.Count == 0)
            return;

        // ALWAYS rebuild the ID map because VisibleMinerUBlocks content changes
        // (Clear + Add) without changing the collection reference.
        // Using a local dictionary avoids allocation issues since it's short-lived.
        var minerUIdToIndex = new Dictionary<string, int>();
        for (int i = 0; i < minerUBlocks.Count; i++)
        {
            if (!string.IsNullOrEmpty(minerUBlocks[i].BlockId) && !minerUIdToIndex.ContainsKey(minerUBlocks[i].BlockId))
                minerUIdToIndex[minerUBlocks[i].BlockId] = i;
        }

        // Build cumulative Y cache for fast position lookup
        BuildCumulativeYCacheForList(minerUBlocks);

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
                return;
            RenderSinglePageConnections(context, preprocBlocks, minerUIdToIndex, 0);
        }
    }

    /// <summary>
    /// Builds cumulative Y cache for a given block list (used at render time).
    /// </summary>
    private void BuildCumulativeYCacheForList(IReadOnlyList<MinerUBlockViewModel> blocks)
    {
        if (blocks.Count == 0)
        {
            _cachedMinerUCumulativeY = Array.Empty<double>();
            _cumulativeYCacheValid = true;
            return;
        }

        var cache = new double[blocks.Count + 1];
        double cumulative = 0;
        cache[0] = 0;

        for (int i = 0; i < blocks.Count; i++)
        {
            var h = blocks[i].ActualHeight;
            if (h <= 0)
                h = MinerUBlockItemHeight;
            cache[i + 1] = cumulative + h + 4.0;
            cumulative = cache[i + 1];
        }

        _cachedMinerUCumulativeY = cache;
        _cumulativeYCacheValid = true;
    }

    /// <summary>
    /// Debug method removed to avoid excessive logging during scrolling.
    /// </summary>
    private void DebugLogConnectionLines()
    {
        // No-op: logging removed for performance
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
        double cumulativeYOffset = 0;

        // Sort page numbers without LINQ allocation
        _pageNumberBuffer.Clear();
        foreach (var key in preprocBlocksByPage.Keys)
            _pageNumberBuffer.Add(key);
        _pageNumberBuffer.Sort();

        for (int i = 0; i < _pageNumberBuffer.Count; i++)
        {
            var pageNumber = _pageNumberBuffer[i];
            var (preprocBlocks, pageSize) = preprocBlocksByPage[pageNumber];
            if (preprocBlocks.Count == 0)
                continue;

            RenderSinglePageConnections(context, preprocBlocks, minerUIdToIndex, cumulativeYOffset, pageSize);

            // Accumulate Y offset for next page
            cumulativeYOffset += pageSize.Height * ZoomLevel;
        }
    }

    // Reusable buffer for sorting page numbers (avoids LINQ allocation)
    private readonly List<int> _pageNumberBuffer = new();

    /// <summary>
    /// Renders connection lines for a single page's preproc_blocks.
    /// Uses for-loops to avoid LINQ allocation.
    /// </summary>
    private void RenderSinglePageConnections(
        DrawingContext context,
        IReadOnlyList<MinerUBlock> preprocBlocks,
        Dictionary<string, int> minerUIdToIndex,
        double pageYOffset,
        Size? pageSize = null)
    {
        var minerUBlocks = MinerUBlocks;
        if (minerUBlocks is null || minerUBlocks.Count == 0)
            return;

        // Determine which connections to draw, directly drawing without allocating a list
        if (!string.IsNullOrEmpty(SelectedBlockId))
        {
            var selId = SelectedBlockId;
            if (minerUIdToIndex.TryGetValue(selId, out var minerUIdx))
            {
                for (int i = 0; i < preprocBlocks.Count; i++)
                {
                    var preproc = preprocBlocks[i];
                    if (!string.IsNullOrEmpty(preproc.DestinationType) && preproc.RelatedBlockIds.Contains(selId))
                    {
                        DrawSingleConnection(context, preproc, minerUBlocks[minerUIdx], minerUIdToIndex, pageYOffset, pageSize);
                    }
                }
            }
        }
        else
        {
            for (int i = 0; i < preprocBlocks.Count; i++)
            {
                var preproc = preprocBlocks[i];
                if (!string.IsNullOrEmpty(preproc.DestinationType) && preproc.RelatedBlockIds.Count > 0)
                {
                    var targetId = preproc.RelatedBlockIds[0];
                    if (minerUIdToIndex.TryGetValue(targetId, out var minerUIdx))
                    {
                        DrawSingleConnection(context, preproc, minerUBlocks[minerUIdx], minerUIdToIndex, pageYOffset, pageSize);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Draws a single connection line with an additional Y offset for the page position.
    /// </summary>
    private void DrawSingleConnection(
        DrawingContext context,
        MinerUBlock preproc,
        MinerUBlockViewModel target,
        Dictionary<string, int> minerUIdToIndex,
        double pageYOffset,
        Size? pageSize = null)
    {
        var pdfBlockStart = CalculatePdfBlockStartPointWithOffset(preproc, pageYOffset, pageSize);
        var minerUIdx = minerUIdToIndex.TryGetValue(target.BlockId, out var idx) ? idx : -1;
        var minerUBlockEndPoint = CalculateMinerUBlockEndPoint(minerUIdx);

        if (IsOutsideVisibleArea(pdfBlockStart.Y, minerUBlockEndPoint.Y))
            return;

        var startPoint = pdfBlockStart;
        var endPoint = minerUBlockEndPoint;

        var horizontalGap = endPoint.X - startPoint.X;
        if (horizontalGap <= 0)
            return;

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

    /// <summary>
    /// Calculates the screen position on the BORDER of a preproc_block on the PDF page.
    /// Finds the intersection of the line from block center to target with the block's bounding box edge.
    /// This makes the connection line appear to start from the block's border rather than its center.
    /// </summary>
    private Point CalculatePdfBlockStartPoint(MinerUBlock block, Point targetPoint)
    {
        // First calculate the block's center in screen coordinates
        var center = CalculatePdfBlockCenter(block);
        
        // Calculate the block's bounding box in screen coordinates
        var bbox = block.Bbox;
        double left, top, right, bottom;

        if (block.IsBboxNormalized && PageSize.Width > 0 && PageSize.Height > 0)
        {
            left = PdfPageLeftOffset + bbox.X * PageSize.Width * ZoomLevel;
            top = PdfPageTopOffset + bbox.Y * PageSize.Height * ZoomLevel - PdfScrollOffsetY;
            right = PdfPageLeftOffset + (bbox.X + bbox.Width) * PageSize.Width * ZoomLevel;
            bottom = PdfPageTopOffset + (bbox.Y + bbox.Height) * PageSize.Height * ZoomLevel - PdfScrollOffsetY;
        }
        else
        {
            left = PdfPageLeftOffset + bbox.X * ZoomLevel;
            top = PdfPageTopOffset + bbox.Y * ZoomLevel - PdfScrollOffsetY;
            right = PdfPageLeftOffset + bbox.Right * ZoomLevel;
            bottom = PdfPageTopOffset + bbox.Bottom * ZoomLevel - PdfScrollOffsetY;
        }

        // Find the intersection of the line from center to target with the bounding box
        return IntersectionWithRect(center, targetPoint, left, top, right, bottom);
    }

    /// <summary>
    /// Like CalculatePdfBlockStartPoint but adds a page Y offset for multi-page rendering.
    /// </summary>
    private Point CalculatePdfBlockStartPointWithOffset(MinerUBlock block, double pageYOffset, Size? pageSize = null)
    {
        var page = pageSize ?? PageSize;
        
        // First calculate the block's center in screen coordinates (with offset)
        var center = CalculatePdfBlockCenterWithOffset(block, pageYOffset, page);
        
        // Calculate the block's bounding box in screen coordinates
        var bbox = block.Bbox;
        double left, top, right, bottom;

        if (block.IsBboxNormalized && page.Width > 0 && page.Height > 0)
        {
            left = PdfPageLeftOffset + bbox.X * page.Width * ZoomLevel;
            top = PdfPageTopOffset + bbox.Y * page.Height * ZoomLevel + pageYOffset - PdfScrollOffsetY;
            right = PdfPageLeftOffset + (bbox.X + bbox.Width) * page.Width * ZoomLevel;
            bottom = PdfPageTopOffset + (bbox.Y + bbox.Height) * page.Height * ZoomLevel + pageYOffset - PdfScrollOffsetY;
        }
        else
        {
            left = PdfPageLeftOffset + bbox.X * ZoomLevel;
            top = PdfPageTopOffset + bbox.Y * ZoomLevel + pageYOffset - PdfScrollOffsetY;
            right = PdfPageLeftOffset + bbox.Right * ZoomLevel;
            bottom = PdfPageTopOffset + bbox.Bottom * ZoomLevel + pageYOffset - PdfScrollOffsetY;
        }

        // Calculate the target point (MinerU block center) for direction
        // We need to find the intersection with the block's border
        return IntersectionWithRect(center, new Point(PdfColumnRightEdge + 100, center.Y), left, top, right, bottom);
    }

    /// <summary>
    /// Calculates the screen position of the center of a preproc_block on the PDF page.
    /// </summary>
    private Point CalculatePdfBlockCenter(MinerUBlock block)
    {
        var bbox = block.Bbox;
        double centerX, centerY;

        if (block.IsBboxNormalized && PageSize.Width > 0 && PageSize.Height > 0)
        {
            centerX = (bbox.X + bbox.Width / 2.0) * PageSize.Width;
            centerY = (bbox.Y + bbox.Height / 2.0) * PageSize.Height;
        }
        else
        {
            centerX = bbox.X + bbox.Width / 2.0;
            centerY = bbox.Y + bbox.Height / 2.0;
        }

        var screenX = PdfPageLeftOffset + centerX * ZoomLevel;
        var screenY = PdfPageTopOffset + centerY * ZoomLevel - PdfScrollOffsetY;
        return new Point(screenX, screenY);
    }

    /// <summary>
    /// Calculates the screen position of the center of a preproc_block with page offset.
    /// </summary>
    private Point CalculatePdfBlockCenterWithOffset(MinerUBlock block, double pageYOffset, Size page)
    {
        var bbox = block.Bbox;
        double centerX, centerY;

        if (block.IsBboxNormalized && page.Width > 0 && page.Height > 0)
        {
            centerX = (bbox.X + bbox.Width / 2.0) * page.Width;
            centerY = (bbox.Y + bbox.Height / 2.0) * page.Height;
        }
        else
        {
            centerX = bbox.X + bbox.Width / 2.0;
            centerY = bbox.Y + bbox.Height / 2.0;
        }

        var screenX = PdfPageLeftOffset + centerX * ZoomLevel;
        var screenY = PdfPageTopOffset + centerY * ZoomLevel + pageYOffset - PdfScrollOffsetY;
        return new Point(screenX, screenY);
    }

    /// <summary>
    /// Calculates the intersection point of a line from 'start' to 'end' with a rectangle.
    /// Returns the first intersection point on the rectangle's border.
    /// </summary>
    private static Point IntersectionWithRect(Point start, Point end, double left, double top, double right, double bottom)
    {
        // If start is inside the rectangle, find where the line exits the rectangle
        // If start is outside, find where it enters
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;

        // Handle zero direction
        if (dx == 0 && dy == 0)
            return start;

        // Find intersections with each edge of the rectangle
        // We want the closest intersection point in the direction from start to end
        double minT = double.MaxValue;
        Point result = start;

        // Left edge (x = left)
        if (dx != 0)
        {
            double t = (left - start.X) / dx;
            if (t > 0.001 && t < minT)
            {
                double y = start.Y + t * dy;
                if (y >= top && y <= bottom)
                {
                    minT = t;
                    result = new Point(left, y);
                }
            }
        }

        // Right edge (x = right)
        if (dx != 0)
        {
            double t = (right - start.X) / dx;
            if (t > 0.001 && t < minT)
            {
                double y = start.Y + t * dy;
                if (y >= top && y <= bottom)
                {
                    minT = t;
                    result = new Point(right, y);
                }
            }
        }

        // Top edge (y = top)
        if (dy != 0)
        {
            double t = (top - start.Y) / dy;
            if (t > 0.001 && t < minT)
            {
                double x = start.X + t * dx;
                if (x >= left && x <= right)
                {
                    minT = t;
                    result = new Point(x, top);
                }
            }
        }

        // Bottom edge (y = bottom)
        if (dy != 0)
        {
            double t = (bottom - start.Y) / dy;
            if (t > 0.001 && t < minT)
            {
                double x = start.X + t * dx;
                if (x >= left && x <= right)
                {
                    minT = t;
                    result = new Point(x, bottom);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Calculates the screen position of the LEFT EDGE of a MinerU block item in the list.
    /// Returns the center-left point of the block item in screen coordinates.
    /// Uses pre-computed cumulative Y cache for O(1) lookup instead of O(n) loop.
    /// </summary>
    private Point CalculateMinerUBlockEndPoint(int blockIndex)
    {
        if (blockIndex < 0)
            return new Point(MinerUColumnLeftEdge, MinerUListTopOffset);

        var blocks = MinerUBlocks;
        if (blocks is null || blockIndex >= blocks.Count)
            return new Point(MinerUColumnLeftEdge, MinerUListTopOffset);

        // Use pre-computed cumulative Y cache for O(1) lookup
        // cache[blockIndex] = cumulative Y before block at index blockIndex
        double itemTop;
        if (_cumulativeYCacheValid && blockIndex < _cachedMinerUCumulativeY.Length)
        {
            itemTop = _cachedMinerUCumulativeY[blockIndex];
        }
        else
        {
            // Fallback: compute on the fly (should rarely happen)
            itemTop = 0;
            for (int i = 0; i < blockIndex; i++)
            {
                var h = blocks[i].ActualHeight;
                if (h <= 0)
                    h = MinerUBlockItemHeight;
                itemTop += h + 4.0;
            }
        }

        var currentBlock = blocks[blockIndex];
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