using Caly.Core.Models;
using Caly.Core.Services;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Caly.Tests;

/// <summary>
/// Unit tests for the deterministic Popo-tree → Jiulu draft-plan mapping, the
/// draft-response id extraction, and the MCP call wrappers' error propagation.
/// Tool names/args follow the live jiulu_mcp tools/list (2026-11-16).
/// </summary>
public class JiuluPublisherTests
{
    private static AnalysisTreeNode Node(string type, string title, string content, int level = 0,
        params AnalysisTreeNode[] children)
    {
        var node = new AnalysisTreeNode { Type = type, Title = title, Content = content, Level = level };
        foreach (var child in children)
            node.Children.Add(child);
        return node;
    }

    private static AnalysisTreeNode NodeOnPages(string type, string title, string content, int start, int end, int level = 0)
    {
        var node = Node(type, title, content, level);
        node.Location.Add(new LocationEntry { Page = start });
        node.Location.Add(new LocationEntry { Page = end });
        return node;
    }

    private static StructureDocument MakeDocument()
    {
        var root = Node("root", "", "", 0,
            NodeOnPages("text", "", "版权页信息", 1, 1),
            Node("title", "第一章 源起", "", 1),
            NodeOnPages("text", "", "正文第一段<|txt_split|>正文第二段", 12, 13),
            Node("title", "1.1 驿路", "", 2),
            Node("text", "", "驿路细节", 13),
            Node("title", "第二章 远行", "", 1),
            NodeOnPages("text", "", "远行正文", 20, 25));
        return new StructureDocument
        {
            DocId = "978-7-000-0000-0",
            ModelName = "mineru",
            TreeRoot = root,
        };
    }

    [Fact]
    public void BuildPlan_SplitsFrontMatterAndChaptersByLevelOneTitles()
    {
        var plan = JiuluPublisher.BuildPlan(MakeDocument(), "测试书");

        Assert.Equal("测试书", plan.DocName);
        Assert.Equal("测试书", plan.BookDraft.Title);
        Assert.Contains("doc_id=978-7-000-0000-0", plan.BookDraft.Description);
        Assert.Equal("版权页信息", plan.BookDraft.ContentMarkdown);

        Assert.Equal(2, plan.Chapters.Count);
        Assert.Equal("第一章 源起", plan.Chapters[0].Title);
        Assert.Equal(0, plan.Chapters[0].SerialNumber);
        Assert.Equal("第二章 远行", plan.Chapters[1].Title);
        Assert.Equal(1, plan.Chapters[1].SerialNumber);

        // Deeper headings stay inside the chapter as markdown; the chapter's own
        // level-1 title node is not repeated in its content.
        Assert.DoesNotContain("# 第一章 源起", plan.Chapters[0].ContentMarkdown);
        Assert.Contains("### 1.1 驿路", plan.Chapters[0].ContentMarkdown);
        Assert.Contains("正文第一段\n正文第二段", plan.Chapters[0].ContentMarkdown);
        Assert.Equal("远行正文", plan.Chapters[1].ContentText);
    }

    [Fact]
    public void BuildPlan_ChapterPageRange_CoversWholeSectionIncludingDescendants()
    {
        var plan = JiuluPublisher.BuildPlan(MakeDocument(), "测试书");

        // Section spans the level-1 title node's subtree: pages 12..13 (title node
        // itself carries no Location).
        Assert.Equal(12, plan.Chapters[0].PageStart);
        Assert.Equal(13, plan.Chapters[0].PageEnd);
        Assert.Equal(20, plan.Chapters[1].PageStart);
        Assert.Equal(25, plan.Chapters[1].PageEnd);
    }

    [Fact]
    public void BuildPlan_NoLevelOneTitles_KeepsSingleWholeDocumentChapter()
    {
        var root = Node("root", "", "", 0,
            NodeOnPages("text", "唯一章节", "整本无章节", 3, 9));
        var document = new StructureDocument { DocId = "d1", ModelName = "mineru", TreeRoot = root };

        var plan = JiuluPublisher.BuildPlan(document, "无章节书");

        Assert.Single(plan.Chapters);
        Assert.Equal("无章节书", plan.Chapters[0].Title);
        Assert.Equal("唯一章节\n整本无章节", plan.Chapters[0].ContentText);
        Assert.Equal(3, plan.Chapters[0].PageStart);
        Assert.Equal(9, plan.Chapters[0].PageEnd);
        // The single chapter's title node is kept in the body (nothing to split on).
        Assert.Contains("唯一章节", plan.Chapters[0].ContentMarkdown);
    }

    [Fact]
    public void BuildPlan_EmptyTree_Throws()
    {
        var document = new StructureDocument { DocId = "d1", ModelName = "mineru" };
        Assert.Throws<InvalidOperationException>(() => JiuluPublisher.BuildPlan(document, "空"));
    }

    [Fact]
    public void BuildPlan_PopoResultShape_TextHeadingsAndNoiseNodes()
    {
        // popo_result.json shape: headings are "text" nodes with Title/Level, and
        // root children are polluted with header/footer/page_number noise nodes.
        var heading = NodeOnPages("text", "序 一", "序言正文第一段", 4, 6, level: 1);
        heading.Children.Add(Node("text", "sub", "小节正文", level: 2));
        var root = Node("root", "测试书", "", -1,
            Node("header", "Page 4 - header", "页眉噪音", level: -1),
            Node("page_number", "4", "4", level: -1),
            NodeOnPages("image", "图 0-1", "", 4, 4),
            heading,
            Node("footer", "Page 6 - footer", "页脚噪音", level: -1),
            NodeOnPages("text", "第二章 远行", "远行正文", 7, 9, level: 1));
        var document = new StructureDocument { DocId = "d2", ModelName = "mineru", TreeRoot = root };

        var plan = JiuluPublisher.BuildPlan(document, "测试书");

        Assert.Equal(2, plan.Chapters.Count);
        Assert.Equal("序 一", plan.Chapters[0].Title);
        Assert.Equal("第二章 远行", plan.Chapters[1].Title);

        // The text heading carries its own body; its title is not repeated.
        Assert.Contains("序言正文第一段", plan.Chapters[0].ContentMarkdown);
        Assert.DoesNotContain("序 一", plan.Chapters[0].ContentMarkdown);
        Assert.Contains("## sub", plan.Chapters[0].ContentMarkdown);
        Assert.Contains("小节正文", plan.Chapters[0].ContentMarkdown);

        // Page-noise nodes never reach any payload.
        Assert.DoesNotContain("页眉噪音", plan.BookDraft.ContentMarkdown);
        Assert.DoesNotContain("页脚噪音", plan.Chapters[0].ContentText);
        Assert.DoesNotContain("页眉噪音", plan.Chapters[1].ContentText);

        // Noise before the first heading is dropped; the real image stays in front matter.
        Assert.Contains("图 0-1", plan.BookDraft.ContentMarkdown);

        Assert.Equal(4, plan.Chapters[0].PageStart);
        Assert.Equal(6, plan.Chapters[0].PageEnd);
    }

    [Fact]
    public void ToArguments_BookDraft_OmitsEmptyOptionals()
    {
        var args = new JiuluBookDraftArgs { Title = "T", Description = "D", Tags = "popo" }.ToArguments();

        Assert.Equal("T", args["title"]);
        Assert.Equal("D", args["description"]);
        Assert.Equal("popo", args["tags"]);
        Assert.False(args.ContainsKey("authorinfo"));
        Assert.False(args.ContainsKey("content_markdown"));
    }

    [Fact]
    public void ToArguments_Chapter_EmitsRequiredAndOptionalKeys()
    {
        var chapter = new JiuluChapterDraftArgs
        {
            Title = "第一章",
            SerialNumber = 2,
            ContentMarkdown = "md",
            ContentText = "txt",
            PageStart = 12,
        };

        var args = chapter.ToArguments(42);

        Assert.Equal(42L, args["book_id"]);
        Assert.Equal("第一章", args["title"]);
        Assert.Equal(2, args["serialnumber"]);
        Assert.Equal("md", args["content_markdown"]);
        Assert.Equal("txt", args["content_text"]);
        Assert.Equal(12, args["page_start"]);
        Assert.False(args.ContainsKey("page_end"));
    }

    [Fact]
    public void SummarizePlan_ReportsTitleChaptersAndPages()
    {
        var plan = JiuluPublisher.BuildPlan(MakeDocument(), "测试书");

        var summary = JiuluPublisher.SummarizePlan(plan);

        Assert.Contains("测试书", summary);
        Assert.Contains("2 个章节草稿", summary);
        Assert.Contains("含前置内容", summary);
        Assert.Contains("第一章 源起（第 12-13 页）", summary);
    }

    [Theory]
    [InlineData("{\"book_id\":42}", true, 42L)]
    [InlineData("{\"book\":{\"id\":9}}", true, 9L)]
    [InlineData("{\"id\":\"3\"}", true, 3L)]
    [InlineData("{\"data\":{\"book\":{\"book_id\":7},\"id\":1}}", true, 7L)]
    [InlineData("draft created", false, null)]
    public void ExtractDraftId_FindsIdLeniently(string json, bool structured, long? expected)
    {
        var result = MakeResult(json, structured);

        Assert.Equal(expected, JiuluPublisher.ExtractDraftId(result));
    }

    [Theory]
    [InlineData("{\"books\":[],\"total\":0}", true, 0)]
    [InlineData("{\"total\":5}", true, 5)]
    [InlineData("{\"books\":[{\"id\":1},{\"id\":2}]}", true, 2)]
    [InlineData("no json here", false, null)]
    public void SearchTotalMatches_ParsesBothShapes(string json, bool structured, int? expected)
    {
        var result = MakeResult(json, structured);

        Assert.Equal(expected, JiuluPublisher.SearchTotalMatches(result));
    }

    [Fact]
    public async Task CreateBookDraftAsync_ErrorResult_ThrowsWithServerText()
    {
        var registry = new RecordingRegistry(new CallToolResult
        {
            IsError = true,
            Content = [new TextContentBlock { Text = "quota exceeded" }],
        });
        var publisher = new JiuluPublisher(registry);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => publisher.CreateBookDraftAsync(new JiuluBookDraftArgs { Title = "T" }));
        Assert.Contains("quota exceeded", ex.Message);
    }

    [Fact]
    public async Task SubmitBookRevisionAsync_SendsBookIdAndMessage()
    {
        var registry = new RecordingRegistry(MakeResult("{\"id\":1}", structured: true));
        var publisher = new JiuluPublisher(registry);

        await publisher.SubmitBookRevisionAsync(42, "送审");

        Assert.Equal(JiuluPublisher.SubmitBookRevisionTool, registry.LastToolName);
        Assert.Equal(42L, registry.LastArguments!["book_id"]);
        Assert.Equal("送审", registry.LastArguments["message"]);
    }

    [Fact]
    public async Task CreateChapterDraftAsync_UsesVerifiedToolNameAndArgs()
    {
        var registry = new RecordingRegistry(MakeResult("{\"id\":1}", structured: true));
        var publisher = new JiuluPublisher(registry);

        await publisher.CreateChapterDraftAsync(42, new JiuluChapterDraftArgs { Title = "A", SerialNumber = 0 });

        Assert.Equal(JiuluPublisher.AddChapterDraftTool, registry.LastToolName);
        Assert.Equal(42L, registry.LastArguments!["book_id"]);
        Assert.Equal("A", registry.LastArguments["title"]);
    }

    // ---------------------------------------------------------------- helpers

    private static CallToolResult MakeResult(string json, bool structured)
    {
        if (structured)
            return new CallToolResult { StructuredContent = JsonDocument.Parse(json).RootElement };
        return new CallToolResult { Content = [new TextContentBlock { Text = json }] };
    }

    private sealed class RecordingRegistry(CallToolResult response) : IMcpRegistryService
    {
        public string? LastToolName { get; private set; }
        public IReadOnlyDictionary<string, object>? LastArguments { get; private set; }

        public IReadOnlyList<McpServerConfig> GetServers() => [];
        public Task<IReadOnlyList<McpClientTool>> GetToolsAsync(string serverName, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<McpClientTool>>([]);
        public Task<McpClientTool?> FindToolAsync(string serverName, string toolName, CancellationToken ct = default)
            => Task.FromResult<McpClientTool?>(null);
        public Task<string?> TestConnectionAsync(string serverName, CancellationToken ct = default)
            => Task.FromResult<string?>(null);
        public void Invalidate(string serverName) { }
        public Task<CallToolResult> CallToolAsync(string serverName, string toolName,
            IReadOnlyDictionary<string, object> arguments, CancellationToken ct = default)
        {
            LastToolName = toolName;
            LastArguments = arguments;
            return Task.FromResult(response);
        }
    }
}
