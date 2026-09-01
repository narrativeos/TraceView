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
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Caly.Core.Models;

/// <summary>
/// Represents a node in the hierarchical tree structure from MinerU-Popo processing.
/// Corresponds to the output of get_json_tree.py.
/// </summary>
public partial class AnalysisTreeNode : ObservableObject
{
    /// <summary>
    /// Node type: root, text, image, table, etc.
    /// </summary>
    [ObservableProperty]
    private string _type = string.Empty;

    /// <summary>
    /// Node title (for text nodes, the section title).
    /// </summary>
    [ObservableProperty]
    private string _title = string.Empty;

    /// <summary>
    /// Metadata (e.g., image_footnote content).
    /// </summary>
    [ObservableProperty]
    private string _metadata = string.Empty;

    /// <summary>
    /// Aggregated content (multiple block contents joined by <|txt_split|>).
    /// </summary>
    [ObservableProperty]
    private string _content = string.Empty;

    /// <summary>
    /// Title level (1-6, root is 0).
    /// </summary>
    [ObservableProperty]
    private int _level;

    /// <summary>
    /// Location list: each original block's bbox + page.
    /// </summary>
    [ObservableProperty]
    private List<LocationEntry> _location = new();

    /// <summary>
    /// All original block IDs that compose this node.
    /// </summary>
    [ObservableProperty]
    private List<int> _blockIds = new();

    /// <summary>
    /// Source block IDs (UUID strings) from MinerU middle.json that compose this node.
    /// Used for tracing back to the original MinerU blocks for cross-referencing.
    /// Corresponds to the source_block_ids field in popo_result.json.
    /// </summary>
    [ObservableProperty]
    private List<string> _sourceBlockIds = new();

    /// <summary>
    /// Image path returned by the Popo API (relative path like "images/xxx.jpg").
    /// Only populated for image-type nodes when loaded from popo_result.json.
    /// </summary>
    [ObservableProperty]
    private string _imgPath = string.Empty;

    /// <summary>
    /// Unique node identifier (UUID) returned by the Popo API.
    /// </summary>
    public string NodeId { get; set; } = string.Empty;

    /// <summary>
    /// Column headers for table_data nodes (null for other node types).
    /// Each header carries name plus optional group/unit from the newer
    /// Popo object-form headers.
    /// </summary>
    public List<TableHeader>? Headers { get; set; }

    /// <summary>
    /// Structured data rows for table_data nodes (null for other node types).
    /// </summary>
    public List<TableDataRow>? Rows { get; set; }

    /// <summary>
    /// Key-value pairs for table_key_value nodes (null for other node types).
    /// </summary>
    public List<TableKeyValuePair>? Pairs { get; set; }

    /// <summary>
    /// Parsed HTML table grid for legacy table nodes (rows of cell texts).
    /// Populated when a table node's content contains an HTML &lt;table&gt;
    /// that was parsed by HtmlTableParser; null for all other nodes.
    /// </summary>
    public List<List<string>>? GridRows { get; set; }

    /// <summary>
    /// Node-level table of contents (in-table sections for table nodes,
    /// null for nodes without a node-level toc).
    /// </summary>
    public List<TocEntry>? Toc { get; set; }

    /// <summary>
    /// Ordered lists detected in this node's content by the Popo API
    /// (typically text nodes). Each entry maps its items to a segment
    /// range of the content (split on &lt;|txt_split|&gt;).
    /// Null or empty for nodes without ordered lists.
    /// Corresponds to the ordered_lists field in popo_result.json.
    /// </summary>
    public List<OrderedListEntry>? OrderedLists { get; set; }

    /// <summary>
    /// Child nodes list (recursive).
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<AnalysisTreeNode> _children = new();

    /// <summary>
    /// Whether the node is expanded (for UI collapse).
    /// </summary>
    [ObservableProperty]
    private bool _isExpanded = true;

    /// <summary>
    /// Whether the node is selected (for highlighting).
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// Computed property: child count (excluded from JSON serialization).
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int ChildCount => Children.Count;

    /// <summary>
    /// Creates a flat list of all block IDs in this node and its descendants.
    /// </summary>
    public List<int> GetAllBlockIds()
    {
        var allIds = new List<int>(BlockIds);
        foreach (var child in Children)
        {
            allIds.AddRange(child.GetAllBlockIds());
        }
        return allIds;
    }
}

/// <summary>
/// Represents a location entry with bounding box and page number.
/// </summary>
public sealed class LocationEntry
{
    public Rect Bbox { get; set; }
    public int Page { get; set; }

    /// <summary>
    /// Row range [start, end] within the source table.
    /// Only populated for structured table sub-nodes (table_data / table_key_value).
    /// </summary>
    public int[]? RowRange { get; set; }
}