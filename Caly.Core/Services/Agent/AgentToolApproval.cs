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
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Caly.Core.Services;

namespace Caly.Core.Services.Agent;

/// <summary>
/// Everything the user needs to see to decide on one pending tool execution.
/// </summary>
/// <param name="CallSiteId">Call site the agent runs at, e.g. "popo.push-jiulu".</param>
/// <param name="ServerName">MCP registry name for remote tools, null for local tools.</param>
/// <param name="ToolName">Tool the model is trying to execute.</param>
/// <param name="ArgumentsSummary">Best-effort human-readable rendering of the arguments.</param>
public sealed record AgentToolApprovalRequest(
    string CallSiteId,
    string? ServerName,
    string ToolName,
    string ArgumentsSummary);

/// <summary>
/// Returns true to let the tool execute, false (or a throw) to deny it.
/// Implementations show the approval UI; the agent run blocks on this callback.
/// </summary>
public delegate Task<bool> AgentToolApprover(AgentToolApprovalRequest request, CancellationToken ct);

/// <summary>
/// Wraps an <see cref="AIFunction"/> so that every execution first goes through an
/// approval callback. Denials (and a missing approver) never reach the inner function:
/// the model receives an explicit refusal as the tool result instead.
/// </summary>
/// <remarks>
/// This is the approval mechanism for one-shot call sites: unlike MAF's
/// <c>ToolApprovalAgent</c> (which pauses the run and resumes on a response token),
/// the run simply blocks while the user decides — matching our on-demand,
/// dialog-driven UX.
/// </remarks>
internal sealed class UserApprovalAIFunction : DelegatingAIFunction
{
    private const int MaxArgumentValueChars = 240;

    private readonly AgentToolApprover? _approver;
    private readonly string _callSiteId;
    private readonly string? _serverName;

    public UserApprovalAIFunction(
        AIFunction inner,
        AgentToolApprover? approver,
        string callSiteId,
        string? serverName)
        : base(inner)
    {
        _approver = approver;
        _callSiteId = callSiteId;
        _serverName = serverName;
    }

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var request = new AgentToolApprovalRequest(
            _callSiteId, _serverName, Name, SummarizeArguments(arguments));

        var approved = _approver is not null && await _approver(request, cancellationToken).ConfigureAwait(false);
        if (!approved)
        {
            // Refuse as a normal tool result so the model can acknowledge and stop
            // rather than seeing an opaque failure.
            return "The user denied this operation. The tool was not executed. Do not retry it without new instructions.";
        }

        return await InnerFunction.InvokeAsync(arguments, cancellationToken).ConfigureAwait(false);
    }

    private static string SummarizeArguments(AIFunctionArguments arguments)
    {
        if (arguments.Count == 0)
        {
            return "(no arguments)";
        }

        var sb = new StringBuilder();
        foreach (var (key, value) in arguments)
        {
            if (sb.Length > 0)
            {
                sb.Append(", ");
            }

            sb.Append(key).Append('=').Append(FormatValue(value));
        }

        return sb.ToString();
    }

    private static string FormatValue(object? value) => value switch
    {
        null => "null",
        JsonElement json => Truncate(json.GetRawText()),
        string or bool => Truncate(value.ToString() ?? string.Empty),
        // Arguments parsed from the model's JSON arrive as JsonElement; anything else
        // is dictionary/enumerable composition — render it back to compact JSON.
        _ => Truncate(SafeJson(value)),
    };

    private static string SafeJson(object value)
    {
        try
        {
            return JsonSerializer.Serialize(value, SourceGenerationContext.Default.GetTypeInfo(value.GetType()));
        }
        catch (Exception)
        {
            return value.ToString() ?? string.Empty;
        }
    }

    private static string Truncate(string text) =>
        text.Length <= MaxArgumentValueChars ? text : text[..MaxArgumentValueChars] + "…";
}
