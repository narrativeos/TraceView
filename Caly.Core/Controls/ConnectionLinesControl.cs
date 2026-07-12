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
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.VisualTree;
using Caly.Core.Models;
using Caly.Core.Utilities;
using Caly.Core.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;

namespace Caly.Core.Controls;

/// <summary>
/// Custom control that draws connecting lines between:
/// 1. preproc_blocks (PDF overlay) and para/discarded blocks (MinerU Blocks column)
/// 2. MinerU Blocks and Popo Blocks (when Popo data is available)
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

    /// <summary>
    /// Right edge of the MinerU column (MinerUColumnLeftEdge + MinerU column width).
    /// Used for calculating the start point of MinerU → Popo connection lines.
    /// </summary>
    public static readonly StyledProperty<double> MinerUColumnRightEdgeProperty =
        AvaloniaProperty.Register<ConnectionLinesControl, double>(nameof(MinerUColumnRightEdge));

    public static readonly StyledProperty<double> PdfPageLeftOffsetProperty =
        AvaloniaProperty.Register<ConnectionLinesControl, double>(nameof(PdfPageLeftOffset));

    public static readonly StyledProperty<double> PdfPageTopOffsetProperty =
        AvaloniaProperty.Register<ConnectionLinesControl, double>(nameof(PdfPageTopOffset));

    public static readonly StyledProperty<double> MinerUListTopOffsetProperty =
        AvaloniaProperty.Register<ConnectionLinesControl, double>(nameof(MinerUListTopOffset));

    public static readonly StyledProperty<double> MinerUBlockItemHeightProperty =
        AvaloniaProperty.Register<ConnectionLinesControl, double>(nameof(MinerUBlockItemHeight), 60.0);

    /// <summary>
    /// PPI scale factor for converting PDF points to display pixels.
    /// Used when block coordinates are in PDF point space (not normalized).
    /// </summary>
    public static readonly StyledProperty<double> PpiScaleProperty =
        AvaloniaProperty.Register<ConnectionLinesControl, double>(nameof(PpiScale), 1.0);

    public static readonly StyledProperty<string?> SelectedBlockIdProperty =
        AvaloniaProperty.Register<ConnectionLinesControl, string?>(nameof(SelectedBlockId));

    public static readonly StyledProperty<bool> ShowConnectionsProperty =
        AvaloniaProperty.Register<ConnectionLinesControl, bool>(nameof(ShowConnections), false);

    /// <summary>
    /// Whether to show connection lines between MinerU Blocks and Popo Blocks.
    /// </summary>
    public static readonly StyledProperty<bool> ShowPopoConnectionsProperty =
        AvaloniaProperty.Register<ConnectionLinesControl, bool>(nameof(ShowPopoConnections), false);

    /// <summary>
    /// Left edge of the Popo column (for MinerU → Popo connection lines).
    /// </summary>
    public static readonly StyledProperty<double> PopoColumnLeftEdgeProperty =
        AvaloniaProperty.Register<ConnectionLinesControl, double>(nameof(PopoColumnLeftEdge));

    /// <summary>
    /// Scroll offset Y for the Popo column.
    /// </summary>
    public static readonly StyledProperty<double> PopoScrollOffsetYProperty =
        AvaloniaProperty.Register<ConnectionLinesControl, double>(nameof(PopoScrollOffsetY));

    /// <summary>
    /// Top offset of the Popo tree items within the Popo column.
    /// </summary>
    public static readonly StyledProperty<double> PopoListTopOffsetProperty =
        AvaloniaProperty.Register<ConnectionLinesControl, double>(nameof(PopoListTopOffset));

    /// <summary>
    /// Flat list of visible Popo tree nodes (respecting IsExpanded state).
    /// Used for drawing connection lines from MinerU blocks to Popo nodes.
    /// </summary>
    public static readonly StyledProperty<IReadOnlyList<TreeNodeViewModel>?> VisiblePopoNodesProperty =
        AvaloniaProperty.Register<ConnectionLinesControl, IReadOnlyList<TreeNodeViewModel>?>(nameof(VisiblePopoNodes));

    /// <summary>
    /// Reference to the BlockOverlayControl for getting actual block positions.
    /// When set, connection lines will use the overlay's Bounds to calculate positions.
    /// </summary>
    public static readonly StyledProperty<BlockOverlayControl?> PdfOverlayReferenceProperty =
        AvaloniaProperty.Register<ConnectionLinesControl, BlockOverlayControl?>(nameof(PdfOverlayReference));

    /// <summary>
    /// Reference to the MinerU ItemsControl for getting actual block item positions.
    /// When set, connection lines will use the items' actual rendered positions.
    /// </summary>
    public static readonly StyledProperty<ItemsControl?> MinerUItemsReferenceProperty =
        AvaloniaProperty.Register<ConnectionLinesControl, ItemsControl?>(nameof(MinerUItemsReference));

    /// <summary>
    /// Reference to the Popo TreeView for getting actual tree node positions.
    /// When set, connection lines will query the visual tree for accurate positions.
    /// </summary>
    public static readonly StyledProperty<TreeView?> PopoTreeViewReferenceProperty =
        AvaloniaProperty.Register<ConnectionLinesControl, TreeView?>(nameof(PopoTreeViewReference));

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
            PdfPageLeftOffsetProperty,
            PdfPageTopOffsetProperty,
            MinerUListTopOffsetProperty,
            MinerUBlockItemHeightProperty,
            PpiScaleProperty,
            SelectedBlockIdProperty,
            ShowConnectionsProperty,
            ShowPopoConnectionsProperty,
            PopoColumnLeftEdgeProperty,
            PopoScrollOffsetYProperty,
            PopoListTopOffsetProperty,
            VisiblePopoNodesProperty);

        // Subscribe to scroll offset property changes for throttled rendering
        PdfScrollOffsetYProperty.Changed.AddClassHandler<ConnectionLinesControl>((control, e) =>
        {
            control.ScheduleRender();
        });
        MinerUScrollOffsetYProperty.Changed.AddClassHandler<ConnectionLinesControl>((control, e) =>
        {
            control.ScheduleRender();
        });
        PopoScrollOffsetYProperty.Changed.AddClassHandler<ConnectionLinesControl>((control, e) =>
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

    public double MinerUColumnRightEdge
    {
        get => GetValue(MinerUColumnRightEdgeProperty);
        set => SetValue(MinerUColumnRightEdgeProperty, value);
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

    public double PpiScale
    {
        get => GetValue(PpiScaleProperty);
        set => SetValue(PpiScaleProperty, value);
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

    public bool ShowPopoConnections
    {
        get => GetValue(ShowPopoConnectionsProperty);
        set => SetValue(ShowPopoConnectionsProperty, value);
    }

    public double PopoColumnLeftEdge
    {
        get => GetValue(PopoColumnLeftEdgeProperty);
        set => SetValue(PopoColumnLeftEdgeProperty, value);
    }

    public double PopoScrollOffsetY
    {
        get => GetValue(PopoScrollOffsetYProperty);
        set => SetValue(PopoScrollOffsetYProperty, value);
    }

    public double PopoListTopOffset
    {
        get => GetValue(PopoListTopOffsetProperty);
        set => SetValue(PopoListTopOffsetProperty, value);
    }

    public IReadOnlyList<TreeNodeViewModel>? VisiblePopoNodes
    {
        get => GetValue(VisiblePopoNodesProperty);
        set => SetValue(VisiblePopoNodesProperty, value);
    }

    public BlockOverlayControl? PdfOverlayReference
    {
        get => GetValue(PdfOverlayReferenceProperty);
        set => SetValue(PdfOverlayReferenceProperty, value);
    }

    public ItemsControl? MinerUItemsReference
    {
        get => GetValue(MinerUItemsReferenceProperty);
        set => SetValue(MinerUItemsReferenceProperty, value);
    }

    public TreeView? PopoTreeViewReference
    {
        get => GetValue(PopoTreeViewReferenceProperty);
        set => SetValue(PopoTreeViewReferenceProperty, value);
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

    // Purple line for MinerU → Popo connections
    private static readonly ImmutableSolidColorBrush PopoLineBrush =
        new(Color.Parse("#9C27B0"), 0.7);
    private static readonly ImmutablePen PopoPen = new(PopoLineBrush, 1.5);

    // Cached cumulative Y positions for MinerU blocks (avoids O(n²) calculation in CalculateMinerUBlockEndPoint)
    private double[] _cachedMinerUCumulativeY = Array.Empty<double>();
    private bool _cumulativeYCacheValid = false;

    // Cached cumulative Y positions for Popo nodes
    private double[] _cachedPopoCumulativeY = Array.Empty<double>();
    private bool _popoCumulativeYCacheValid = false;

    public override void Render(DrawingContext context)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        // Render PDF → MinerU connections
        if (ShowConnections)
        {
            RenderPdfToMinerUConnections(context);
        }

        // Render MinerU → Popo connections
        if (ShowPopoConnections)
        {
            RenderMinerUToPopoConnections(context);
        }
    }

    private void RenderPdfToMinerUConnections(DrawingContext context)
    {
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

    #region MinerU → Popo Connections

    /// <summary>
    /// Renders connection lines from MinerU Blocks to Popo Blocks.
    /// Each MinerU block is connected to the Popo node whose SourceBlockIds contains the block's BlockId.
    /// Uses visual tree queries for accurate Popo node positions.
    /// </summary>
    private void RenderMinerUToPopoConnections(DrawingContext context)
    {
        var minerUBlocks = MinerUBlocks;
        var popoNodes = VisiblePopoNodes;

        if (minerUBlocks is null || minerUBlocks.Count == 0)
            return;
        if (popoNodes is null || popoNodes.Count == 0)
            return;

        // Build cumulative Y cache for MinerU blocks
        BuildCumulativeYCacheForList(minerUBlocks);

        // Build a map: BlockId (UUID) -> Popo node index
        // A MinerU block's BlockId may appear in multiple Popo nodes' SourceBlockIds,
        // but we draw to the first matching node.
        var blockIdToPopoIndex = new Dictionary<string, int>();
        for (int i = 0; i < popoNodes.Count; i++)
        {
            var node = popoNodes[i];
            foreach (var srcId in node.SourceBlockIds)
            {
                if (!string.IsNullOrEmpty(srcId) && !blockIdToPopoIndex.ContainsKey(srcId))
                {
                    blockIdToPopoIndex[srcId] = i;
                }
            }
        }

        // Calculate a safe minimum horizontal span for the S-curve.
        // Because MinerU and Popo columns are very close (only 4px GridSplitter),
        // a direct curve would be nearly vertical and cause severe overlap.
        // Instead, we create a S-curve that bows outward on both sides,
        // extending beyond each column's edge to create visual separation.
        // The bow distance is calculated as a fraction of the vertical distance,
        // creating a smooth arc that makes each line distinguishable.
        const double minBowDistance = 40.0; // Minimum outward bow in pixels

        // Draw connections: MinerU block -> Popo node
        for (int i = 0; i < minerUBlocks.Count; i++)
        {
            var block = minerUBlocks[i];
            if (string.IsNullOrEmpty(block.BlockId))
                continue;

            if (!blockIdToPopoIndex.TryGetValue(block.BlockId, out var popoIdx))
                continue;

            // Calculate start point (center of MinerU block)
            var startPoint = CalculateMinerUBlockCenter(i);

            // Calculate end point using visual tree query for accurate position
            var endPoint = GetPopoNodePositionFromVisualTree(popoNodes[popoIdx]);

            // Skip if outside visible area
            if (IsOutsideVisibleArea(startPoint.Y, endPoint.Y))
                continue;

            // Calculate vertical distance between start and end
            var verticalDist = Math.Abs(endPoint.Y - startPoint.Y);
            // Bow distance: proportional to vertical distance, but with a minimum
            // This creates curves that bow outward more when the Y difference is large,
            // and still bow enough when Y is similar (preventing overlap)
            var bowDistance = Math.Max(minBowDistance, verticalDist * 0.5);

            // S-curve: bow RIGHT from MinerU start, bow LEFT from Popo end
            // This creates a visible arc between the two columns
            var controlPoint1 = new Point(startPoint.X + bowDistance, startPoint.Y);
            var controlPoint2 = new Point(endPoint.X - bowDistance, endPoint.Y);

            var sg = new StreamGeometry();
            using (var ctx = sg.Open())
            {
                ctx.BeginFigure(startPoint, false);
                ctx.CubicBezierTo(controlPoint1, controlPoint2, endPoint);
                ctx.EndFigure(false);
            }

            context.DrawGeometry(null, PopoPen, sg);
        }
    }

    /// <summary>
    /// Gets the actual screen position of a Popo tree node by querying the visual tree.
    /// Falls back to calculated position if the visual tree query fails.
    /// </summary>
    private Point GetPopoNodePositionFromVisualTree(TreeNodeViewModel nodeVm)
    {
        // Try to get the position from the visual tree
        var treeView = PopoTreeViewReference;
        if (treeView is not null)
        {
            // Find the TreeViewItem for this node by walking the visual tree
            var container = FindContainerForNode(treeView, nodeVm);
            if (container is not null)
            {
                // Get the Border inside the TreeViewItem
                var border = container.GetVisualDescendants().OfType<Border>().FirstOrDefault();
                if (border is not null)
                {
                    // Get the position relative to the ConnectionLinesControl
                    var transform = border.TransformToVisual(this);
                    if (transform is Matrix m)
                    {
                        var point = m.Transform(new Point(0, 0));
                        // Return the center of the border
                        return new Point(point.X + border.Bounds.Width / 2.0, point.Y + border.Bounds.Height / 2.0);
                    }
                }
            }
        }

        // Fallback: use calculated position (find the node index in VisiblePopoNodes)
        if (VisiblePopoNodes is not null)
        {
            for (int i = 0; i < VisiblePopoNodes.Count; i++)
            {
                if (ReferenceEquals(VisiblePopoNodes[i], nodeVm))
                {
                    return CalculatePopoNodeCenter(i);
                }
            }
        }

        // Last resort fallback
        return new Point(PopoColumnLeftEdge + 150.0, PopoListTopOffset);
    }

    /// <summary>
    /// Finds the TreeViewItem container for a given TreeNodeViewModel by walking the visual tree.
    /// </summary>
    private TreeViewItem? FindContainerForNode(TreeView treeView, TreeNodeViewModel targetNode)
    {
        // Use GetVisualDescendants to find all TreeViewItems
        foreach (var item in treeView.GetVisualDescendants().OfType<TreeViewItem>())
        {
            if (item.DataContext is TreeNodeViewModel nodeVm && ReferenceEquals(nodeVm, targetNode))
                return item;
        }
        return null;
    }

    /// <summary>
    /// Builds cumulative Y cache for Popo nodes using actual rendered heights.
    /// </summary>
    private void BuildPopoCumulativeYCache(IReadOnlyList<TreeNodeViewModel> nodes)
    {
        if (nodes.Count == 0)
        {
            _cachedPopoCumulativeY = Array.Empty<double>();
            _popoCumulativeYCacheValid = true;
            return;
        }

        var cache = new double[nodes.Count + 1];
        double cumulative = 0;
        cache[0] = 0;

        for (int i = 0; i < nodes.Count; i++)
        {
            // Use actual rendered height from the ViewModel
            var h = nodes[i].ActualHeight;
            if (h <= 0)
                h = 80.0; // Fallback height
            cache[i + 1] = cumulative + h + 4.0; // 4px gap between items
            cumulative = cache[i + 1];
        }

        _cachedPopoCumulativeY = cache;
        _popoCumulativeYCacheValid = true;
    }

    /// <summary>
    /// Calculates the CENTER of a MinerU block for connection start point.
    /// </summary>
    private Point CalculateMinerUBlockCenter(int blockIndex)
    {
        if (blockIndex < 0)
            return new Point(MinerUColumnLeftEdge, MinerUListTopOffset);

        var blocks = MinerUBlocks;
        if (blocks is null || blockIndex >= blocks.Count)
            return new Point(MinerUColumnLeftEdge, MinerUListTopOffset);

        // Use pre-computed cumulative Y cache
        double itemTop;
        if (_cumulativeYCacheValid && blockIndex < _cachedMinerUCumulativeY.Length)
        {
            itemTop = _cachedMinerUCumulativeY[blockIndex];
        }
        else
        {
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

        // Center X of MinerU column
        var itemCenterX = (MinerUColumnLeftEdge + MinerUColumnRightEdge) / 2.0;
        var screenY = MinerUListTopOffset + itemCenterY - MinerUScrollOffsetY;

        return new Point(itemCenterX, screenY);
    }

    /// <summary>
    /// Calculates the CENTER of a Popo node for connection end point.
    /// </summary>
    private Point CalculatePopoNodeCenter(int nodeIndex)
    {
        if (nodeIndex < 0)
            return new Point(PopoColumnLeftEdge, PopoListTopOffset);

        var nodes = VisiblePopoNodes;
        if (nodes is null || nodeIndex >= nodes.Count)
            return new Point(PopoColumnLeftEdge, PopoListTopOffset);

        // Use pre-computed cumulative Y cache
        double itemTop;
        if (_popoCumulativeYCacheValid && nodeIndex < _cachedPopoCumulativeY.Length)
        {
            itemTop = _cachedPopoCumulativeY[nodeIndex];
        }
        else
        {
            itemTop = 0;
            for (int i = 0; i < nodeIndex; i++)
            {
                var h = nodes[i].ActualHeight;
                if (h <= 0) h = 80.0;
                itemTop += h + 4.0;
            }
        }

        var currentNode = nodes[nodeIndex];
        var nodeHeight = currentNode.ActualHeight;
        if (nodeHeight <= 0) nodeHeight = 80.0;
        var nodeCenterY = itemTop + nodeHeight / 2.0;

        // Center X of Popo column (estimate: PopoColumnLeftEdge + half of estimated Popo column width)
        // We don't have PopoColumnRightEdge, so estimate the center as ~150px into the Popo column
        var itemCenterX = PopoColumnLeftEdge + 150.0;
        var screenY = PopoListTopOffset + nodeCenterY - PopoScrollOffsetY;

        return new Point(itemCenterX, screenY);
    }

    #endregion

    #region PDF Position Calculation

    /// <summary>
    /// Calculates the RIGHT EDGE of a preproc_block on the PDF page, with Y at the block center.
    /// This is the start point for connection lines going from PDF to MinerU column.
    /// </summary>
    private Point CalculatePdfBlockStartPoint(MinerUBlock block)
    {
        var bbox = block.Bbox;
        double blockRightX, blockCenterY;

        if (block.IsBboxNormalized && PageSize.Width > 0 && PageSize.Height > 0)
        {
            blockRightX = (bbox.X + bbox.Width) * PageSize.Width;
            blockCenterY = (bbox.Y + bbox.Height / 2.0) * PageSize.Height;
        }
        else
        {
            // Non-normalized: coordinates are in PDF point space.
            // Multiply by PpiScale to convert to display pixel space.
            blockRightX = bbox.Right * PpiScale;
            blockCenterY = (bbox.Y + bbox.Height / 2.0) * PpiScale;
        }

        var screenX = PdfPageLeftOffset + blockRightX * ZoomLevel;
        var screenY = PdfPageTopOffset + blockCenterY * ZoomLevel - PdfScrollOffsetY;
        return new Point(screenX, screenY);
    }

    /// <summary>
    /// Like CalculatePdfBlockStartPoint but adds a page Y offset for multi-page rendering.
    /// </summary>
    private Point CalculatePdfBlockStartPointWithOffset(MinerUBlock block, double pageYOffset, Size? pageSize = null)
    {
        var page = pageSize ?? PageSize;
        var bbox = block.Bbox;
        double blockRightX, blockCenterY;

        if (block.IsBboxNormalized && page.Width > 0 && page.Height > 0)
        {
            blockRightX = (bbox.X + bbox.Width) * page.Width;
            blockCenterY = (bbox.Y + bbox.Height / 2.0) * page.Height;
        }
        else
        {
            // Non-normalized: coordinates are in PDF point space.
            // Multiply by PpiScale to convert to display pixel space.
            blockRightX = bbox.Right * PpiScale;
            blockCenterY = (bbox.Y + bbox.Height / 2.0) * PpiScale;
        }

        var screenX = PdfPageLeftOffset + blockRightX * ZoomLevel;
        var screenY = PdfPageTopOffset + blockCenterY * ZoomLevel + pageYOffset - PdfScrollOffsetY;
        return new Point(screenX, screenY);
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

    private bool IsOutsideVisibleArea(double y1, double y2)
    {
        var boundsTop = -20;
        var boundsBottom = Bounds.Height + 20;

        var bothAbove = y1 < boundsTop && y2 < boundsTop;
        var bothBelow = y1 > boundsBottom && y2 > boundsBottom;

        return bothAbove || bothBelow;
    }

    #endregion
}