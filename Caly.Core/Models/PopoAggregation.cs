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

namespace Caly.Core.Models;

/// <summary>
/// Types of aggregation relationships between blocks.
/// </summary>
public enum AggregationType
{
    /// <summary>
    /// Text continuation: multiple text blocks judged as continuous text (contd chain).
    /// </summary>
    Continuation,

    /// <summary>
    /// Title aggregation: blocks aggregated under the same title level.
    /// </summary>
    Level,

    /// <summary>
    /// Caption association: image + image_caption linked.
    /// </summary>
    Caption,

    /// <summary>
    /// Footnote association: table + table_footnote linked.
    /// </summary>
    Footnote,

    /// <summary>
    /// Table merge: cross-page table merge.
    /// </summary>
    TableMerge,

    /// <summary>
    /// Supplement: header/footer etc. attached to root node.
    /// </summary>
    Supplement
}

/// <summary>
/// Represents an aggregation relationship between source blocks and a target tree node.
/// </summary>
public sealed class PopoAggregation
{
    /// <summary>
    /// Type of aggregation relationship.
    /// </summary>
    public AggregationType Type { get; set; }

    /// <summary>
    /// Source block IDs that are aggregated.
    /// </summary>
    public List<int> SourceBlockIds { get; set; } = new();

    /// <summary>
    /// Target tree node reference (optional, for display purposes).
    /// </summary>
    public PopoTreeNode? TargetNode { get; set; }

    /// <summary>
    /// Label text for the aggregation line annotation.
    /// </summary>
    public string LabelText { get; set; } = string.Empty;
}