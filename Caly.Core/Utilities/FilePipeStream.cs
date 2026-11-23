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
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Caly.Core.Utilities;

/// <summary>
/// Transport stream to communicate between application instances on the machine.
/// </summary>
/// <remarks>
/// On Windows the transport is a local named pipe (<see cref="PipeOptions.CurrentUserOnly"/>).
/// Elsewhere (macOS / Linux) it is a Unix domain socket at a short, deterministic path:
/// <see cref="NamedPipeServerStream"/> derives its socket path from <c>$TMPDIR</c>, which
/// varies per session and can exceed the 104 character limit of the platform socket API —
/// the constructor then threw and prevented the app from starting. Server creation is also
/// failure tolerant: the app starts without file handoff rather than crashing.
/// </remarks>
public sealed class FilePipeStream : IDisposable, IAsyncDisposable
{
    // https://googleprojectzero.blogspot.com/2019/09/windows-exploitation-tricks-spoofing.html

    private const string PipeName = "caly_pdf_files.pipe";

    /// <summary>Socket file name, per user so instances of the same user agree regardless of <c>$TMPDIR</c>.</summary>
    private static readonly string UnixSocketName = $"caly_pdf_files_{InstancePaths.PerUserToken}.pipe";

    private static ReadOnlySpan<byte> KeyPhrase => "ca1y k3y pa$$"u8;

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);

    private readonly MemoryPool<byte> _memoryPool = MemoryPool<byte>.Shared;
    private readonly NamedPipeServerStream? _pipeServer;
    private readonly Socket? _unixListener;

    public FilePipeStream()
    {
#if DEBUG
        if (Avalonia.Controls.Design.IsDesignMode)
        {
            _pipeServer = TryCreateNamedPipeServer(Guid.NewGuid().ToString());
            return;
        }
#endif
        if (OperatingSystem.IsWindows())
        {
            _pipeServer = TryCreateNamedPipeServer(PipeName);
        }
        else
        {
            _unixListener = TryCreateUnixListener();
        }
    }

    public async IAsyncEnumerable<string?> ReceivePathAsync([EnumeratorCancellation] CancellationToken token)
    {
        if (_pipeServer is null && _unixListener is null)
        {
            // Transport creation failed (logged there); this instance cannot receive handoff paths.
            yield break;
        }

        while (true)
        {
            string? path = null;
            NetworkStream? unixConnection = null;

            try
            {
                token.ThrowIfCancellationRequested();

                if (_unixListener is not null)
                {
                    unixConnection = new NetworkStream(await _unixListener.AcceptAsync(token), ownsSocket: false);

                    path = await ProcessConnectionAsync(unixConnection, token);
                }
                else
                {
                    // https://learn.microsoft.com/en-us/dotnet/standard/io/how-to-use-named-pipes-for-network-interprocess-communication
                    await _pipeServer!.WaitForConnectionAsync(token);

                    path = await ProcessConnectionAsync(_pipeServer, token);
                }
            }
            catch (OperationCanceledException)
            {
                // No op
            }
            catch (Exception e)
            {
                Debug.WriteExceptionToFile(e);
                throw;
            }
            finally
            {
                unixConnection?.Dispose();

                // We are not connected if operation was canceled
                if (_pipeServer is { IsConnected: true })
                {
                    _pipeServer.Disconnect();
                }
            }

            if (!string.IsNullOrEmpty(path))
            {
                yield return path;
            }
        }
    }

    private async Task<string?> ProcessConnectionAsync(Stream stream, CancellationToken token)
    {
        ushort len;
        using (var lengthMemoryOwner = _memoryPool.Rent(2))
        {
            if (!await ReadExactlyAsync(stream, lengthMemoryOwner.Memory, 2, token))
            {
                // TODO - Log
                return null;
            }

            len = BitConverter.ToUInt16(lengthMemoryOwner.Memory.Span);
        }

        if (len == 0)
        {
            // TODO - Log
            return null;
        }

        using var memoryOwner = _memoryPool.Rent(Math.Max(KeyPhrase.Length + 1, len));

        Memory<byte> buffer = memoryOwner.Memory;

        // Read key phrase and message type
        if (!await ReadExactlyAsync(stream, buffer, KeyPhrase.Length + 1, token))
        {
            // TODO - Log
            return null;
        }

        // Check key phrase
        if (!buffer.Span.Slice(0, KeyPhrase.Length).SequenceEqual(KeyPhrase))
        {
            // TODO - Log
            return null;
        }

        PipeMessageType messageType = (PipeMessageType)buffer.Span[KeyPhrase.Length];
        switch (messageType)
        {
            case PipeMessageType.FilePath:
                {
                    // Read file path
                    if (!await ReadExactlyAsync(stream, buffer, len, token))
                    {
                        // TODO - Log
                        return null;
                    }
                }
                break;

            case PipeMessageType.Command:
                {
                    if (!await ReadExactlyAsync(stream, buffer.Slice(0, 1), 1, token))
                    {
                        // TODO - Log
                        return null;
                    }

                    ProcessMessageCommand((PipeCommandMessageType)buffer.Span[0]);
                }
                break;

            default:
                // TODO - Log
                return null;
        }

        return messageType == PipeMessageType.FilePath
            ? Encoding.UTF8.GetString(buffer.Span.Slice(0, len))
            : null;
    }

    private static async Task<bool> ReadExactlyAsync(Stream stream, Memory<byte> buffer, int count, CancellationToken token)
    {
        // Stream reads may deliver fewer bytes than requested; socket transports make that real.
        int total = 0;
        while (total < count)
        {
            int read = await stream.ReadAsync(buffer.Slice(total, count - total), token);
            if (read == 0)
            {
                return false;
            }

            total += read;
        }

        return true;
    }

    private static void ProcessMessageCommand(PipeCommandMessageType commandType)
    {
        switch (commandType)
        {
            case PipeCommandMessageType.BringToFront:
                App.Current?.TryBringToFront();
                break;

            default:
                // TODO - Log
                break;
        }
    }

    private static NamedPipeServerStream? TryCreateNamedPipeServer(string name)
    {
        try
        {
            return new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly);
        }
        catch (Exception e)
        {
            // Do not block app startup on the inter-instance transport.
            Debug.WriteExceptionToFile(e);
            return null;
        }
    }

    private static Socket? TryCreateUnixListener()
    {
        string path;
        try
        {
            path = GetUnixSocketPath();
        }
        catch (Exception e)
        {
            Debug.WriteExceptionToFile(e);
            return null;
        }

        try
        {
            return BindUnixListener(path);
        }
        catch (SocketException se) when (se.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            // A crashed instance can leave the socket file behind; a live listener answers the
            // probe, an orphaned file refuses the connection.
            if (IsListening(path))
            {
                // Another instance holds this path although the file mutex let us be primary
                // (e.g. the lock was force-released while that instance stayed alive). Do not
                // remove their socket; run without handoff instead.
                Debug.WriteExceptionToFile(new IOException(
                    $"A live Caly file pipe server is already listening at {path}; this instance will not receive files."));
                return null;
            }

            try
            {
                File.Delete(path);
                return BindUnixListener(path);
            }
            catch (Exception e)
            {
                Debug.WriteExceptionToFile(e);
                return null;
            }
        }
        catch (Exception e)
        {
            Debug.WriteExceptionToFile(e);
            return null;
        }
    }

    private static Socket BindUnixListener(string path)
    {
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            socket.Bind(new UnixDomainSocketEndPoint(path));
            socket.Listen(1);
            InstancePaths.TryRestrictToCurrentUser(path);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static bool IsListening(string path)
    {
        try
        {
            using var probe = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            return probe.ConnectAsync(new UnixDomainSocketEndPoint(path)).Wait(TimeSpan.FromMilliseconds(500));
        }
        catch (Exception)
        {
            // Connection refused / no socket: orphaned file.
            return false;
        }
    }

    private static string GetUnixSocketPath() => Path.Combine(InstancePaths.ResolveDirectory(UnixSocketName), UnixSocketName);

    public void Dispose()
    {
        _pipeServer?.Dispose();
        _unixListener?.Dispose();
        _memoryPool.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_pipeServer is not null)
        {
            await _pipeServer.DisposeAsync();
        }
        else
        {
            _unixListener?.Dispose();
        }

        _memoryPool.Dispose();
    }

    public static bool SendBringToFront()
    {
        try
        {
            using (var pipeClient = CreateConnectedClient())
            {
                Memory<byte> lengthBytes = BitConverter.GetBytes((ushort)1);
                pipeClient.Write(lengthBytes.Span);
                pipeClient.Write(KeyPhrase);
                pipeClient.WriteByte((byte)PipeMessageType.Command);
                pipeClient.WriteByte((byte)PipeCommandMessageType.BringToFront);

                pipeClient.Flush();
            }

            return true;
        }
        catch (UnauthorizedAccessException uae)
        {
            // Server must be running in admin, but not the client
            // Handle the case and display error message
            Debug.WriteExceptionToFile(uae);
            throw;
        }
        catch (TimeoutException toe)
        {
            // Could not connect to the running instance of Caly
            // probably because it is actually not running, i.e. the 
            // lock file was not properly deleted after close
            CalyFileMutex.ForceReleaseMutex();
            throw ThrowOnTimeoutException(toe);
        }
        catch (Exception e)
        {
            Debug.WriteExceptionToFile(e);
            throw;
        }
    }

    public static bool SendPath(string filePath)
    {
        try
        {
            using (var pipeClient = CreateConnectedClient())
            {
                Memory<byte> pathBytes = Encoding.UTF8.GetBytes(filePath);
                if (pathBytes.Length > ushort.MaxValue)
                {
                    throw new PathTooLongException($"The pdf file path passed to Caly is too long. Received {pathBytes.Length} bytes, and maximum size is {ushort.MaxValue}.");
                }

                Memory<byte> lengthBytes = BitConverter.GetBytes((ushort)pathBytes.Length);
                pipeClient.Write(lengthBytes.Span);
                pipeClient.Write(KeyPhrase);
                pipeClient.WriteByte((byte)PipeMessageType.FilePath);
                pipeClient.Write(pathBytes.Span);

                pipeClient.Flush();
            }

            return true;
        }
        catch (UnauthorizedAccessException uae)
        {
            // Server must be running in admin, but not the client
            // Handle the case and display error message
            Debug.WriteExceptionToFile(uae);
            throw;
        }
        catch (TimeoutException toe)
        {
            // Could not connect to the running instance of Caly
            // probably because it is actually not running, i.e. the 
            // lock file was not properly deleted after close
            CalyFileMutex.ForceReleaseMutex();
            throw ThrowOnTimeoutException(toe);
        }
        catch (Exception e)
        {
            Debug.WriteExceptionToFile(e);
            throw;
        }
    }

    /// <summary>
    /// Client connected to the running instance's server, honoring <see cref="ConnectTimeout"/>.
    /// A refused / missing endpoint is surfaced as <see cref="TimeoutException"/> so callers keep
    /// the existing stale-lock recovery path.
    /// </summary>
    private static Stream CreateConnectedClient()
    {
        if (OperatingSystem.IsWindows())
        {
            var pipeClient = new NamedPipeClientStream(".", PipeName,
                PipeDirection.Out, PipeOptions.CurrentUserOnly,
                TokenImpersonationLevel.Identification);
            try
            {
                pipeClient.Connect(ConnectTimeout); // If you are getting a timeout in debug mode, just re-run Caly
                return pipeClient;
            }
            catch
            {
                pipeClient.Dispose();
                throw;
            }
        }

        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            var connectTask = socket.ConnectAsync(new UnixDomainSocketEndPoint(GetUnixSocketPath()));
            try
            {
                if (!connectTask.Wait(ConnectTimeout))
                {
                    throw new TimeoutException("Connect timed out.");
                }
            }
            catch (AggregateException ae) when (ae.Flatten().InnerException is SocketException se &&
                                               se.SocketErrorCode is SocketError.TimedOut or SocketError.ConnectionRefused or SocketError.HostDown)
            {
                // Nothing listening at the socket file - treat like the named pipe timeout case.
                throw new TimeoutException("No running instance listening at the file pipe socket.", se);
            }

            connectTask.GetAwaiter().GetResult();
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static CalyCriticalException ThrowOnTimeoutException(TimeoutException toe)
    {
        return new CalyCriticalException("Could not connect to the running instance of Caly," +
                                        " probably because it is actually not running, i.e. the" +
                                        " Caly lock was not properly removed after close.", toe)
        {
            TryRestartApp = true
        };
    }

    private enum PipeMessageType : byte
    {
        None = 0,
        FilePath = 1,
        Command = 2
    }

    private enum PipeCommandMessageType : byte
    {
        None = 0,
        BringToFront = 1
    }
}
