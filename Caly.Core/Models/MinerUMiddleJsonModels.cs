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

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Caly.Core.Models;

/// <summary>
/// Represents a single block from MinerU middle.json pages section.
/// This is the raw format before mapping to MinerUBlock.
/// </summary>
public class MinerUMiddlePageBlock
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>
    /// Unique block identifier from MinerU middle.json (UUID string).
    /// Used as the primary alignment key between PDF overlay and MinerU Blocks column.
    /// </summary>
    [JsonPropertyName("block_id")]
    public string BlockId { get; set; } = string.Empty;

    /// <summary>
    /// Source block IDs referenced by this block (for para_blocks that merge multiple preproc_blocks).
    /// These are the block_id UUIDs from the preproc_blocks that were merged into this para_block.
    /// </summary>
    [JsonPropertyName("block_ids")]
    public List<string> SourceBlockIds { get; set; } = new();

    [JsonPropertyName("page")]
    public int Page { get; set; }

    /// <summary>
    /// Block type from MinerU (may differ from MinerUBlock.Type).
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Bounding box as [x1, y1, x2, y2].
    /// May be absolute pixels or normalized coordinates depending on MinerU version.
    /// </summary>
    [JsonPropertyName("bbox")]
    public double[] Bbox { get; set; } = new double[4];

    /// <summary>
    /// Text content of the block.
    /// </summary>
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Original model label (e.g., paragraph, paragraph_title, figure, table, etc.).
    /// </summary>
    [JsonPropertyName("source_label")]
    public string SourceLabel { get; set; } = string.Empty;

    /// <summary>
    /// Continuation target block ID (-1 if none).
    /// </summary>
    [JsonPropertyName("contd")]
    public int Contd { get; set; } = -1;

    /// <summary>
    /// Title level (1-6 for titles, -1 for others).
    /// </summary>
    [JsonPropertyName("level")]
    public int Level { get; set; } = -1;

    /// <summary>
    /// Associated image/table block ID (-1 if none).
    /// </summary>
    [JsonPropertyName("image")]
    public int Image { get; set; } = -1;

    /// <summary>
    /// Table merge target ID (-1 if none).
    /// </summary>
    [JsonPropertyName("table_merge")]
    public int TableMerge { get; set; } = -1;

    /// <summary>
    /// Block source indicator: "para" (from para_blocks/adopted), 
    /// "discarded" (from discarded_blocks/rejected), or empty (unknown).
    /// NOT serialized to JSON - set during parsing.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string BlockSource { get; set; } = string.Empty;

    /// <summary>
    /// Destination type for preproc_blocks: "adopted" (merged into para_blocks),
    /// "discarded" (placed in discarded_blocks), or "unknown" (could not be matched).
    /// NOT serialized to JSON - set during parsing.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string DestinationType { get; set; } = string.Empty;

    /// <summary>
    /// Related block IDs for cross-highlighting between MinerU Blocks column and PDF overlay.
    /// - For para_blocks: contains the preproc_block IDs that were merged into this paragraph
    /// - For preproc_blocks: contains the para_block/discarded_block ID it belongs to
    /// NOT serialized to JSON - set during parsing.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public System.Collections.Generic.List<int> RelatedBlockIds { get; set; } = new();

    /// <summary>
    /// Whether this block's match to its target was a fallback match (not a precise 1:1 index-based match).
    /// - false: precise match (sub-block index exactly corresponds to block_ids index)
    /// - true: fallback match (e.g., sub-block count != block_ids count, or matched via bbox overlap)
    /// Used in connection lines rendering to draw fallback connections with a lighter/faded color.
    /// NOT serialized to JSON - set during parsing.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsFallbackMatch { get; set; }
}

/// <summary>
/// Location entry in MinerU tree node.
/// </summary>
public class MinerUMiddleLocation
{
    [JsonPropertyName("bbox")]
    public double[] Bbox { get; set; } = new double[4];

    [JsonPropertyName("page")]
    public int Page { get; set; }
}

/// <summary>
/// Tree node from MinerU middle.json tree section.
/// </summary>
public class MinerUMiddleTreeNode
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("metadata")]
    public string Metadata { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("level")]
    public int Level { get; set; }

    [JsonPropertyName("location")]
    public List<MinerUMiddleLocation> Location { get; set; } = new();

    [JsonPropertyName("block_ids")]
    public List<int> BlockIds { get; set; } = new();

    [JsonPropertyName("children")]
    public List<MinerUMiddleTreeNode> Children { get; set; } = new();
}

/// <summary>
/// Complete MinerU middle.json structure.
/// Contains pages (flat block list) and tree (hierarchical structure).
/// </summary>
public class MinerUMiddleJson
{
    [JsonPropertyName("doc_id")]
    public string DocId { get; set; } = string.Empty;

    [JsonPropertyName("model_name")]
    public string ModelName { get; set; } = "mineru";

    /// <summary>
    /// Pages dictionary: key is page number (string), value is block array.
    /// </summary>
    [JsonPropertyName("pages")]
    public Dictionary<string, List<MinerUMiddlePageBlock>> Pages { get; set; } = new();

    /// <summary>
    /// Optional tree structure from MinerU.
    /// </summary>
    [JsonPropertyName("tree")]
    public MinerUMiddleTreeNode? Tree { get; set; }

    /// <summary>
    /// Page dimensions for coordinate normalization (if available).
    /// Key is page number, value is [width, height].
    /// </summary>
    [JsonPropertyName("page_size")]
    public Dictionary<string, double[]> PageSize { get; set; } = new();
}