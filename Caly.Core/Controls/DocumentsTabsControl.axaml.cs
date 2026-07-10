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
using Avalonia.Controls.Metadata;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Caly.Core.Models;
using Caly.Core.Utilities;
using Caly.Core.ViewModels;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Linq;

namespace Caly.Core.Controls;

[TemplatePart("PART_TextBoxPageNumber", typeof(TextBox))]
[TemplatePart("PART_SplitView", typeof(SplitView))]
[TemplatePart("PART_DocumentControl", typeof(DocumentControl))]
[TemplatePart("PART_MinerUScrollViewer", typeof(ScrollViewer))]
[TemplatePart("PART_ConnectionLinesControl", typeof(ConnectionLinesControl))]
[TemplatePart("PART_ThreeColumnGrid", typeof(Grid))]
public sealed partial class DocumentsTabsControl : UserControl
{
    private const int MaxPaneLength = 500;
    private const int MinPaneLength = 200;

    private Point? _lastPoint;
    private double _originalPaneLength;

    private SplitView? _splitView;
    private TextBox? _textBoxPageNumber;
    private DocumentControl? _documentControl;
    private ScrollViewer? _minerUScrollViewer;
    private ConnectionLinesControl? _connectionLinesControl;
    private Grid? _threeColumnGrid;

    public DocumentsTabsControl()
    {
        InitializeComponent();
        KeyBindings.Add(new KeyBinding
        {
            Gesture = CalyHotkeyConfiguration.DocumentGoToGesture,
            Command = new RelayCommand(() =>
            {
                var textBox = GetTextBoxPageNumber();
                if (textBox is null)
                {
                    return;
                }

                textBox.Focus();
                textBox.SelectAll();
            })
        });
    }

    private TextBox? GetTextBoxPageNumber()
    {
        if (_textBoxPageNumber is null)
        {
            _textBoxPageNumber = this.FindDescendantOfType<TextBox>(false, tb => tb.Name == "PART_TextBoxPageNumber");
        }
        return _textBoxPageNumber;
    }

    private SplitView? GetSplitView()
    {
        if (_splitView is null)
        {
            _splitView = this.FindDescendantOfType<SplitView>();
            if (_splitView is null)
            {
                return null;
            }

            if (string.IsNullOrEmpty(_splitView.Name) || !_splitView.Name.Equals("PART_SplitView"))
            {
                throw new Exception("The found split view does not have the correct name.");
            }
        }

        return _splitView;
    }

    #region Resize SplitView.Pane
    private void Resize_OnPointerEntered(object? sender, PointerEventArgs e)
    {
        Debug.ThrowNotOnUiThread();
        Cursor = App.SizeWestEastCursor;
    }

    private void Resize_OnPointerExited(object? sender, PointerEventArgs e)
    {
        Debug.ThrowNotOnUiThread();
        Cursor = App.DefaultCursor;
    }

    private void Resize_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Grid)
        {
            return;
        }

        SplitView? splitView = GetSplitView();

        if (splitView is null)
        {
            return;
        }

        if (!splitView.IsPaneOpen)
        {
            return;
        }

        _lastPoint = e.GetPosition(null);
        _originalPaneLength = splitView.OpenPaneLength;
        e.Handled = true;
        e.PreventGestureRecognition();
    }

    private void Resize_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_lastPoint.HasValue || sender is not Grid)
        {
            return;
        }

        SplitView? splitView = GetSplitView();

        if (splitView is null || !splitView.IsPaneOpen)
        {
            return;
        }

        Point mouseMovement = (e.GetPosition(null) - _lastPoint).Value;
        splitView.OpenPaneLength = Math.Max(Math.Min(_originalPaneLength + mouseMovement.X, MaxPaneLength), MinPaneLength);
    }

    private void Resize_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_lastPoint.HasValue || sender is not Grid)
        {
            return;
        }

        _lastPoint = null;
        _originalPaneLength = 0;
        e.Handled = true;
    }
    #endregion

    private void PageNumberTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        if (sender is not TextBox textBox)
        {
            return;
        }

        BindingOperations.GetBindingExpressionBase(textBox, TextBox.TextProperty)?.UpdateSource();
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged ||
            sender is not DocumentsTabsControl tabsControl ||
            e.NewSize.Width > e.PreviousSize.Width)
        {
            return;
        }

        var splitView = GetSplitView();
        if (splitView is null)
        {
            return;
        }

        if (splitView.IsPaneOpen && tabsControl.Bounds.Width < splitView.OpenPaneLength * 2)
        {
            splitView.SetCurrentValue(SplitView.IsPaneOpenProperty, false);
        }
    }

    private void EmbeddedFiles_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.Properties.IsLeftButtonPressed || e.ClickCount != 2)
        {
            return;
        }

        if (sender is not Control ctrl || ctrl.DataContext is not PdfEmbeddedFileViewModel vm)
        {
            return;
        }
        
        vm.OpenCommand.Execute(null);
        e.Handled = true;
    }

    private void MinerUBlock_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border || border.DataContext is not MinerUBlockViewModel blockVm)
            return;

        var tabsControl = border.FindAncestorOfType<DocumentsTabsControl>();
        if (tabsControl?.DataContext is not MainViewModel mainVm)
            return;

        if (mainVm.SelectedDocument is DocumentViewModel currentDoc)
        {
            currentDoc.SelectMinerUBlockCommand?.Execute(blockVm.Id);
            e.Handled = true;
        }
    }

    private void MinerUBlockBorder_OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not Border border || border.DataContext is not MinerUBlockViewModel blockVm)
            return;

        // Set the actual rendered height so ConnectionLinesControl can calculate accurate positions
        blockVm.ActualHeight = border.Bounds.Height;
        
        // Trigger connection lines redraw when a block is loaded
        UpdateConnectionLines();
    }

    private void OnMinerUScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        UpdateConnectionLines();
    }

    private ConnectionLinesControl? GetConnectionLinesControl()
    {
        if (_connectionLinesControl is null)
        {
            _connectionLinesControl = this.FindDescendantOfType<ConnectionLinesControl>();
            if (_connectionLinesControl is not null)
            {
                _connectionLinesControl.SizeChanged += (_, _) => UpdateConnectionLines();
            }
        }
        return _connectionLinesControl;
    }

    private DocumentControl? GetDocumentControl()
    {
        if (_documentControl is null)
        {
            _documentControl = this.FindDescendantOfType<DocumentControl>();
            if (_documentControl is not null)
            {
                var pageItemsControl = _documentControl.FindDescendantOfType<PageItemsControl>();
                if (pageItemsControl?.Scroll is { } scroll)
                {
                    scroll.ScrollChanged += (_, _) => UpdateConnectionLines();
                }
            }
        }
        return _documentControl;
    }

    private ScrollViewer? GetMinerUScrollViewer()
    {
        if (_minerUScrollViewer is null)
        {
            _minerUScrollViewer = this.FindDescendantOfType<ScrollViewer>(false, sv => sv.Name == "PART_MinerUScrollViewer");
        }
        return _minerUScrollViewer;
    }

    private Grid? GetThreeColumnGrid()
    {
        if (_threeColumnGrid is null)
        {
            _threeColumnGrid = this.FindDescendantOfType<Grid>(false, g => g.Name == "PART_ThreeColumnGrid");
        }
        return _threeColumnGrid;
    }

    private void UpdateConnectionLines()
    {
        var connControl = GetConnectionLinesControl();
        if (connControl is null)
        {
            System.Diagnostics.Debug.WriteLine("[ConnectionLines] connControl is null");
            return;
        }

        System.Diagnostics.Debug.WriteLine($"[ConnectionLines] Bounds={connControl.Bounds.Width}x{connControl.Bounds.Height}, ShowConnections={connControl.ShowConnections}");

        if (DataContext is not MainViewModel mainVm || mainVm.SelectedDocument is not DocumentViewModel docVm)
        {
            connControl.ShowConnections = false;
            return;
        }

        if (!docVm.ShowMinerUColumn || docVm.MinerUBlocks.Count == 0)
        {
            connControl.ShowConnections = false;
            return;
        }

        var currentPage = docVm.Pages.FirstOrDefault(p => p.PageNumber == docVm.SelectedPageNumber);
        if (currentPage?.MinerUBlocks is null || currentPage.MinerUBlocks.Count == 0)
        {
            connControl.ShowConnections = false;
            return;
        }

        // Debug: check if PreprocBlocks are available
        var preprocCount = currentPage.PreprocBlocks?.Count ?? 0;
        System.Diagnostics.Debug.WriteLine($"[ConnectionLines] Page {currentPage.PageNumber}: MinerUBlocks={currentPage.MinerUBlocks.Count}, PreprocBlocks={preprocCount}");

        if (preprocCount == 0)
        {
            // No preproc blocks for this page — connection lines cannot be drawn
            connControl.ShowConnections = false;
            return;
        }

        connControl.ShowConnections = true;
        connControl.PreprocBlocks = currentPage.PreprocBlocks;
        connControl.MinerUBlocks = docVm.MinerUBlocks;
        connControl.PageSize = currentPage.Size;
        connControl.ZoomLevel = docVm.ZoomLevel;
        connControl.SelectedBlockId = docVm.SelectedMinerUBlockId;

        // Force a render after layout is applied (IsVisible change triggers layout asynchronously)
        connControl.InvalidateVisual();

        // PDF scroll offset
        var docControl = GetDocumentControl();
        if (docControl is not null)
        {
            var pageItemsControl = docControl.FindDescendantOfType<PageItemsControl>();
            if (pageItemsControl?.Scroll is { } pdfScroll)
            {
                var pageItem = pageItemsControl.GetPageItem(docVm.SelectedPageNumber ?? 1);
                if (pageItem is not null)
                {
                    var scale = pageItemsControl.LayoutTransform?.LayoutTransform?.Value.M11 ?? 1.0;
                    connControl.PdfScrollOffsetY = pdfScroll.Offset.Y - pageItem.Bounds.Top * scale;
                    connControl.PdfPageTopOffset = 0;
                }
            }
        }

        // MinerU scroll offset + list top offset
        var minerUScroll = GetMinerUScrollViewer();
        if (minerUScroll is not null)
        {
            connControl.MinerUScrollOffsetY = minerUScroll.Offset.Y;
            // Border Padding=8 + header StackPanel(~20) + margin=6 + ItemsControl padding=8
            connControl.MinerUListTopOffset = 42;
        }

        // Column edges
        var layoutGrid = GetThreeColumnGrid();
        if (layoutGrid is not null && connControl.Bounds.Width > 0 && layoutGrid.ColumnDefinitions.Count > 0)
        {
            var pdfColumnWidth = layoutGrid.ColumnDefinitions[0].ActualWidth;
            connControl.PdfColumnRightEdge = pdfColumnWidth;
            connControl.MinerUColumnLeftEdge = pdfColumnWidth + 4;
        }
        else if (connControl.Bounds.Width > 0)
        {
            connControl.PdfColumnRightEdge = connControl.Bounds.Width * 0.4;
            connControl.MinerUColumnLeftEdge = connControl.Bounds.Width * 0.6;
        }
    }
}