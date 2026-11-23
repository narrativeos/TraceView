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

// The models below mirror the *real* jiulu_mcp inputSchemas, verified against the
// live tools/list on 2026-11-16 (server: Jiulu Wenhua Spatiotemporal Knowledge
// Hub MCP v2.0.0). The hub has no single "publish" tool: content is pushed as
// drafts (book, then chapters) and enters the platform's manual audit queue via
// submit_publications_book_revision.

/// <summary>
/// Arguments for add_publications_book_draft. Only title is required;
/// optional fields are omitted from the call when empty.
/// </summary>
public sealed class JiuluBookDraftArgs
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? AuthorInfo { get; set; }

    /// <summary>Front matter or whole-book markdown content.</summary>
    public string? ContentMarkdown { get; set; }

    /// <summary>Comma separated tags.</summary>
    public string? Tags { get; set; }

    public Dictionary<string, object> ToArguments()
    {
        var args = new Dictionary<string, object> { ["title"] = Title };
        AddIfPresent(args, "description", Description);
        AddIfPresent(args, "authorinfo", AuthorInfo);
        AddIfPresent(args, "content_markdown", ContentMarkdown);
        AddIfPresent(args, "tags", Tags);
        return args;
    }

    internal static void AddIfPresent(IDictionary<string, object> args, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
            args[name] = value!;
    }
}

/// <summary>
/// Arguments for add_corpus_chapter_draft (a chapter under a book). The hub's
/// section tool hangs under articles, not chapters, so deeper TOC levels are
/// carried inside the chapter markdown as headings.
/// </summary>
public sealed class JiuluChapterDraftArgs
{
    public string Title { get; set; } = string.Empty;
    public int SerialNumber { get; set; }
    public string? ContentMarkdown { get; set; }
    public string? ContentText { get; set; }

    /// <summary>Start page in the original book (optional).</summary>
    public int? PageStart { get; set; }

    /// <summary>End page in the original book (optional).</summary>
    public int? PageEnd { get; set; }

    public Dictionary<string, object> ToArguments(long bookId)
    {
        var args = new Dictionary<string, object>
        {
            ["book_id"] = bookId,
            ["title"] = Title,
            ["serialnumber"] = SerialNumber,
        };
        JiuluBookDraftArgs.AddIfPresent(args, "content_markdown", ContentMarkdown);
        JiuluBookDraftArgs.AddIfPresent(args, "content_text", ContentText);
        if (PageStart is { } pageStart)
            args["page_start"] = pageStart;
        if (PageEnd is { } pageEnd)
            args["page_end"] = pageEnd;
        return args;
    }
}

/// <summary>
/// The complete local push plan: one book draft plus its chapter drafts, in
/// reading order. Built deterministically from the Popo tree; the user approves
/// the whole plan before anything touches the hub.
/// </summary>
public sealed class JiuluPushPlan
{
    public string DocName { get; set; } = string.Empty;
    public JiuluBookDraftArgs BookDraft { get; set; } = new();
    public List<JiuluChapterDraftArgs> Chapters { get; set; } = [];
}
