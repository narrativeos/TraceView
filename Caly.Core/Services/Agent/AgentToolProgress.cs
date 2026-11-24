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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Caly.Core.Services.Agent;

/// <summary>
/// Context-key convention for live tool progress: the invoking UI puts an
/// <c>Action&lt;string, string&gt;</c> under this key in <see cref="AgentToolContext.Items"/>
/// and receives (toolName, status) with status in {start, done, error} for every tool
/// call in the loop, letting it render step feedback without widening IAgentService.
/// </summary>
internal static class AgentToolProgressKeys
{
    public const string ItemKey = "agent.tool_progress";
}

/// <summary>
/// Decorator that emits a progress notification before and after the inner tool runs.
/// Composed outside the approval wrapper on purpose: the start fires while an approval
/// is still pending, which is exactly what a run card should show while it waits.
/// </summary>
internal sealed class NotifyingAIFunction : DelegatingAIFunction
{
    private readonly Action<string, string>? _notify;

    public NotifyingAIFunction(AIFunction inner, Action<string, string>? notify)
        : base(inner)
    {
        _notify = notify;
    }

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        _notify?.Invoke(Name, "start");
        try
        {
            var result = await InnerFunction.InvokeAsync(arguments, cancellationToken).ConfigureAwait(false);
            _notify?.Invoke(Name, "done");
            return result;
        }
        catch
        {
            _notify?.Invoke(Name, "error");
            throw;
        }
    }
}
