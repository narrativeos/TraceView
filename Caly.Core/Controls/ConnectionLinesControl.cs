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

    public static readonly StyledProperty<int?> SelectedBlockIdProperty =
        AvaloniaProperty.Register<ConnectionLinesControl, int?>(nameof(SelectedBlockId));

    public static readonly StyledProperty<bool> ShowConnectionsProperty =
        AvaloniaProperty.Register<ConnectionLinesControl, bool>(nameof(ShowConnections), false);

    static ConnectionLinesControl()
    {
        AffectsRender<ConnectionLinesControl>(
            PreprocBlocksProperty,
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

    public int? SelectedBlockId
    {
        get => GetValue(SelectedBlockIdProperty);
        set => SetValue(SelectedBlockIdProperty, value);
    }

    public bool ShowConnections
    {
        get => GetValue(ShowConnectionsProperty);
        set => SetValue(ShowConnectionsProperty, value);
    }

    // High-visibility brushes
    private static readonly ImmutableSolidColorBrush AdoptedLineBrush =
        new(Color.Parse(MinerUConstants.AdoptedColor), 0.8);
    private static readonly ImmutableSolidColorBrush DiscardedLineBrush =
        new(Color.Parse(MinerUConstants.DiscardedColor), 0.8);
    private static readonly ImmutableSolidColorBrush DefaultLineBrush =
        new(Color.Parse(MinerUConstants.DefaultColor), 0.6);

    private static readonly ImmutablePen AdoptedPen = new(AdoptedLineBrush, 2.0);
    private static readonly ImmutablePen DiscardedPen = new(DiscardedLineBrush, 2.0);
    private static readonly ImmutablePen DefaultPen = new(DefaultLineBrush, 1.5);

    public override void Render(DrawingContext context)
    {
        if (!ShowConnections)
            return;
            
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        var preprocBlocks = PreprocBlocks;
        var minerUBlocks = MinerUBlocks;

        if (preprocBlocks is null || preprocBlocks.Count == 0 ||
            minerUBlocks is null || minerUBlocks.Count == 0)
        {
            System.Diagnostics.Debug.WriteLine($"[ConnectionLines.Render] Early exit: preproc={preprocBlocks?.Count ?? -1}, mineru={minerUBlocks?.Count ?? -1}");
            return;
        }

        // Filter to preproc_blocks that have a DestinationType (matched to a para/discarded block)
        var matchedPreproc = preprocBlocks.Where(b => !string.IsNullOrEmpty(b.DestinationType)).ToList();

        System.Diagnostics.Debug.WriteLine($"[ConnectionLines.Render] Total preproc={preprocBlocks.Count}, matched(with DestinationType)={matchedPreproc.Count}");
        if (matchedPreproc.Count > 0 && matchedPreproc.Count <= 5)
        {
            foreach (var mp in matchedPreproc)
            {
                System.Diagnostics.Debug.WriteLine($"  preproc id={mp.Id}, blockId={mp.BlockId}, destType={mp.DestinationType}, relatedIds=[{string.Join(",", mp.RelatedBlockIds)}]");
            }
        }

        if (matchedPreproc.Count == 0)
        {
            System.Diagnostics.Debug.WriteLine("[ConnectionLines.Render] No matched preproc blocks - no lines drawn");
            return;
        }

        // Build ID -> index map for MinerU blocks
        var minerUIdToIndex = new Dictionary<int, int>();
        for (int i = 0; i < minerUBlocks.Count; i++)
        {
            if (!minerUIdToIndex.ContainsKey(minerUBlocks[i].Id))
                minerUIdToIndex[minerUBlocks[i].Id] = i;
        }

        // Determine which connections to draw
        List<(MinerUBlock preproc, MinerUBlockViewModel target)> connections;

        if (SelectedBlockId.HasValue)
        {
            var selId = SelectedBlockId.Value;
            connections = new List<(MinerUBlock, MinerUBlockViewModel)>();

            // Find the selected block in MinerU list
            if (minerUIdToIndex.TryGetValue(selId, out var minerUIdx))
            {
                // Find all preproc blocks that point to this target
                foreach (var preproc in matchedPreproc)
                {
                    if (preproc.RelatedBlockIds.Contains(selId))
                    {
                        connections.Add((preproc, minerUBlocks[minerUIdx]));
                    }
                }
            }

            if (connections.Count == 0)
                return;
        }
        else
        {
            // Draw connections for all matched preproc blocks
            connections = new List<(MinerUBlock, MinerUBlockViewModel)>();
            foreach (var preproc in matchedPreproc)
            {
                // Each preproc block has exactly one RelatedBlockId (its target)
                if (preproc.RelatedBlockIds.Count > 0)
                {
                    var targetId = preproc.RelatedBlockIds[0];
                    if (minerUIdToIndex.TryGetValue(targetId, out var minerUIdx))
                    {
                        connections.Add((preproc, minerUBlocks[minerUIdx]));
                    }
                }
            }

            if (connections.Count == 0)
                return;
        }

        DrawConnections(context, connections, minerUIdToIndex);
    }

    private void DrawConnections(
        DrawingContext context,
        List<(MinerUBlock preproc, MinerUBlockViewModel target)> connections,
        Dictionary<int, int> minerUIdToIndex)
    {
        var minerULeft = MinerUColumnLeftEdge;

        foreach (var (preproc, target) in connections)
        {
            var pdfBlockStart = CalculatePdfBlockStartPoint(preproc);
            var minerUIdx = minerUIdToIndex.TryGetValue(target.Id, out var idx) ? idx : -1;
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

            // Use DestinationType to determine color
            var pen = preproc.DestinationType switch
            {
                MinerUConstants.DestPara => AdoptedPen,
                MinerUConstants.DestDiscarded => DiscardedPen,
                _ => DefaultPen
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
    /// </summary>
    private Point CalculateMinerUBlockEndPoint(int blockIndex)
    {
        if (blockIndex < 0)
            return new Point(MinerUColumnLeftEdge, MinerUListTopOffset);

        // Calculate the item's position in the list
        var itemTop = blockIndex * MinerUBlockItemHeight;
        var itemCenterY = itemTop + MinerUBlockItemHeight / 2.0;

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