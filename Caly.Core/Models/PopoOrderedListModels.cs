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
/// A single numbered item of an ordered list detected by the Popo API.
/// The text is the list item content with the leading number stripped;
/// it may contain inline HTML tags such as &lt;sup&gt; / &lt;sub&gt;.
/// </summary>
public sealed class OrderedListItem
{
    /// <summary>
    /// Item number as it appears in the source document (usually 1-based).
    /// </summary>
    public int Num { get; set; }

    /// <summary>
    /// Item text (may contain inline HTML tags like &lt;sup&gt;/&lt;sub&gt;).
    /// </summary>
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// One ordered list detected within a node's content by the Popo API.
/// A node (typically a text node) may contain several ordered lists
/// interleaved with plain paragraphs.
/// Corresponds to one element of the "ordered_lists" array in popo_result.json.
/// </summary>
public sealed class OrderedListEntry
{
    /// <summary>
    /// The numbered items of this list, in document order.
    /// </summary>
    public List<OrderedListItem> Items { get; set; } = new();

    /// <summary>
    /// Segment range [start, end] (0-based, inclusive) of this list's items
    /// within the node's content, where content is split on &lt;|txt_split|&gt;.
    /// Null when the API did not report a range.
    /// </summary>
    public int[]? SegmentRange { get; set; }

    /// <summary>
    /// Whether the list numbering starts at one.
    /// </summary>
    public bool StartsAtOne { get; set; } = true;
}

/// <summary>
/// One display line of a node's structured content (content rendered with
/// its ordered lists resolved). Either a plain paragraph (Number is null)
/// or a numbered list item (Number is set).
/// </summary>
public sealed class ContentDisplayLine
{
    /// <summary>
    /// Item number for list items (null for plain paragraphs).
    /// </summary>
    public string? Number { get; init; }

    /// <summary>
    /// Line text with inline HTML tags stripped.
    /// </summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// Whether this line is a numbered list item.
    /// </summary>
    public bool IsListItem => Number is not null;

    /// <summary>
    /// Whether this line is the first item of an ordered list that does not
    /// start at the very beginning of the content (used to render a subtle
    /// separator between a preceding paragraph and the list).
    /// </summary>
    public bool IsListStart { get; init; }
}