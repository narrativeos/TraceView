using Caly.Core.Models;
using Caly.Core.Services;
using Caly.Core.Services.Agent;
using Caly.Core.Services.Interfaces;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.ClientModel;
using Xunit;

namespace Caly.Tests;

/// <summary>
/// Tests for the approval-gated tool wrapper, the call-site factory wrapping and the
/// agent run loop. No network: the LLM is a scripted in-memory <see cref="IChatClient"/>.
/// </summary>
public class AgentApprovalTests
{
    private sealed record FakeSecretStore(bool Found, string Secret = "sk-test") : ISecretStore
    {
        public bool TryResolve(string reference, out string? secret)
        {
            secret = Found ? Secret : null;
            return Found;
        }

        public bool TrySet(string reference, string secret) => Found;
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

    private sealed class FakeMcpRegistry : IMcpRegistryService
    {
        public IReadOnlyList<McpServerConfig> GetServers() => [];
        public Task<IReadOnlyList<McpClientTool>> GetToolsAsync(string serverName, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<McpClientTool>>([]);
        public Task<McpClientTool?> FindToolAsync(string serverName, string toolName, CancellationToken ct = default)
            => Task.FromResult<McpClientTool?>(null);
        public Task<string?> TestConnectionAsync(string serverName, CancellationToken ct = default)
            => Task.FromResult<string?>("not reachable in tests");
        public void Invalidate(string serverName) { }
        public Task<CallToolResult> CallToolAsync(string serverName, string toolName,
            IReadOnlyDictionary<string, object> arguments, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Canned chat client: reply i is responses[i]; the last reply repeats forever.
    /// </summary>
    private sealed class ScriptedChatClient(params ChatResponse[] responses) : IChatClient
    {
        public List<ChatMessage> Requests { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
        {
            var snapshot = messages.ToList();
            lock (Requests) { Requests.AddRange(snapshot); }
            var index = Math.Min(Requests.Count - 1, responses.Length - 1);
            return Task.FromResult(responses[index]);
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    private static ChatResponse FunctionCallResponse(string toolName) =>
        new(new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("call-1", toolName, new Dictionary<string, object?> { ["arg"] = 1 })]));

    private static ChatResponse TextResponse(string text) =>
        new(new ChatMessage(ChatRole.Assistant, text));

    // ---------------------------------------------------------------- wrapper

    [Fact]
    public async Task Approved_call_delegates_to_inner_function()
    {
        var calls = 0;
        var inner = AIFunctionFactory.Create(() => { calls++; return "inner-result"; }, name: "local_tool");

        AgentToolApprovalRequest? seen = null;
        var wrapped = new UserApprovalAIFunction(inner,
            (request, _) => { seen = request; return Task.FromResult(true); },
            "test.site", "some_server");

        var result = await wrapped.InvokeAsync(new AIFunctionArguments());

        Assert.Equal(1, calls);
        Assert.Equal("inner-result", result?.ToString());
        Assert.NotNull(seen);
        Assert.Equal("test.site", seen!.CallSiteId);
        Assert.Equal("some_server", seen.ServerName);
        Assert.Equal("local_tool", seen.ToolName);
    }

    [Fact]
    public async Task Denied_call_never_reaches_inner_function()
    {
        var calls = 0;
        var inner = AIFunctionFactory.Create(() => { calls++; return "inner-result"; }, name: "local_tool");

        var wrapped = new UserApprovalAIFunction(inner,
            (_, _) => Task.FromResult(false), "test.site", null);

        var result = await wrapped.InvokeAsync(new AIFunctionArguments());

        Assert.Equal(0, calls);
        Assert.Contains("denied", result?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Missing_approver_denies_instead_of_auto_approving()
    {
        var calls = 0;
        var inner = AIFunctionFactory.Create(() => { calls++; return "inner-result"; }, name: "local_tool");

        var wrapped = new UserApprovalAIFunction(inner, approver: null, "test.site", null);

        var result = await wrapped.InvokeAsync(new AIFunctionArguments());

        Assert.Equal(0, calls);
        Assert.Contains("denied", result?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------- factory

    [Fact]
    public async Task Factory_wraps_only_approval_required_local_tools()
    {
        var auto = AIFunctionFactory.Create(() => "auto", name: "read_tool");
        var gated = AIFunctionFactory.Create(() => "gated", name: "write_tool");

        var definition = AgentCallSiteDefinitions.PopoTocAlign with
        {
            LocalTools = [auto, gated],
            LocalToolsFactory = null, // isolate the approval-wrapping behavior from the align tools
            ApprovalRequiredTools = new HashSet<string>(StringComparer.Ordinal) { "write_tool" },
        };

        var approvals = 0;
        AgentToolApprover approver = (_, _) => { approvals++; return Task.FromResult(true); };

        var resolved = await AgentCallSiteFactory.ResolveAsync(definition, new FakeMcpRegistry(), approver);

        Assert.Equal(["read_tool", "write_tool"],
            AgentCallSiteFactory.DescribeToolSurface(resolved));

        await resolved.Tools.Single(t => t.Function.Name == "write_tool").Function.InvokeAsync(new AIFunctionArguments());
        Assert.Equal(1, approvals);

        approvals = 0;
        await resolved.Tools.Single(t => t.Function.Name == "read_tool").Function.InvokeAsync(new AIFunctionArguments());
        Assert.Equal(0, approvals);
    }

    [Fact]
    public async Task Factory_throws_when_bound_mcp_tool_is_missing()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await AgentCallSiteFactory.ResolveAsync(AgentCallSiteDefinitions.PopoPushJiulu, new FakeMcpRegistry()));
    }

    // ------------------------------------------------------------- agent loop

    private static async Task<(AgentResponse Response, List<string> Approvals)> RunLoopAsync(
        AIFunction tool, bool approve)
    {
        var approvals = new List<string>();
        AgentToolApprover approver = (request, _) =>
        {
            approvals.Add(request.ToolName);
            return Task.FromResult(approve);
        };

        var wrapped = new UserApprovalAIFunction(tool, approver, "test.site", null);
        var scripted = new ScriptedChatClient(FunctionCallResponse(tool.Name), TextResponse("done"));

        var agent = new ChatClientAgent(
            new FunctionInvokingChatClient(scripted) { MaximumIterationsPerRequest = 4 },
            new ChatClientAgentOptions
            {
                Name = "test.site",
                UseProvidedChatClientAsIs = true,
                ChatOptions = new ChatOptions { Tools = [wrapped] },
            });

        var response = await agent.RunAsync("go");
        return (response, approvals);
    }

    private static string ExtractText(AgentResponse response) =>
        string.Concat(response.Messages.SelectMany(m => m.Contents).OfType<TextContent>().Select(t => t.Text));

    [Fact]
    public async Task Agent_loop_executes_approved_tool_then_answers()
    {
        var calls = 0;
        var tool = AIFunctionFactory.Create(() => { calls++; return "tool-output"; }, name: "local_tool");

        var (response, approvals) = await RunLoopAsync(tool, approve: true);

        Assert.Equal(["local_tool"], approvals);
        Assert.Equal(1, calls);
        Assert.Contains("done", ExtractText(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Agent_loop_survives_denied_tool_and_reports_denial_to_model()
    {
        var calls = 0;
        var tool = AIFunctionFactory.Create(() => { calls++; return "tool-output"; }, name: "local_tool");

        var (response, approvals) = await RunLoopAsync(tool, approve: false);

        Assert.Equal(["local_tool"], approvals);
        Assert.Equal(0, calls);
        Assert.Contains("done", ExtractText(response), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------ AgentService

    private static AgentService MakeService(CalySettings settings, bool secretFound, IChatClient chat) =>
        new(new FakeSettingsService(settings), new FakeSecretStore(secretFound), new FakeMcpRegistry(),
            NullLogger<AgentService>.Instance, (_, _) => chat);

    [Fact]
    public async Task AgentService_throws_without_configured_model()
    {
        var service = MakeService(new CalySettings { AiModel = "" }, true, new ScriptedChatClient(TextResponse("x")));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RunAsync(AgentCallSiteDefinitions.PopoTocAlign.Id, "hello"));
        Assert.Contains("model", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AgentService_throws_when_credential_missing()
    {
        var service = MakeService(
            new CalySettings { AiModel = "test-model", AiCredentialRef = "env:MISSING_KEY" },
            secretFound: false, new ScriptedChatClient(TextResponse("x")));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RunAsync(AgentCallSiteDefinitions.PopoTocAlign.Id, "hello"));
        Assert.Contains("credential", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AgentService_throws_for_unknown_call_site()
    {
        var service = MakeService(
            new CalySettings { AiModel = "test-model", AiCredentialRef = "env:K" }, true,
            new ScriptedChatClient(TextResponse("x")));

        await Assert.ThrowsAsync<ArgumentException>(() => service.RunAsync("nope.nope", "hello"));
    }

    [Fact]
    public async Task AgentService_runs_call_site_and_returns_final_text()
    {
        var scripted = new ScriptedChatClient(TextResponse("最终回答"));
        var service = MakeService(
            new CalySettings { AiModel = "test-model", AiCredentialRef = "keychain:s/a" }, true, scripted);

        var result = await service.RunAsync(
            AgentCallSiteDefinitions.PopoTocAlign.Id,
            "开始对齐",
            new AgentToolContext { Document = new Caly.Core.Models.StructureDocument() });

        Assert.Equal(AgentCallSiteDefinitions.PopoTocAlign.Id, result.CallSiteId);
        Assert.Equal("最终回答", result.FinalText);
        Assert.Empty(result.ToolTrace);
    }
}
