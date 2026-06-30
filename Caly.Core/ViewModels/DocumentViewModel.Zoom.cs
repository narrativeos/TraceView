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

using System;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Caly.Core.ViewModels;

public partial class DocumentViewModel
{
    private static readonly double[] ZoomLevelsDiscrete =
    [
        0.08, 0.125, 0.25, 0.33, 0.5, 0.67, 0.75, 1,
        1.25, 1.5, 2, 3, 4, 6, 8, 12, 16, 24, 32, 48, 64
    ];

    /*
     * See PDF Reference 1.7 - C.2 Architectural limits
     * The magnification factor of a view should be constrained to be between approximately 8 percent and 6400 percent.
     */
    public double MinZoomLevel => 0.08;
    public double MaxZoomLevel => 64;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ZoomInCommand))]
    [NotifyCanExecuteChangedFor(nameof(ZoomOutCommand))]
    private double _zoomLevel = 1;

    /// <summary>
    /// Scroll offset to restore when this document's tab becomes active again. Y is
    /// relative to the top of <see cref="SelectedPageNumber"/>. Stored in unscaled
    /// document coordinates (independent of <see cref="ZoomLevel"/>).
    /// </summary>
    [ObservableProperty] private Vector _scrollOffset;

    [RelayCommand(CanExecute = nameof(CanZoomIn))]
    private void ZoomIn()
    {
        var index = Array.BinarySearch(ZoomLevelsDiscrete, ZoomLevel);
        if (index < -1)
        {
            ZoomLevel = Math.Min(MaxZoomLevel, ZoomLevelsDiscrete[~index]);
        }
        else
        {
            if (index >= ZoomLevelsDiscrete.Length - 1)
            {
                return;
            }

            ZoomLevel = Math.Min(MaxZoomLevel, ZoomLevelsDiscrete[index + 1]);
        }
    }

    private bool CanZoomIn()
    {
        return ZoomLevel < MaxZoomLevel;
    }

    [RelayCommand(CanExecute = nameof(CanZoomOut))]
    private void ZoomOut()
    {
        var index = Array.BinarySearch(ZoomLevelsDiscrete, ZoomLevel);
        if (index < -1)
        {
            ZoomLevel = Math.Max(MinZoomLevel, ZoomLevelsDiscrete[~index - 1]);
        }
        else
        {
            if (index == 0)
            {
                return;
            }

            ZoomLevel = Math.Max(MinZoomLevel, ZoomLevelsDiscrete[index - 1]);
        }
    }

    private bool CanZoomOut()
    {
        return ZoomLevel > MinZoomLevel;
    }

    /// <summary>
    /// Viewport width in pixels, used for fit-to-width calculations.
    /// Set by the PageItemsControl when its size changes.
    /// </summary>
    [ObservableProperty]
    private double _viewportWidth;

    /// <summary>
    /// Whether the first page has been loaded and auto-fit to width has been performed.
    /// Used to trigger auto-fit only once when the document is first opened.
    /// </summary>
    [ObservableProperty]
    private bool _autoFitDone;

    [RelayCommand]
    private void ZoomToPageWidth()
    {
        if (Pages.Count == 0 || !SelectedPageNumber.HasValue)
        {
            return;
        }

        var currentPage = Pages[SelectedPageNumber.Value - 1];
        double pageWidth = currentPage.Size.Width;

        if (pageWidth <= 0 || ViewportWidth <= 0)
        {
            return;
        }

        // Calculate the zoom level that makes the current page fill the viewport width
        // Apply 0.97 scaling factor to ensure the page fits within the actual visible area
        // without triggering horizontal scrollbar (accounts for layout overhead, rounding, etc.)
        double targetZoom = (ViewportWidth * 0.97) / pageWidth;

        // Clamp to allowed zoom range
        ZoomLevel = Math.Max(MinZoomLevel, Math.Min(MaxZoomLevel, targetZoom));
    }

    [RelayCommand]
    private void RotateAllPagesClockwise()
    {
        foreach (PageViewModel page in Pages)
        {
            page.RotateClockwise();
        }
    }

    [RelayCommand]
    private void RotateAllPagesCounterclockwise()
    {
        foreach (PageViewModel page in Pages)
        {
            page.RotateCounterclockwise();
        }
    }
}