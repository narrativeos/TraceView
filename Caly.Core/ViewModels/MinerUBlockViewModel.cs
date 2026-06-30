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

using Caly.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Caly.Core.ViewModels;

/// <summary>
/// View model for displaying a raw MinerU block in the middle column of the three-column layout.
/// Wraps MinerUMiddlePageBlock for UI binding.
/// </summary>
public partial class MinerUBlockViewModel : ObservableObject
{
    private readonly MinerUMiddlePageBlock _block;

    public MinerUBlockViewModel(MinerUMiddlePageBlock block)
    {
        _block = block;
    }

    public int Id => _block.Id;
    public int Page => _block.Page;
    public string Type => _block.Type;
    public string Content => _block.Content;
    public string SourceLabel => _block.SourceLabel;
    public int Contd => _block.Contd;
    public int Level => _block.Level;
    public int Image => _block.Image;
    public double[] Bbox => _block.Bbox;

    /// <summary>
    /// Gets a short preview of the content for display in the list.
    /// </summary>
    public string ContentPreview
    {
        get
        {
            if (string.IsNullOrEmpty(Content))
                return "[empty]";
            return Content.Length > 100 ? Content.Substring(0, 100) + "..." : Content;
        }
    }

    /// <summary>
    /// Gets a display label combining type and source_label.
    /// </summary>
    public string TypeLabel
    {
        get
        {
            var parts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(Type))
                parts.Add(Type);
            if (!string.IsNullOrEmpty(SourceLabel) && SourceLabel != Type)
                parts.Add(SourceLabel);
            return string.Join(" / ", parts);
        }
    }

    /// <summary>
    /// Gets a color key for the block type (used for visual distinction).
    /// </summary>
    public string TypeColorKey
    {
        get
        {
            return Type.ToLowerInvariant() switch
            {
                "text" or "paragraph" => "Blue",
                "title" or "heading" => "Purple",
                "image" or "figure" => "Green",
                "table" => "Orange",
                "caption" => "Gray",
                _ => "Default"
            };
        }
    }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isExpanded;
}