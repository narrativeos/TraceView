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
using System.ClientModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Caly.Core.Models;
using Caly.Core.Services.Interfaces;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Caly.Core.Services.Agent;

/// <summary>
/// Runs agent call sites on a Microsoft Agent Framework <see cref="ChatClientAgent"/>.
/// The LLM is any OpenAI-compatible endpoint configured in settings
/// (AiBaseUrl / AiModel / AiCredentialRef); the credential lives in the OS secret
/// store and is resolved fresh on every run so rotations take effect immediately.
/// </summary>
/// <remarks>
/// Tool execution flows through <see cref="FunctionInvokingChatClient"/> so
/// <see cref="AgentCallSiteDefinition.MaxToolIterations"/> bounds the loop, and
/// approval-gated tools are pre-wrapped by <see cref="AgentCallSiteFactory"/> —
/// the agent itself never sees an ungated write tool.
/// </remarks>
internal sealed class AgentService : IAgentService
{
    private readonly ISettingsService _settingsService;
    private readonly ISecretStore _secretStore;
    private readonly IMcpRegistryService _mcpRegistry;
    private readonly ILogger<AgentService> _logger;

    /// <summary>
    /// Test seam: replaces the OpenAI-compatible client built from settings.
    /// </summary>
    private readonly Func<CalySettings, string, IChatClient>? _chatClientFactory;

    public AgentService(
        ISettingsService settingsService,
        ISecretStore secretStore,
        IMcpRegistryService mcpRegistry,
        ILogger<AgentService> logger)
        : this(settingsService, secretStore, mcpRegistry, logger, chatClientFactory: null)
    {
    }

    internal AgentService(
        ISettingsService settingsService,
        ISecretStore secretStore,
        IMcpRegistryService mcpRegistry,
        ILogger<AgentService> logger,
        Func<CalySettings, string, IChatClient>? chatClientFactory)
    {
        _settingsService = settingsService;
        _secretStore = secretStore;
        _mcpRegistry = mcpRegistry;
        _logger = logger;
        _chatClientFactory = chatClientFactory;
    }

    public async Task<AgentRunResult> RunAsync(
        string callSiteId,
        string userPrompt,
        AgentToolContext? context = null,
        AgentToolApprover? approver = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callSiteId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userPrompt);

        var definition = AgentCallSiteDefinitions.All.TryGetValue(callSiteId, out var known)
            ? known
            : throw new ArgumentException($"Unknown agent call site '{callSiteId}'.", nameof(callSiteId));

        var settings = _settingsService.GetSettings();
        var apiKey = ResolveApiKey(settings);
        var chatClient = _chatClientFactory?.Invoke(settings, apiKey) ?? CreateChatClient(settings, apiKey);

        var resolved = await AgentCallSiteFactory
            .ResolveAsync(definition, _mcpRegistry, approver, context, ct).ConfigureAwait(false);

        // Bound the automatic tool loop ourselves: ChatClientAgent builds its own
        // FunctionInvokingChatClient otherwise, which would leave MaxToolIterations inert.
        var invokingClient = new FunctionInvokingChatClient(chatClient)
        {
            MaximumIterationsPerRequest = definition.MaxToolIterations,
        };

        var options = new ChatClientAgentOptions
        {
            Name = definition.Id,
            Description = definition.DisplayName,
            UseProvidedChatClientAsIs = true,
            ChatOptions = new ChatOptions
            {
                Instructions = definition.Instructions,
                Tools = resolved.Tools.Select(t => (AITool)t.Function).ToList(),
            },
        };

        var agent = new ChatClientAgent(invokingClient, options,
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);

        _logger.LogInformation("Agent run {CallSiteId} with {ToolCount} tool(s).", definition.Id, resolved.Tools.Count);

        var response = await agent.RunAsync(userPrompt, session: null, cancellationToken: ct).ConfigureAwait(false);

        return new AgentRunResult(definition.Id, ExtractFinalText(response), BuildToolTrace(response));
    }

    private string ResolveApiKey(CalySettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.AiModel))
        {
            throw new InvalidOperationException(
                "No AI model configured. Set AiModel (and optionally AiBaseUrl) in settings to use agent features.");
        }

        if (string.IsNullOrWhiteSpace(settings.AiCredentialRef) ||
            !_secretStore.TryResolve(settings.AiCredentialRef, out var apiKey))
        {
            throw new InvalidOperationException(
                "AI credential not found. Store the API key with the secret store and set AiCredentialRef in settings.");
        }

        return apiKey;
    }

    private static IChatClient CreateChatClient(CalySettings settings, string apiKey)
    {
        var clientOptions = new OpenAI.OpenAIClientOptions();
        if (!string.IsNullOrWhiteSpace(settings.AiBaseUrl))
        {
            clientOptions.Endpoint = new Uri(settings.AiBaseUrl);
        }

        var chatClient = new OpenAI.Chat.ChatClient(settings.AiModel, new ApiKeyCredential(apiKey), clientOptions);
        return chatClient.AsIChatClient();
    }

    private static string ExtractFinalText(AgentResponse response)
    {
        // Final assistant text is the last text content in the run's message list.
        return response.Messages
            .Reverse()
            .SelectMany(m => m.Contents)
            .OfType<TextContent>()
            .Select(t => t.Text)
            .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t))
            ?? string.Empty;
    }

    private static IReadOnlyList<string> BuildToolTrace(AgentResponse response)
    {
        var namesByCallId = new Dictionary<string, string>(StringComparer.Ordinal);
        var trace = new List<string>();

        foreach (var content in response.Messages.SelectMany(m => m.Contents))
        {
            switch (content)
            {
                case FunctionCallContent call:
                    namesByCallId[call.CallId] = call.Name;
                    break;

                case FunctionResultContent result:
                    var name = namesByCallId.TryGetValue(result.CallId, out var n) ? n : result.CallId;
                    trace.Add(result.Exception is not null
                        ? $"{name}: error {result.Exception.Message}"
                        : ResultOutcome(name, result.Result));
                    break;
            }
        }

        return trace;
    }

    private static string ResultOutcome(string name, object? result) =>
        result is string text && text.Contains("denied", StringComparison.OrdinalIgnoreCase)
            ? $"{name}: denied"
            : $"{name}: ok";
}
