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
using System.Text.Json.Serialization;

namespace Caly.Core.Models;

/// <summary>
/// A user-configured MCP server entry (the <c>mcpServers</c> registry).
/// </summary>
/// <remarks>
/// Secrets never live here. The settings file is plain JSON under LocalApplicationData,
/// so a server's bearer token is referenced indirectly via <see cref="CredentialRef"/>
/// and resolved at connect time by <see cref="Services.ISecretStore"/>.
/// </remarks>
public sealed class McpServerConfig
{
    /// <summary>
    /// Unique registry key (e.g. "jiulu_mcp"). Call sites bind tools by this name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable name shown in approval dialogs and the settings UI
    /// (e.g. "九录文华·时空知识中枢MCP服务"). Shown to the user verbatim so a
    /// hostile config entry cannot masquerade as a trusted platform.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Free-form description of what this server provides.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Transport identifier. Only "streamable-http" is supported for now;
    /// stdio would not work on Android/iOS and is a desktop-only candidate later.
    /// </summary>
    public string Transport { get; set; } = StreamableHttpTransport;

    /// <summary>
    /// Absolute endpoint URL of the MCP server (e.g. "https://services.jiulu.ltd/mcp").
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Indirect reference to the bearer token, resolved by <see cref="Services.ISecretStore"/>
    /// (e.g. "keychain:com.traceview.jiulu/token" or "env:JIULU_MCP_TOKEN").
    /// Never store the raw token in settings.
    /// </summary>
    public string CredentialRef { get; set; } = string.Empty;

    /// <summary>
    /// Optional exact tool-name allowlist for this server. Tools outside this list are
    /// never exposed to any call site. Empty means the server contributes no tools
    /// by itself — bindings are declared per call site instead.
    /// </summary>
    public List<string> AllowedTools { get; set; } = [];

    /// <summary>
    /// Whether tools from this server that mutate remote state require an explicit
    /// user confirmation before invocation. Defaults to true (deny-by-default);
    /// a call site can still tighten but never relax this.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool RequiresApprovalForWrites { get; set; } = true;

    public const string StreamableHttpTransport = "streamable-http";
}
