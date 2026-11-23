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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Caly.Core.Models;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Caly.Core.Services;

/// <summary>
/// The single module that owns MCP server configuration, connections and the tool
/// catalog. Call sites (columns, agent features) never open connections themselves;
/// they bind to tools by (server, tool) pair and receive shared clients from here.
/// </summary>
internal interface IMcpRegistryService
{
    /// <summary>
    /// Snapshot of the configured servers from settings.
    /// </summary>
    IReadOnlyList<McpServerConfig> GetServers();

    /// <summary>
    /// Tools exposed by a server (cached per server after first successful listing).
    /// Connects lazily on first call.
    /// </summary>
    Task<IReadOnlyList<McpClientTool>> GetToolsAsync(string serverName, CancellationToken ct = default);

    /// <summary>
    /// Resolves one tool by exact name, or null when the server does not expose it.
    /// </summary>
    Task<McpClientTool?> FindToolAsync(string serverName, string toolName, CancellationToken ct = default);

    /// <summary>
    /// Opens (or reuses) a connection and pings the server. Used by the settings UI
    /// connection test. Returns a failure description instead of throwing.
    /// </summary>
    Task<string?> TestConnectionAsync(string serverName, CancellationToken ct = default);

    /// <summary>
    /// Drops the cached connection and tool catalog for a server so the next use
    /// reconnects with the latest settings.
    /// </summary>
    void Invalidate(string serverName);

    /// <summary>
    /// Direct tool invocation (RPC use, e.g. JiuluPublisher's publish/status calls).
    /// Bypasses the agent loop entirely; write tools must still be approval-gated
    /// by the caller. Connects lazily like the tool-catalog methods.
    /// </summary>
    Task<CallToolResult> CallToolAsync(
        string serverName,
        string toolName,
        IReadOnlyDictionary<string, object> arguments,
        CancellationToken ct = default);
}
