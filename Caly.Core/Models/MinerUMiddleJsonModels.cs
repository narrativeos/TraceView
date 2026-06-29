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
/// This is the raw format before mapping to PopoBlock.
/// </summary>
public class MinerUMiddlePageBlock
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }

    /// <summary>
    /// Block type from MinerU (may differ from PopoBlock.Type).
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