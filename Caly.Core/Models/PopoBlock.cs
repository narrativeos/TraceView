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
using CommunityToolkit.Mvvm.ComponentModel;

namespace Caly.Core.Models;

/// <summary>
/// Represents a normalized block from MinerU-Popo processing.
/// Corresponds to the output of label_normalization.py / inference.py.
/// </summary>
public partial class PopoBlock : ObservableObject
{
    /// <summary>
    /// Block unique ID, format: integer order.
    /// </summary>
    [ObservableProperty]
    private int _id;

    /// <summary>
    /// Page number (1-based).
    /// </summary>
    [ObservableProperty]
    private int _page;

    /// <summary>
    /// Bounding box [x1, y1, x2, y2] with normalized coordinates (0-1).
    /// </summary>
    [ObservableProperty]
    private Rect _bbox;

    /// <summary>
    /// Block type: title, text, image, table, caption.
    /// </summary>
    [ObservableProperty]
    private string _type = string.Empty;

    /// <summary>
    /// Block text content.
    /// </summary>
    [ObservableProperty]
    private string _content = string.Empty;

    /// <summary>
    /// Title level (only valid for title type, 1-6).
    /// </summary>
    [ObservableProperty]
    private int? _titleLevel;

    /// <summary>
    /// Original model label (e.g., paragraph_title, image_caption etc.).
    /// </summary>
    [ObservableProperty]
    private string _sourceLabel = string.Empty;

    /// <summary>
    /// Popo type (for downstream processing).
    /// </summary>
    [ObservableProperty]
    private string _popoType = string.Empty;

    /// <summary>
    /// Inference field: continuation target block ID.
    /// </summary>
    [ObservableProperty]
    private int _contd = -1;

    /// <summary>
    /// Inference field: title level.
    /// </summary>
    [ObservableProperty]
    private int _level = -1;

    /// <summary>
    /// Inference field: associated image/table block ID.
    /// </summary>
    [ObservableProperty]
    private int _image = -1;

    /// <summary>
    /// Inference field: table merge target ID.
    /// </summary>
    [ObservableProperty]
    private int _tableMerge = -1;

    /// <summary>
    /// Computed property: returns color based on Type.
    /// </summary>
    public Color TypeColor => Type switch
    {
        "title" => Colors.Blue,
        "text" => Colors.Green,
        "image" => Colors.Orange,
        "table" => Colors.Purple,
        "caption" => Colors.Gray,
        _ => Colors.LightGray
    };
}