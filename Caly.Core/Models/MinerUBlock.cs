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
    private int _id;
    public int Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    /// <summary>
    /// Unique block identifier from MinerU middle.json (UUID string).
    /// Used as the primary alignment key between PDF overlay and MinerU Blocks column.
    /// Each block in preproc_blocks, para_blocks, and discarded_blocks has a unique block_id.
    /// The para_blocks also contain a block_ids array referencing the source preproc_block IDs.
    /// </summary>
    private string _blockId = string.Empty;
    public string BlockId
    {
        get => _blockId;
        set => SetProperty(ref _blockId, value);
    }

    /// <summary>
    /// Source block IDs referenced by this block (for para_blocks that merge multiple preproc_blocks).
    /// These are the block_id UUIDs from the preproc_blocks that were merged into this para_block.
    /// Used for cross-highlighting between MinerU Blocks column and PDF overlay.
    /// </summary>
    private List<string> _sourceBlockIds = new();
    public List<string> SourceBlockIds
    {
        get => _sourceBlockIds;
        set => SetProperty(ref _sourceBlockIds, value);
    }

    /// <summary>
    /// Page number (1-based).
    /// </summary>
    private int _page;
    public int Page
    {
        get => _page;
        set => SetProperty(ref _page, value);
    }

    /// <summary>
    /// Bounding box [x1, y1, x2, y2] with normalized coordinates (0-1).
    /// </summary>
    private Rect _bbox;
    public Rect Bbox
    {
        get => _bbox;
        set => SetProperty(ref _bbox, value);
    }

    /// <summary>
    /// Whether the Bbox coordinates are normalized (0-1) or in absolute pixels.
    /// Set by the parsing service based on the source data format.
    /// </summary>
    private bool _isBboxNormalized = true;
    public bool IsBboxNormalized
    {
        get => _isBboxNormalized;
        set => SetProperty(ref _isBboxNormalized, value);
    }

    /// <summary>
    /// Block type: title, text, image, table, caption.
    /// </summary>
    private string _type = string.Empty;
    public string Type
    {
        get => _type;
        set => SetProperty(ref _type, value);
    }

    /// <summary>
    /// Block text content.
    /// </summary>
    private string _content = string.Empty;
    public string Content
    {
        get => _content;
        set => SetProperty(ref _content, value);
    }

    /// <summary>
    /// Title level (only valid for title type, 1-6).
    /// </summary>
    private int? _titleLevel;
    public int? TitleLevel
    {
        get => _titleLevel;
        set => SetProperty(ref _titleLevel, value);
    }

    /// <summary>
    /// Original model label (e.g., paragraph_title, image_caption etc.).
    /// </summary>
    private string _sourceLabel = string.Empty;
    public string SourceLabel
    {
        get => _sourceLabel;
        set => SetProperty(ref _sourceLabel, value);
    }

    /// <summary>
    /// Popo type (for downstream processing).
    /// </summary>
    private string _blockType = string.Empty;
    public string BlockType
    {
        get => _blockType;
        set => SetProperty(ref _blockType, value);
    }

    /// <summary>
    /// Inference field: continuation target block ID.
    /// </summary>
    private int _contd = -1;
    public int Contd
    {
        get => _contd;
        set => SetProperty(ref _contd, value);
    }

    /// <summary>
    /// Inference field: title level.
    /// </summary>
    private int _level = -1;
    public int Level
    {
        get => _level;
        set => SetProperty(ref _level, value);
    }

    /// <summary>
    /// Inference field: associated image/table block ID.
    /// </summary>
    private int _image = -1;
    public int Image
    {
        get => _image;
        set => SetProperty(ref _image, value);
    }

    /// <summary>
    /// Inference field: table merge target ID.
    /// </summary>
    private int _tableMerge = -1;
    public int TableMerge
    {
        get => _tableMerge;
        set => SetProperty(ref _tableMerge, value);
    }

    /// <summary>
    /// Original tree node block IDs from AnalysisTreeNode.BlockIds.
    /// Populated by FlattenTreeNodes when building from tree structure.
    /// Used for tree node ↔ block overlay cross-highlighting.
    /// </summary>
    private List<int> _originalBlockIds = new();
    public List<int> OriginalBlockIds
    {
        get => _originalBlockIds;
        set => SetProperty(ref _originalBlockIds, value);
    }

    /// <summary>
    /// Block source indicator: "para" (from para_blocks), "discarded" (from discarded_blocks),
    /// or empty string (from preproc_blocks or unknown source).
    /// Used in MinerU Blocks column to show whether the block was adopted or discarded.
    /// </summary>
    private string _blockSource = string.Empty;
    public string BlockSource
    {
        get => _blockSource;
        set => SetProperty(ref _blockSource, value);
    }

    /// <summary>
    /// Destination type for preproc_blocks: "para" (merged into para_blocks),
    /// "discarded" (placed in discarded_blocks), or empty string (could not be matched).
    /// Used in PDF overlay to color-code block fate.
    /// </summary>
    private string _destinationType = string.Empty;
    public string DestinationType
    {
        get => _destinationType;
        set => SetProperty(ref _destinationType, value);
    }

    /// <summary>
    /// Related block IDs (UUID strings) for cross-highlighting between MinerU Blocks column and PDF overlay.
    /// - For para_blocks: contains the preproc_block BlockIds that were merged into this paragraph
    /// - For preproc_blocks: contains the para_block/discarded_block BlockId it belongs to
    /// </summary>
    private List<string> _relatedBlockIds = new();
    public List<string> RelatedBlockIds
    {
        get => _relatedBlockIds;
        set => SetProperty(ref _relatedBlockIds, value);
    }

    /// <summary>
    /// Whether this block's match to its target was a fallback match (not a precise 1:1 index-based match).
    /// - false: precise match (sub-block index exactly corresponds to block_ids index)
    /// - true: fallback match (e.g., sub-block count != block_ids count, or matched via bbox overlap)
    /// Used in connection lines rendering to draw fallback connections with a lighter/faded color.
    /// </summary>
    private bool _isFallbackMatch;
    public bool IsFallbackMatch
    {
        get => _isFallbackMatch;
        set => SetProperty(ref _isFallbackMatch, value);
    }

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