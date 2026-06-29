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
using Avalonia.Media;
using Caly.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Caly.Core.ViewModels;

/// <summary>
/// View model for displaying a single Popo block in the block list view.
/// </summary>
public partial class BlockViewModel : ObservableObject
{
    private readonly PopoBlock _block;

    public BlockViewModel(PopoBlock block)
    {
        _block = block;
    }

    public int Id => _block.Id;
    public int Page => _block.Page;
    public Rect Bbox => _block.Bbox;
    public string Type => _block.Type;
    public string Content => _block.Content;
    public int? TitleLevel => _block.TitleLevel;
    public string SourceLabel => _block.SourceLabel;
    public int Contd => _block.Contd;
    public int Level => _block.Level;
    public int Image => _block.Image;
    public int TableMerge => _block.TableMerge;

    public Color TypeColor => _block.TypeColor;

    /// <summary>
    /// Display label for the block (e.g., "1:title" or "3:text").
    /// </summary>
    public string DisplayLabel => $"{Id}:{Type}";

    /// <summary>
    /// Bbox display string.
    /// </summary>
    public string BboxDisplay => $"[{Bbox.X:F3}, {Bbox.Y:F3}, {Bbox.Right:F3}, {Bbox.Bottom:F3}]";

    /// <summary>
    /// Truncated content for display (max 100 chars).
    /// </summary>
    public string DisplayContent
    {
        get
        {
            if (Content.Length <= 100)
                return Content;
            return Content.Substring(0, 100) + "...";
        }
    }

    /// <summary>
    /// Whether this block is currently selected/highlighted.
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// Whether this block type is currently visible (based on type filter).
    /// </summary>
    [ObservableProperty]
    private bool _isVisible = true;
}