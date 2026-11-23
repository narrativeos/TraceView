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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Caly.Core.Services.Agent;

/// <summary>
/// Resolves an <see cref="AgentCallSiteDefinition"/> into concrete tools by pulling
/// the bound MCP tools from the registry. Fails fast: a call site that names a tool
/// its bound server no longer exposes is a configuration error, not a silent
/// degradation — the agent must never run with a partially missing tool surface.
/// </summary>
internal static class AgentCallSiteFactory
{
    public static async ValueTask<ResolvedAgentCallSite> ResolveAsync(
        AgentCallSiteDefinition definition,
        IMcpRegistryService registry,
        AgentToolApprover? approver = null,
        AgentToolContext? context = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(registry);

        var localTools = definition.LocalTools;
        if (definition.LocalToolsFactory is not null)
        {
            localTools = [.. localTools, .. definition.LocalToolsFactory(context ?? new AgentToolContext())];
        }

        var tools = new List<AgentTool>(localTools.Count);

        foreach (var function in localTools)
        {
            var requiresApproval = definition.ApprovalRequiredTools.Contains(function.Name);
            tools.Add(new AgentTool(
                Wrap(function, requiresApproval, definition.Id, serverName: null, approver), requiresApproval));
        }

        foreach (var (serverName, allowedToolNames) in definition.McpToolBindings)
        {
            foreach (var toolName in allowedToolNames)
            {
                var tool = await registry.FindToolAsync(serverName, toolName, ct).ConfigureAwait(false)
                    ?? throw new InvalidOperationException(
                        $"Call site '{definition.Id}': MCP server '{serverName}' does not expose tool '{toolName}'.");

                // Remote tools default to approval-required (deny-by-default); read-only
                // MCP tools can opt out later via an explicit ReadOnlyMcpTools list.
                const bool requiresApproval = true;
                tools.Add(new AgentTool(
                    Wrap(tool, requiresApproval, definition.Id, serverName, approver), requiresApproval, serverName));
            }
        }

        return new ResolvedAgentCallSite(definition, tools);
    }

    /// <summary>
    /// Approval-gated tools always run through <see cref="UserApprovalAIFunction"/>,
    /// even when no approver is supplied: a missing approver then denies every call
    /// (fail-closed) instead of silently auto-approving a write.
    /// </summary>
    private static AIFunction Wrap(
        AIFunction function,
        bool requiresApproval,
        string callSiteId,
        string? serverName,
        AgentToolApprover? approver) =>
        requiresApproval ? new UserApprovalAIFunction(function, approver, callSiteId, serverName) : function;

    /// <summary>
    /// Flatten for diagnostics/tests: "server-or-local:tool" names actually bound.
    /// </summary>
    public static IReadOnlyList<string> DescribeToolSurface(ResolvedAgentCallSite resolved) =>
        resolved.Tools.Select(t => t.Function.Name).ToList();
}
