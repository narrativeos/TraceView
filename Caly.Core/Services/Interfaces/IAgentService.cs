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
using System.Threading;
using System.Threading.Tasks;
using Caly.Core.Services.Agent;

namespace Caly.Core.Services.Interfaces;

/// <summary>
/// Runs a declared agent call site to completion and returns its final answer plus a
/// tool trace. One run = one prompt in, one answer out; approval-gated tools pause the
/// loop on <paramref name="approver"/> until the user decides.
/// </summary>
public interface IAgentService
{
    /// <summary>
    /// Execute the call site identified by <paramref name="callSiteId"/>
    /// (see AgentCallSiteDefinitions). Throws when AI settings are missing, when a
    /// bound MCP tool is unavailable, or when the model call fails.
    /// </summary>
    /// <param name="context">
    /// Data the call site's local tools operate on (current document) and a place to
    /// collect run artifacts; pass null when the call site needs neither.
    /// </param>
    Task<AgentRunResult> RunAsync(
        string callSiteId,
        string userPrompt,
        AgentToolContext? context = null,
        AgentToolApprover? approver = null,
        CancellationToken ct = default);
}

/// <summary>
/// Outcome of one agent run.
/// </summary>
/// <param name="CallSiteId">Call site that produced this result.</param>
/// <param name="FinalText">The model's final text answer.</param>
/// <param name="ToolTrace">One line per tool outcome, in execution order
/// ("name: ok" / "name: denied" / "name: error ...").</param>
public sealed record AgentRunResult(
    string CallSiteId,
    string FinalText,
    IReadOnlyList<string> ToolTrace);
