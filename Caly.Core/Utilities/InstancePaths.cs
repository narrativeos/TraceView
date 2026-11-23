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
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Caly.Core.Utilities;

/// <summary>
/// Deterministic per-user locations for the inter-instance artifacts (the file mutex lock
/// file and the file pipe socket). On non-Windows platforms they deliberately avoid
/// <c>$TMPDIR</c>: it varies per session, which made instances disagree about the lock path
/// (two instances starting as primary) and pushed the pipe socket path past the 104 character
/// platform limit (startup crash). Windows <c>%TEMP%</c> is stable per user, so the historical
/// location there is kept.
/// </summary>
internal static class InstancePaths
{
    /// <summary>Bound for the combined path, kept clear of the 104 character platform socket limit.</summary>
    private const int PathLimit = 100;

    /// <summary>Short token that is stable for a given user on a given machine.</summary>
    internal static readonly string PerUserToken = ComputePerUserToken();

    /// <summary>
    /// Resolves the artifact directory for <paramref name="fileName"/>: XDG_RUNTIME_DIR when the
    /// platform provides it and the combined path stays short (systemd Linux), otherwise /tmp.
    /// </summary>
    internal static string ResolveDirectory(string fileName)
    {
        string? runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        return !string.IsNullOrEmpty(runtimeDir) && Path.IsPathRooted(runtimeDir) &&
               Path.Combine(runtimeDir!, fileName).Length < PathLimit
            ? runtimeDir!
            : "/tmp";
    }

    /// <summary>Best-effort restriction of an artifact to the current user; never throws.</summary>
    internal static void TryRestrictToCurrentUser(string path)
    {
        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception)
        {
            // Best effort; other protections (key phrase, file sharing) still apply.
        }
    }

    /// <summary>
    /// User names are unique on a machine, so instances of the same user always agree on the
    /// resulting path; a hash suffix guards against collisions introduced by sanitising /
    /// truncating the slug.
    /// </summary>
    private static string ComputePerUserToken()
    {
        string userName;
        try
        {
            userName = Environment.UserName;
        }
        catch (Exception)
        {
            return "u";
        }

        if (userName.Length == 0)
        {
            return "u";
        }

        string normalized = userName.ToLowerInvariant();
        var slug = new string(normalized.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray());
        if (slug.Length > 24)
        {
            slug = slug[..24];
        }

        return $"{slug}-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)).AsSpan(0, 2)).ToLowerInvariant()}";
    }
}
