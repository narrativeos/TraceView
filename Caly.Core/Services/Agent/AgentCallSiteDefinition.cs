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
using Microsoft.Extensions.AI;

namespace Caly.Core.Services.Agent;

/// <summary>
/// Declarative binding between a UI call site (a column feature, a command) and the
/// tools an agent may use there. Call sites are the unit of least privilege: a site
/// sees exactly the tools it lists and nothing else — MCP servers are never bound
/// wholesale.
/// </summary>
/// <remarks>
/// The definition is resolved into an <see cref="ResolvedAgentCallSite"/> by
/// <see cref="AgentCallSiteFactory"/>; turning a resolved site into an actual agent
/// (MAF ChatClientAgent + approval middleware) happens at the call site's entry point.
/// </remarks>
internal sealed record AgentCallSiteDefinition
{
    /// <summary>
    /// Stable identifier, e.g. "popo.toc-align". Used for telemetry and DI lookups.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Human-readable name shown in the UI.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// System prompt for the agent.
    /// TODO: document-dependent context (current TOC, block counts) will be injected
    /// via a context provider when the agent loop lands (step 4); until then keep
    /// this static.
    /// </summary>
    public required string Instructions { get; init; }

    /// <summary>
    /// MCP tool bindings: server registry name → exact tool names to expose here.
    /// Names are matched case-sensitively against the server's tools/list output.
    /// Empty by default: an unlisted tool is never reachable (deny-by-default).
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> McpToolBindings { get; init; } =
        EmptyToolBindings;

    /// <summary>
    /// In-process tools (reads over PopoJsonService/StructureDocument, patch proposal, etc.).
    /// </summary>
    public IReadOnlyList<AIFunction> LocalTools { get; init; } = [];

    /// <summary>
    /// Per-run tool factory: for call sites whose tools must bind to the run's data
    /// (the live StructureDocument etc.) instead of static state. Evaluated once per
    /// run with the caller's <see cref="AgentToolContext"/>.
    /// </summary>
    public Func<AgentToolContext, IReadOnlyList<AIFunction>>? LocalToolsFactory { get; init; }

    /// <summary>
    /// Tool names (local or MCP) that mutate state and must be confirmed by the user
    /// through IDialogService before execution. A call site can add names but can
    /// never mark a server-declared write as auto-approved.
    /// </summary>
    public IReadOnlySet<string> ApprovalRequiredTools { get; init; } =
        new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Whether the agent may loop multiple tool rounds on its own, or should stop
    /// after each write tool returns (generation-type sites, e.g. TOC patch propose
    /// &amp; validate, benefit from loop caps; execution-type sites usually one-shot).
    /// </summary>
    public int MaxToolIterations { get; init; } = 16;

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> EmptyToolBindings { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// A call site with its MCP bindings resolved into concrete tool handles.
/// </summary>
internal sealed record ResolvedAgentCallSite(
    AgentCallSiteDefinition Definition,
    IReadOnlyList<AgentTool> Tools);

/// <summary>
/// One tool exposed at a call site, carrying its approval requirement.
/// Wraps either a local <see cref="AIFunction"/> or a resolved MCP tool.
/// </summary>
/// <param name="ServerName">MCP registry name for remote tools; null for local ones.</param>
internal sealed record AgentTool(AIFunction Function, bool RequiresApproval, string? ServerName = null);
