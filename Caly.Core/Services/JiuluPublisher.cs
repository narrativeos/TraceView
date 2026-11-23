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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Caly.Core.Models;
using ModelContextProtocol.Protocol;

namespace Caly.Core.Services;

/// <summary>
/// Deterministic publisher of Popo results to the Jiulu spatiotemporal knowledge
/// hub over MCP. The hub's write model is draft + manual audit: one
/// add_publications_book_draft, one add_corpus_chapter_draft per section, then
/// submit_publications_book_revision to enter the approval queue. Tree → plan
/// mapping is plain code (testable in Caly.Tests); the agent never iterates
/// blocks itself.
/// </summary>
/// <remarks>
/// Tool names and argument shapes below were verified against the live
/// jiulu_mcp tools/list on 2026-11-16. Draft response shapes (where the new
/// book id lands) are parsed leniently because they were not observable
/// without a real write.
/// </remarks>
internal sealed partial class JiuluPublisher
{
    /// <summary>Registry name of the Jiulu server entry in settings.</summary>
    public const string ServerName = "jiulu_mcp";

    public const string SearchBooksTool = "search_publications_books";
    public const string AddBookDraftTool = "add_publications_book_draft";
    public const string AddChapterDraftTool = "add_corpus_chapter_draft";
    public const string SubmitBookRevisionTool = "submit_publications_book_revision";

    private readonly IMcpRegistryService _registry;

    public JiuluPublisher(IMcpRegistryService registry)
    {
        _registry = registry;
    }

    // ------------------------------------------------------------- plan building

    /// <summary>
    /// Pure mapping from the Popo tree to a push plan. Root children are split in
    /// reading order: nodes before the first section heading become front matter
    /// (book content_markdown); each heading at the shallowest heading level opens
    /// a chapter, deeper headings stay inside the chapter markdown. With no
    /// headings at all the whole document becomes a single chapter.
    /// Page-noise nodes (header/footer/page_number) are dropped entirely.
    /// </summary>
    public static JiuluPushPlan BuildPlan(StructureDocument document, string documentName)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.TreeRoot is null || document.TreeRoot.Children.Count == 0)
            throw new InvalidOperationException("The document has no Popo tree; nothing to push to Jiulu.");

        var docName = string.IsNullOrWhiteSpace(documentName) ? document.DocId : documentName;
        var plan = new JiuluPushPlan
        {
            DocName = docName,
            BookDraft = new JiuluBookDraftArgs
            {
                Title = docName,
                Description = BuildDescription(document),
                Tags = "popo",
            },
        };

        var contentChildren = new List<AnalysisTreeNode>();
        foreach (var child in document.TreeRoot.Children)
            if (!IsPageNoise(child.Type))
                contentChildren.Add(child);

        // Headings appear as dedicated "title" nodes (popo.json cache shape) or as
        // "text" nodes carrying the section title (popo_result.json shape). The
        // chapter level is the shallowest heading level present at root depth.
        var chapterLevel = int.MaxValue;
        foreach (var child in contentChildren)
            if (IsHeading(child) && child.Level < chapterLevel)
                chapterLevel = child.Level;

        var frontMatter = new List<AnalysisTreeNode>();
        var sections = new List<List<AnalysisTreeNode>>();
        List<AnalysisTreeNode>? current = null;

        foreach (var child in contentChildren)
        {
            if (chapterLevel != int.MaxValue && IsHeading(child) && child.Level == chapterLevel)
            {
                current = new List<AnalysisTreeNode> { child };
                sections.Add(current);
            }
            else if (current is null)
            {
                frontMatter.Add(child);
            }
            else
            {
                current.Add(child);
            }
        }

        if (frontMatter.Count > 0)
            plan.BookDraft.ContentMarkdown = RenderMarkdown(frontMatter);

        if (sections.Count == 0)
            plan.Chapters.Add(BuildChapter(docName, 0, contentChildren, contentChildren));
        else
        {
            var serial = 0;
            foreach (var section in sections)
            {
                // The heading node names the chapter; its title is not repeated in
                // the body, but a "text" heading's own content and children are.
                plan.Chapters.Add(BuildChapter(section[0].Title, serial++, section, section, section[0]));
            }
        }

        return plan;
    }

    private static bool IsHeading(AnalysisTreeNode node) =>
        node.Type is "title" or "text" &&
        node.Level >= 1 &&
        !string.IsNullOrWhiteSpace(node.Title);

    private static bool IsPageNoise(string type) =>
        type is "header" or "footer" or "page_number";

    /// <summary>
    /// Human-readable plan summary for the confirmation dialog before anything
    /// is sent (which document, how many chapters, page spans, sizes).
    /// </summary>
    public static string SummarizePlan(JiuluPushPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var sb = new StringBuilder();
        sb.Append($"《{plan.DocName}》：1 个书籍草稿 + {plan.Chapters.Count} 个章节草稿");
        if (!string.IsNullOrEmpty(plan.BookDraft.ContentMarkdown))
            sb.Append("（含前置内容）");
        sb.AppendLine();

        var shown = Math.Min(plan.Chapters.Count, 12);
        for (var i = 0; i < shown; i++)
        {
            var chapter = plan.Chapters[i];
            sb.Append($"  {i + 1}. {chapter.Title}");
            if (chapter.PageStart is { } start)
                sb.Append(chapter.PageEnd is { } end ? $"（第 {start}-{end} 页）" : $"（第 {start} 页起）");
            sb.Append($"  {(chapter.ContentMarkdown?.Length ?? 0)} 字");
            sb.AppendLine();
        }

        if (plan.Chapters.Count > shown)
            sb.Append($"  …共 {plan.Chapters.Count} 章");

        return sb.ToString();
    }

    private static string BuildDescription(StructureDocument document) =>
        $"doc_id={document.DocId}；解析模型={document.ModelName}；页数={document.PagesBlocks.Count}；由 Caly Popo 管线生成。";

    private static JiuluChapterDraftArgs BuildChapter(
        string title,
        int serialNumber,
        IReadOnlyList<AnalysisTreeNode> contentNodes,
        IReadOnlyList<AnalysisTreeNode> pageNodes,
        AnalysisTreeNode? headingNode = null)
    {
        var (pageStart, pageEnd) = PageRange(pageNodes);
        return new JiuluChapterDraftArgs
        {
            Title = title,
            SerialNumber = serialNumber,
            ContentMarkdown = RenderMarkdown(contentNodes, headingNode),
            ContentText = RenderPlainText(contentNodes, headingNode),
            PageStart = pageStart,
            PageEnd = pageEnd,
        };
    }

    // ------------------------------------------------------------- rendering

    /// <param name="headingNode">Chapter heading node: included for its body but
    /// its own title line is suppressed (it already titles the chapter draft).</param>
    private static string RenderMarkdown(IReadOnlyList<AnalysisTreeNode> nodes, AnalysisTreeNode? headingNode = null)
    {
        var sb = new StringBuilder();
        foreach (var node in nodes)
            AppendMarkdown(sb, node, headingNode);
        return sb.ToString().TrimEnd();
    }

    private static void AppendMarkdown(StringBuilder sb, AnalysisTreeNode node, AnalysisTreeNode? headingNode = null)
    {
        if (IsPageNoise(node.Type))
            return;

        var isHeadingNode = ReferenceEquals(node, headingNode);
        var content = node.Content ?? string.Empty;
        switch (node.Type)
        {
            case "title":
                // Body headings never reach level 1 — that level names the chapter.
                if (!isHeadingNode)
                {
                    var level = Math.Clamp(node.Level + 1, 2, 6);
                    sb.AppendLine(new string('#', level) + " " + node.Title);
                    sb.AppendLine();
                }
                break;
            case "text":
                if (!isHeadingNode && !string.IsNullOrEmpty(node.Title))
                    sb.AppendLine("## " + node.Title);
                // Ordered-list nodes render from their segments; plain content otherwise.
                var ordered = PopoJsonService.GenerateOrderedListMarkdown(node);
                sb.AppendLine(string.IsNullOrEmpty(ordered) ? content.Replace("<|txt_split|>", "\n") : ordered);
                sb.AppendLine();
                break;
            case "image":
                sb.AppendLine($"![{node.Title}]({node.ImgPath ?? "image_placeholder"})");
                if (!string.IsNullOrEmpty(node.Metadata))
                    sb.AppendLine($"*图注: {node.Metadata}*");
                sb.AppendLine();
                break;
            case "image_footnote":
                sb.AppendLine($"*图注: {content}*");
                sb.AppendLine();
                break;
            case "caption":
                sb.AppendLine($"*{content}*");
                sb.AppendLine();
                break;
            case "table":
                // Structured children render the grid; avoid duplicating the raw HTML.
                if (!isHeadingNode && !string.IsNullOrEmpty(node.Title))
                    sb.AppendLine($"### {node.Title}");
                if (!node.Children.Any(c => c.Type is "table_data" or "table_key_value"))
                {
                    var grid = PopoJsonService.GenerateTableMarkdown(node);
                    sb.AppendLine(string.IsNullOrEmpty(grid) ? content : grid);
                    sb.AppendLine();
                }
                break;
            case "table_data":
            case "table_key_value":
                if (!string.IsNullOrEmpty(node.Title))
                    sb.AppendLine($"#### {node.Title}");
                sb.AppendLine(PopoJsonService.GenerateTableMarkdown(node));
                break;
            default:
                if (!string.IsNullOrEmpty(content))
                {
                    sb.AppendLine(content);
                    sb.AppendLine();
                }
                break;
        }

        foreach (var child in node.Children)
            AppendMarkdown(sb, child, headingNode);
    }

    private static string RenderPlainText(IReadOnlyList<AnalysisTreeNode> nodes, AnalysisTreeNode? headingNode = null)
    {
        var sb = new StringBuilder();
        foreach (var node in nodes)
            AppendPlainText(sb, node, headingNode);
        return sb.ToString().TrimEnd();
    }

    private static void AppendPlainText(StringBuilder sb, AnalysisTreeNode node, AnalysisTreeNode? headingNode = null)
    {
        if (IsPageNoise(node.Type))
            return;

        var isHeadingNode = ReferenceEquals(node, headingNode);
        var content = node.Content ?? string.Empty;
        switch (node.Type)
        {
            case "title":
                if (!isHeadingNode && !string.IsNullOrEmpty(node.Title))
                    sb.AppendLine(node.Title);
                break;
            case "text":
                if (!isHeadingNode && !string.IsNullOrEmpty(node.Title))
                    sb.AppendLine(node.Title);
                sb.AppendLine(content.Replace("<|txt_split|>", "\n"));
                break;
            case "image":
                if (!string.IsNullOrEmpty(node.Metadata))
                    sb.AppendLine(node.Metadata);
                break;
            case "table":
                // Keep structured children's own content instead of the raw HTML parent.
                if (!node.Children.Any(c => c.Type is "table_data" or "table_key_value"))
                    sb.AppendLine(NormalizeSpaces(HtmlTags().Replace(content, " ")));
                break;
            default:
                if (!string.IsNullOrEmpty(content))
                    sb.AppendLine(content);
                break;
        }

        foreach (var child in node.Children)
            AppendPlainText(sb, child, headingNode);
    }

    private static (int? Start, int? End) PageRange(IReadOnlyList<AnalysisTreeNode> nodes)
    {
        int? min = null;
        int? max = null;
        foreach (var node in EnumerateSelfAndDescendants(nodes))
        {
            foreach (var location in node.Location)
            {
                if (location.Page <= 0)
                    continue;
                if (min is null || location.Page < min)
                    min = location.Page;
                if (max is null || location.Page > max)
                    max = location.Page;
            }
        }

        return (min, max);
    }

    private static IEnumerable<AnalysisTreeNode> EnumerateSelfAndDescendants(IReadOnlyList<AnalysisTreeNode> roots)
    {
        var stack = new Stack<AnalysisTreeNode>(roots.Reverse());
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;
            foreach (var child in node.Children.Reverse())
                stack.Push(child);
        }
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTags();

    private static string NormalizeSpaces(string value) =>
        NormalizedSpace().Replace(value.Trim(), " ");

    [GeneratedRegex("\\s{2,}")]
    private static partial Regex NormalizedSpace();

    // ------------------------------------------------------------- hub calls

    /// <summary>Read-only duplicate check. The hub's instructions: search first.</summary>
    public async Task<CallToolResult> SearchBooksAsync(string query, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var result = await _registry
            .CallToolAsync(ServerName, SearchBooksTool,
                new Dictionary<string, object> { ["query"] = query, ["limit"] = 10 }, ct)
            .ConfigureAwait(false);

        EnsureSuccess(result, SearchBooksTool);
        return result;
    }

    /// <summary>Creates the book draft. Remote write: requires user confirmation.</summary>
    public async Task<CallToolResult> CreateBookDraftAsync(JiuluBookDraftArgs bookDraft, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(bookDraft);

        var result = await _registry
            .CallToolAsync(ServerName, AddBookDraftTool, bookDraft.ToArguments(), ct)
            .ConfigureAwait(false);

        EnsureSuccess(result, AddBookDraftTool);
        return result;
    }

    /// <summary>Creates one chapter draft under an existing book. Remote write.</summary>
    public async Task<CallToolResult> CreateChapterDraftAsync(
        long bookId,
        JiuluChapterDraftArgs chapter,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(chapter);

        var result = await _registry
            .CallToolAsync(ServerName, AddChapterDraftTool, chapter.ToArguments(bookId), ct)
            .ConfigureAwait(false);

        EnsureSuccess(result, AddChapterDraftTool);
        return result;
    }

    /// <summary>
    /// Moves the book into the hub's manual audit queue. The tool reports
    /// destructiveHint=true: always behind an explicit user confirmation.
    /// </summary>
    public async Task<CallToolResult> SubmitBookRevisionAsync(
        long bookId,
        string message,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        var result = await _registry
            .CallToolAsync(ServerName, SubmitBookRevisionTool,
                new Dictionary<string, object> { ["book_id"] = bookId, ["message"] = message }, ct)
            .ConfigureAwait(false);

        EnsureSuccess(result, SubmitBookRevisionTool);
        return result;
    }

    // ------------------------------------------------------------- result parsing

    public static string? ResponseText(CallToolResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        foreach (var block in result.Content)
        {
            if (block is TextContentBlock text)
                return text.Text;
        }

        return null;
    }

    /// <summary>Total hits of a search_publications_books result (null when unparseable).</summary>
    public static int? SearchTotalMatches(CallToolResult result)
    {
        var root = ResponseJson(result);
        if (root is null)
            return null;

        if (TryFindNumber(root.Value, "total", out var total))
            return checked((int)total);

        if (root.Value.ValueKind == JsonValueKind.Object &&
            root.Value.TryGetProperty("books", out var books) &&
            books.ValueKind == JsonValueKind.Array)
            return books.GetArrayLength();

        return null;
    }

    /// <summary>
    /// Lenient extraction of the new entity id from a draft response: BFS the
    /// JSON for "book_id" first, then "id" (covers {"book_id":N}, {"book":{"id":N}},
    /// {"id":N} and plain-text JSON alike).
    /// </summary>
    public static long? ExtractDraftId(CallToolResult result)
    {
        var root = ResponseJson(result);
        if (root is null)
            return null;

        if (TryFindNumber(root.Value, "book_id", out var bookId))
            return bookId;
        if (TryFindNumber(root.Value, "id", out var id))
            return id;
        return null;
    }

    private static void EnsureSuccess(CallToolResult result, string toolName)
    {
        if (result.IsError == true)
            throw new InvalidOperationException($"MCP tool '{toolName}' reported an error: {ResponseText(result)}");
    }

    private static JsonElement? ResponseJson(CallToolResult result)
    {
        if (result.StructuredContent is JsonElement structured)
            return structured;

        var text = ResponseText(result);
        if (string.IsNullOrWhiteSpace(text))
            return null;

        try
        {
            return JsonDocument.Parse(text).RootElement;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryFindNumber(JsonElement root, string key, out long value)
    {
        var queue = new Queue<JsonElement>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var element = queue.Dequeue();
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    if (element.TryGetProperty(key, out var candidate) && TryGetInt64(candidate, out value))
                        return true;
                    foreach (var property in element.EnumerateObject())
                        queue.Enqueue(property.Value);
                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                        queue.Enqueue(item);
                    break;
            }
        }

        value = 0;
        return false;
    }

    private static bool TryGetInt64(JsonElement element, out long value)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number when element.TryGetInt64(out value):
                return true;
            case JsonValueKind.Number:
                var doubleValue = element.GetDouble();
                value = (long)doubleValue;
                return value == doubleValue;
            case JsonValueKind.String when long.TryParse(element.GetString(), out value):
                return true;
            default:
                value = 0;
                return false;
        }
    }
}
