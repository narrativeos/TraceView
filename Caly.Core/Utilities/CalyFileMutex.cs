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

namespace Caly.Core.Utilities;

// Known issue with named System.Threading.Mutex on Linux/macOS with AOT
// See https://github.com/dotnet/runtime/issues/110348
// https://www.martyndavis.com/?p=440

// Solution:
// https://mvolo.com/fun-with-file-locking/

/// <summary>
/// File mutex implementation to ensure cross-platform compatibility.
/// <para>
/// Known issue with named System.Threading.Mutex on Linux/macOS with AOT.
/// See <see href="https://github.com/dotnet/runtime/issues/110348"/> and
/// <see href="https://www.martyndavis.com/?p=440"/>
/// </para>
/// <para>
/// The lock file lives at a deterministic per-user path (see <see cref="InstancePaths"/>) so
/// instances from different <c>$TMPDIR</c> sessions still see each other; otherwise both would
/// become primary and run side by side.
/// </para>
/// </summary>
public sealed class CalyFileMutex
{
    private static readonly string LockFileName = GetLockFileName();

    private static string GetLockFileName()
    {
        // Windows %TEMP% is stable per user, so the historical location still agrees across
        // instances there. Elsewhere $TMPDIR varies per session and must be avoided.
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(Path.GetTempPath(), "caly.lock");
        }

        string name = $"caly_{InstancePaths.PerUserToken}.lock";
        return Path.Combine(InstancePaths.ResolveDirectory(name), name);
    }

    private FileStream? _lockFile;

    /// <summary>
    /// Same signature as default System.Threading.Mutex.
    /// </summary>
    /// <param name="initiallyOwned">Not in use.</param>
    /// <param name="name">Not in use.</param>
    public CalyFileMutex(bool initiallyOwned, string? name)
    {
        // Same signature as default System.Threading.Mutex
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="timeout">Not in use.</param>
    /// <param name="b">Not in use.</param>
    public bool WaitOne(TimeSpan timeout, bool b)
    {
        // Same signature as default System.Threading.Mutex

        try
        {
            // Force FileMode.CreateNew - if the file already exists, should throw (done for Linux)
            _lockFile = new FileStream(LockFileName, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Delete);

            if (!OperatingSystem.IsWindows())
            {
                InstancePaths.TryRestrictToCurrentUser(LockFileName);
            }
        }
        catch (IOException)
        {
            // File already in use
            return false;
        }

        return true;
    }

    public void ReleaseMutex()
    {
        try
        {
            File.Delete(LockFileName);
        }
        catch (Exception e)
        {
            Debug.WriteExceptionToFile(e);
        }

        if (_lockFile is null)
        {
            throw new NullReferenceException("Cannot release file mutex, because the file is null.");
        }

        _lockFile.Dispose();
        _lockFile = null;
    }

    public static bool ForceReleaseMutex()
    {
        try
        {
            File.Delete(LockFileName);
            return true;
        }
        catch (Exception e)
        {
            Debug.WriteExceptionToFile(e);
        }

        return false;
    }
}
