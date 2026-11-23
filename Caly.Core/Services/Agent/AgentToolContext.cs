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
using Caly.Core.Models;

namespace Caly.Core.Services.Agent;

/// <summary>
/// Per-run data a call site's local tools operate on. The invoking UI fills it with
/// the live document; call-site tool factories may park run artifacts (e.g. a
/// produced patch session) in <see cref="Items"/> for the caller to pick up
/// afterwards — the agent itself cannot reach this object.
/// </summary>
public sealed class AgentToolContext
{
    /// <summary>The document the run operates on, when the call site needs one.</summary>
    public StructureDocument? Document { get; init; }

    /// <summary>Run-scoped scratch space keyed by convention (see tool factories).</summary>
    public IDictionary<string, object?> Items { get; } =
        new Dictionary<string, object?>(StringComparer.Ordinal);
}
