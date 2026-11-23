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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Caly.Core.Models;
using Caly.Core.Services.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Caly.Core.Services;

/// <summary>
/// <see cref="IMcpRegistryService"/> for streamable-HTTP servers.
/// One <see cref="McpClient"/> per configured server, created lazily and shared by
/// all call sites; connections that fail are evicted so the next use retries fresh.
/// </summary>
internal sealed class McpRegistryService : IMcpRegistryService, IAsyncDisposable
{
    private const string ClientName = "Caly";

    private readonly ISettingsService _settingsService;
    private readonly ISecretStore _secretStore;

    // Factories are single-flight per server name; a faulted entry removes itself.
    private readonly ConcurrentDictionary<string, Task<McpClient>> _clients = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task<IReadOnlyList<McpClientTool>>> _toolCatalogs = new(StringComparer.OrdinalIgnoreCase);

    public McpRegistryService(ISettingsService settingsService, ISecretStore secretStore)
    {
        _settingsService = settingsService;
        _secretStore = secretStore;
    }

    public IReadOnlyList<McpServerConfig> GetServers() => _settingsService.GetSettings().McpServers;

    public Task<IReadOnlyList<McpClientTool>> GetToolsAsync(string serverName, CancellationToken ct = default)
    {
        return _toolCatalogs.GetOrAdd(serverName,
            static (name, self) => self.ListToolsCoreAsync(name, CancellationToken.None), this);
    }

    public async Task<McpClientTool?> FindToolAsync(string serverName, string toolName, CancellationToken ct = default)
    {
        var tools = await GetToolsAsync(serverName, ct).ConfigureAwait(false);
        return tools.FirstOrDefault(t => string.Equals(t.Name, toolName, StringComparison.Ordinal));
    }

    public async Task<string?> TestConnectionAsync(string serverName, CancellationToken ct = default)
    {
        // A test always reconnects: the settings UI uses it right after editing an entry,
        // and a cached client would validate the stale configuration.
        Invalidate(serverName);
        try
        {
            var client = await GetOrCreateClientAsync(serverName, ct).ConfigureAwait(false);
            await client.PingAsync(cancellationToken: ct).ConfigureAwait(false);
            return null;
        }
        catch (Exception e)
        {
            Debug.WriteExceptionToFile(e);
            Invalidate(serverName);
            return e.Message;
        }
    }

    public void Invalidate(string serverName)
    {
        _toolCatalogs.TryRemove(serverName, out _);
        if (_clients.TryRemove(serverName, out var clientTask))
        {
            // Fire-and-forget dispose once (and only if) the connect task completed.
            _ = DisposeWhenReadyAsync(clientTask);
        }
    }

    public async Task<CallToolResult> CallToolAsync(
        string serverName,
        string toolName,
        IReadOnlyDictionary<string, object> arguments,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        var client = await GetOrCreateClientAsync(serverName, ct).ConfigureAwait(false);
        return await client.CallToolAsync(toolName, arguments, null, null, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<McpClientTool>> ListToolsCoreAsync(string serverName, CancellationToken ct)
    {
        try
        {
            var client = await GetOrCreateClientAsync(serverName, ct).ConfigureAwait(false);
            var tools = await client.ListToolsAsync(cancellationToken: ct).ConfigureAwait(false);
            return tools.ToList();
        }
        catch
        {
            // Evict so the cached catalog does not poison later attempts.
            Invalidate(serverName);
            throw;
        }
    }

    private Task<McpClient> GetOrCreateClientAsync(string serverName, CancellationToken ct)
    {
        return _clients.GetOrAdd(serverName,
            static (name, self) => self.ConnectSafeAsync(name), this);
    }

    private async Task<McpClient> ConnectSafeAsync(string serverName)
    {
        try
        {
            return await ConnectAsync(serverName, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Never leave a faulted connect in the cache; worst case under a race we
            // evict a fresher healthy client, which only costs one extra reconnect.
            _clients.TryRemove(serverName, out _);
            throw;
        }
    }

    private async ValueTask<McpClient> ConnectAsync(string serverName, CancellationToken ct)
    {
        var config = GetServers().FirstOrDefault(
            s => string.Equals(s.Name, serverName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"MCP server '{serverName}' is not configured.");

        if (!string.Equals(config.Transport, McpServerConfig.StreamableHttpTransport, StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                $"MCP transport '{config.Transport}' is not supported (only '{McpServerConfig.StreamableHttpTransport}').");
        }

        if (!Uri.TryCreate(config.Url, UriKind.Absolute, out var endpoint) ||
            (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException($"MCP server '{serverName}' has an invalid URL '{config.Url}'.");
        }

        var headers = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(config.CredentialRef))
        {
            if (!_secretStore.TryResolve(config.CredentialRef, out var token) || string.IsNullOrEmpty(token))
            {
                throw new InvalidOperationException(
                    $"MCP server '{serverName}': credential reference '{config.CredentialRef}' could not be resolved. " +
                    "Store the token in secure storage (Settings > MCP) and retry.");
            }

            headers["Authorization"] = $"Bearer {token}";
        }

        var transportOptions = new HttpClientTransportOptions
        {
            Endpoint = endpoint,
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = headers,
        };

        // SSE streaming responses force ResponseHeadersRead, under which .NET sends
        // unbuffered request bodies chunked; some servers (e.g. PHP stacks behind
        // Caddy) reject large chunked bodies with HTTP 411. Buffering here makes
        // the request always carry a Content-Length.
        var httpClient = new HttpClient(new BufferRequestContentHandler());

        var transport = new HttpClientTransport(transportOptions, httpClient, NullLoggerFactory.Instance, ownsHttpClient: true);

        var clientOptions = new McpClientOptions
        {
            ClientInfo = new Implementation
            {
                Name = ClientName,
                Version = typeof(McpRegistryService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
            },
        };

        try
        {
            return await McpClient.CreateAsync(transport, clientOptions, NullLoggerFactory.Instance, ct)
                .ConfigureAwait(false);
        }
        catch
        {
            await transport.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task DisposeWhenReadyAsync(Task<McpClient> clientTask)
    {
        try
        {
            var client = await clientTask.ConfigureAwait(false);
            await client.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // Faulted tasks are already handled; disposal of a valid client cannot throw meaningfully.
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var name in _clients.Keys.ToList())
        {
            Invalidate(name);
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>
    /// Buffers outgoing request bodies so .NET always sends a Content-Length
    /// instead of chunked transfer encoding (see the transport construction).
    /// Response bodies are left untouched, keeping SSE streaming intact.
    /// </summary>
    private sealed class BufferRequestContentHandler : DelegatingHandler
    {
        public BufferRequestContentHandler()
            : base(new SocketsHttpHandler())
        {
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                await request.Content.LoadIntoBufferAsync().ConfigureAwait(false);
                request.Content.Headers.ContentLength ??=
                    (await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false)).LongLength;
            }

            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }
}
