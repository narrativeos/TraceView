using Caly.Core.Models;
using Caly.Core.Services;
using Caly.Core.Services.Agent;
using Caly.Core.Services.Interfaces;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Caly.Tests;

/// <summary>
/// Tests for the TOC re-orchestration call site: patch validation rules, the session
/// tools, run-context plumbing and the deterministic apply step. The LLM is again a
/// scripted in-memory client — no network, no real model.
/// </summary>
public class TocAlignmentTests
{
    private const string DocJsonModel = "mineru";

    private static MinerUBlock Block(int id, int page, string type, string content, string source = "para") =>
        new() { Id = id, BlockId = $"uuid-{id}", Page = page, Type = type, Content = content, BlockSource = source };

    private static StructureDocument MakeDoc()
    {
        var doc = new StructureDocument
        {
            DocId = "978-7-000-0000-0",
            ModelName = DocJsonModel,
            TocEntries =
            [
                new TocEntry { Title = "第一章 源起", Level = 1, Page = 1 },
                new TocEntry { Title = "第二章 远行", Level = 1, Page = 2 },
            ],
        };

        doc.PagesBlocks[1] = [Block(1, 1, "title", "第一章 源起"), Block(2, 1, "text", "驿路三千里。"), Block(3, 1, "header", "页眉噪声", "discarded")];
        doc.PagesBlocks[2] = [Block(4, 2, "title", "第二章 远行"), Block(5, 2, "text", "长安归来。")];
        return doc;
    }

    private static TocAlignmentPatch ValidPatch() => new()
    {
        Nodes =
        [
            new ProposedTocNode { Id = "n1", Title = "第一章 源起", Level = 1, Page = 1 },
            new ProposedTocNode { Id = "n2", Title = "1.1 驿路", Level = 2, Page = 1 },
            new ProposedTocNode { Id = "n3", Title = "第二章 远行", Level = 1, Page = 2 },
        ],
        BlockAssignments =
        [
            new BlockAssignment { BlockId = 1, NodeId = "n1" },
            new BlockAssignment { BlockId = 2, NodeId = "n2" },
            new BlockAssignment { BlockId = 4, NodeId = "n3" },
            new BlockAssignment { BlockId = 5, NodeId = "n3" },
        ],
    };

    // ------------------------------------------------------------- validator

    [Fact]
    public void Valid_patch_passes_with_clean_stats()
    {
        var result = TocAlignmentPatcher.Validate(MakeDoc(), ValidPatch());

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Equal(4, result.TotalBlocks); // 5 blocks, 1 discarded
        Assert.Equal(4, result.AssignedBlocks);
        Assert.Empty(result.UnassignedBlockIds);
    }

    [Fact]
    public void Missing_block_coverage_is_an_error()
    {
        var patch = ValidPatch();
        patch.BlockAssignments.RemoveAll(a => a.BlockId == 5);

        var result = TocAlignmentPatcher.Validate(MakeDoc(), patch);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Block 5", StringComparison.Ordinal));
        Assert.Contains(5, result.UnassignedBlockIds);
    }

    [Fact]
    public void Double_assigned_block_is_an_error()
    {
        var patch = ValidPatch();
        patch.BlockAssignments.Add(new BlockAssignment { BlockId = 2, NodeId = "n1" });

        var result = TocAlignmentPatcher.Validate(MakeDoc(), patch);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("more than once", StringComparison.Ordinal));
    }

    [Fact]
    public void Level_jump_and_bad_page_are_errors()
    {
        var patch = ValidPatch();
        ((ProposedTocNode)patch.Nodes[1]).Level = 4; // 1 -> 4 skips levels
        ((ProposedTocNode)patch.Nodes[2]).Page = 99; // beyond last page

        var result = TocAlignmentPatcher.Validate(MakeDoc(), patch);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("nest at most one level", StringComparison.Ordinal));
        Assert.Contains(result.Errors, e => e.Contains("outside the document range", StringComparison.Ordinal));
    }

    [Fact]
    public void Unknown_block_or_node_references_are_errors()
    {
        var patch = ValidPatch();
        patch.BlockAssignments.Add(new BlockAssignment { BlockId = 777, NodeId = "n1" });
        patch.BlockAssignments.Add(new BlockAssignment { BlockId = 3, NodeId = "ghost" });

        var result = TocAlignmentPatcher.Validate(MakeDoc(), patch);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("unknown block 777", StringComparison.Ordinal));
        Assert.Contains(result.Errors, e => e.Contains("unknown node 'ghost'", StringComparison.Ordinal));
    }

    [Fact]
    public void Discarded_blocks_need_no_coverage_but_flag_a_warning()
    {
        var patch = ValidPatch(); // block 3 (discarded) deliberately unassigned
        Assert.True(TocAlignmentPatcher.Validate(MakeDoc(), patch).IsValid);

        patch.BlockAssignments.Add(new BlockAssignment { BlockId = 3, NodeId = "n1" });
        var withDiscarded = TocAlignmentPatcher.Validate(MakeDoc(), patch);
        Assert.True(withDiscarded.IsValid);
        Assert.Contains(withDiscarded.Warnings, w => w.Contains("discarded", StringComparison.Ordinal));
    }

    [Fact]
    public void Empty_node_is_a_warning_not_a_blocker()
    {
        var patch = ValidPatch();
        patch.Nodes.Add(new ProposedTocNode { Id = "n9", Title = "附录", Level = 1, Page = 2 });

        var result = TocAlignmentPatcher.Validate(MakeDoc(), patch);

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Contains("n9", StringComparison.Ordinal));
    }

    // ----------------------------------------------------------------- apply

    [Fact]
    public void Apply_replaces_toc_page_ordered_and_tags_source()
    {
        var doc = MakeDoc();

        TocAlignmentPatcher.Apply(doc, ValidPatch());

        Assert.Equal(
            [("第一章 源起", 1, 1), ("1.1 驿路", 2, 1), ("第二章 远行", 1, 2)],
            doc.TocEntries.Select(e => (e.Title, e.Level, e.Page)));
        Assert.All(doc.TocEntries, e => Assert.Equal("agent", e.Source));
    }

    // ------------------------------------------------------------------ tools

    private static PopoTocAlignSession MakeSession() => new(MakeDoc());

    [Fact]
    public void Overview_and_toc_tools_report_live_document()
    {
        var overview = JsonNode.Parse(MakeSession().GetDocumentOverview())!.AsObject();
        Assert.Equal(2, (int)overview["pageCount"]!);
        Assert.Equal(5, (int)overview["blockCount"]!);
        Assert.Equal(1, (int)overview["discardedBlockCount"]!);
        Assert.Equal(2, (int)overview["currentTocEntryCount"]!);

        var toc = JsonNode.Parse(MakeSession().GetToc())!.AsArray();
        Assert.Equal(2, toc.Count);
        Assert.Equal("第一章 源起", (string?)toc[0]!["title"]);
    }

    [Fact]
    public void Page_tool_distinguishes_valid_and_invalid_pages()
    {
        var session = MakeSession();

        var page1 = JsonNode.Parse(session.GetPageBlocks(1))!.AsObject();
        Assert.Equal(3, page1["blocks"]!.AsArray().Count);

        var bad = JsonNode.Parse(session.GetPageBlocks(42))!.AsObject();
        Assert.NotNull(bad["error"]);
    }

    [Fact]
    public void Block_tool_returns_content_and_not_found()
    {
        var session = MakeSession();

        var block = JsonNode.Parse(session.GetBlockContent(2))!.AsObject();
        Assert.Equal("驿路三千里。", (string?)block["content"]);

        Assert.NotNull(JsonNode.Parse(session.GetBlockContent(999))!["error"]);
    }

    [Fact]
    public void Propose_stores_only_valid_patches()
    {
        var session = MakeSession();

        var invalid = JsonSerializer.Serialize(
            new TocAlignmentPatch { Nodes = [] }, SourceGenerationContext.Default.TocAlignmentPatch);
        var invalidResult = JsonNode.Parse(session.ProposeAlignmentPatch(invalid))!.AsObject();
        Assert.False((bool)invalidResult["accepted"]!);
        Assert.Null(session.ProposedPatch);

        var validJson = JsonSerializer.Serialize(ValidPatch(), SourceGenerationContext.Default.TocAlignmentPatch);
        var validResult = JsonNode.Parse(session.ProposeAlignmentPatch(validJson))!.AsObject();
        Assert.True((bool)validResult["accepted"]!);
        Assert.NotNull(session.ProposedPatch);
        Assert.NotNull(session.LastValidation);
        Assert.True(session.LastValidation!.IsValid);
    }

    [Fact]
    public void Propose_reports_malformed_json_without_storing()
    {
        var session = MakeSession();

        var result = JsonNode.Parse(session.ProposeAlignmentPatch("{not json"))!.AsObject();

        Assert.False((bool)result["accepted"]!);
        Assert.Null(session.ProposedPatch);
    }

    // -------------------------------------------------- factory & agent wiring

    private sealed class FakeMcpRegistry : IMcpRegistryService
    {
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
            => throw new NotSupportedException();
    }

    private sealed class FakeSettingsService(CalySettings settings) : ISettingsService
    {
        public CalySettings GetSettings() => settings;
        public ValueTask<CalySettings> GetSettingsAsync() => ValueTask.FromResult(settings);
        public void SetProperty(CalySettings.CalySettingsProperty property, object value) { }
        public void Load() { }
        public Task LoadAsync() => Task.CompletedTask;
        public void Save() { }
        public Task SaveAsync() => Task.CompletedTask;
    }

    private sealed class FakeSecretStore : ISecretStore
    {
        public bool TryResolve(string reference, out string? secret)
        {
            secret = "sk-test";
            return true;
        }

        public bool TrySet(string reference, string secret) => true;
    }

    private sealed class ScriptedChatClient(params ChatResponse[] responses) : IChatClient
    {
        private int _calls;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(responses[Math.Min(_calls++, responses.Length - 1)]);

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    private static ChatResponse Call(string id, string tool, IDictionary<string, object?>? args = null) =>
        new(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(id, tool, args)]));

    private static ChatResponse Text(string text) => new(new ChatMessage(ChatRole.Assistant, text));

    [Fact]
    public async Task Factory_exposes_five_call_site_tools_bound_to_the_run_document()
    {
        var context = new AgentToolContext { Document = MakeDoc() };

        var resolved = await AgentCallSiteFactory.ResolveAsync(
            AgentCallSiteDefinitions.PopoTocAlign, new FakeMcpRegistry(), context: context);

        Assert.Equal(
            ["get_document_overview", "get_toc", "get_page_blocks", "get_block_content", "propose_alignment_patch"],
            AgentCallSiteFactory.DescribeToolSurface(resolved));
        Assert.NotNull(context.Items[PopoTocAlignTools.SessionItemKey]);
    }

    [Fact]
    public async Task Factory_requires_a_document_for_the_align_site()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await AgentCallSiteFactory.ResolveAsync(
                AgentCallSiteDefinitions.PopoTocAlign, new FakeMcpRegistry(), context: new AgentToolContext()));
    }

    [Fact]
    public async Task Agent_run_reads_toc_and_stores_accepted_patch_in_context()
    {
        var patchJson = JsonSerializer.Serialize(ValidPatch(), SourceGenerationContext.Default.TocAlignmentPatch);
        var scripted = new ScriptedChatClient(
            Call("c1", "get_toc"),
            Call("c2", "propose_alignment_patch", new Dictionary<string, object?> { ["patchJson"] = patchJson }),
            Text("已提出对齐补丁"));

        var service = new AgentService(
            new FakeSettingsService(new CalySettings { AiModel = "test-model", AiCredentialRef = "keychain:s/a" }),
            new FakeSecretStore(), new FakeMcpRegistry(),
            (_, _) => scripted);

        var context = new AgentToolContext { Document = MakeDoc() };
        var result = await service.RunAsync(AgentCallSiteDefinitions.PopoTocAlign.Id, "重排目录并对齐正文", context);

        Assert.Equal("已提出对齐补丁", result.FinalText);
        Assert.Equal(2, result.ToolTrace.Count);
        Assert.All(result.ToolTrace, line => Assert.EndsWith(": ok", line, StringComparison.Ordinal));

        var session = Assert.IsType<PopoTocAlignSession>(context.Items[PopoTocAlignTools.SessionItemKey]);
        Assert.NotNull(session.ProposedPatch);
        Assert.Equal(3, session.ProposedPatch!.Nodes.Count);
    }
}
