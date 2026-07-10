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
using System.Collections.Generic;

namespace Caly.Core.Models;

/// <summary>
/// Represents a normalized block from MinerU parsing (via MinerUJsonService).
/// Corresponds to the output of MinerU middle.json after coordinate normalization.
/// </summary>
public partial class MinerUBlock : ObservableObject
{
    /// <summary>
    /// Block unique ID, format: integer order.
    /// </summary>
    [ObservableProperty]
    private int _id;

    /// <summary>
    /// Unique block identifier from MinerU middle.json (UUID string).
    /// Used as the primary alignment key between PDF overlay and MinerU Blocks column.
    /// Each block in preproc_blocks, para_blocks, and discarded_blocks has a unique block_id.
    /// The para_blocks also contain a block_ids array referencing the source preproc_block IDs.
    /// </summary>
    [ObservableProperty]
    private string _blockId = string.Empty;

    /// <summary>
    /// Source block IDs referenced by this block (for para_blocks that merge multiple preproc_blocks).
    /// These are the block_id UUIDs from the preproc_blocks that were merged into this para_block.
    /// Used for cross-highlighting between MinerU Blocks column and PDF overlay.
    /// </summary>
    [ObservableProperty]
    private List<string> _sourceBlockIds = new();

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
    /// Whether the Bbox coordinates are normalized (0-1) or in absolute pixels.
    /// Set by the parsing service based on the source data format.
    /// </summary>
    [ObservableProperty]
    private bool _isBboxNormalized = true;

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
    private string _blockType = string.Empty;

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
    /// Original tree node block IDs from AnalysisTreeNode.BlockIds.
    /// Populated by FlattenTreeNodes when building from tree structure.
    /// Used for tree node ↔ block overlay cross-highlighting.
    /// </summary>
    [ObservableProperty]
    private List<int> _originalBlockIds = new();

    /// <summary>
    /// Block source indicator: "para" (from para_blocks), "discarded" (from discarded_blocks),
    /// or empty string (from preproc_blocks or unknown source).
    /// Used in MinerU Blocks column to show whether the block was adopted or discarded.
    /// </summary>
    [ObservableProperty]
    private string _blockSource = string.Empty;

    /// <summary>
    /// Destination type for preproc_blocks: "para" (merged into para_blocks),
    /// "discarded" (placed in discarded_blocks), or empty string (could not be matched).
    /// Used in PDF overlay to color-code block fate.
    /// </summary>
    [ObservableProperty]
    private string _destinationType = string.Empty;

    /// <summary>
    /// Related block IDs for cross-highlighting between MinerU Blocks column and PDF overlay.
    /// - For para_blocks: contains the preproc_block IDs that were merged into this paragraph
    /// - For preproc_blocks: contains the para_block/discarded_block ID it belongs to
    /// </summary>
    [ObservableProperty]
    private List<int> _relatedBlockIds = new();

    /// <summary>
    /// Whether this block's match to its target was a fallback match (not a precise 1:1 index-based match).
    /// - false: precise match (sub-block index exactly corresponds to block_ids index)
    /// - true: fallback match (e.g., sub-block count != block_ids count, or matched via bbox overlap)
    /// Used in connection lines rendering to draw fallback connections with a lighter/faded color.
    /// </summary>
    [ObservableProperty]
    private bool _isFallbackMatch;

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