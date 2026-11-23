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
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Caly.Core.Services;

/// <summary>
/// <see cref="ISecretStore"/> backed by the OS credential facility.
/// macOS uses the Keychain via the <c>security</c> CLI; Windows uses Credential
/// Manager via advapi32; Linux and mobile platforms fall back to <c>env:</c>
/// references only for now.
/// </summary>
internal sealed class OsSecretStore : ISecretStore
{
    private const string KeychainPrefix = "keychain:";
    private const string EnvPrefix = "env:";

    public bool TryResolve(string reference, [NotNullWhen(true)] out string? secret)
    {
        secret = null;
        if (string.IsNullOrWhiteSpace(reference))
            return false;

        if (reference.StartsWith(EnvPrefix, StringComparison.OrdinalIgnoreCase))
        {
            secret = Environment.GetEnvironmentVariable(reference[EnvPrefix.Length..]);
            return !string.IsNullOrEmpty(secret);
        }

        if (reference.StartsWith(KeychainPrefix, StringComparison.OrdinalIgnoreCase))
        {
            if (!TrySplitKeychainReference(reference[KeychainPrefix.Length..], out var service, out var account))
                return false;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                secret = MacKeychainGet(service, account);
                return !string.IsNullOrEmpty(secret);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                secret = WindowsCredentialRead(service, account);
                return !string.IsNullOrEmpty(secret);
            }
        }

        return false;
    }

#pragma warning disable CA1416 // Platform guard is the RuntimeInformation check above.

    public bool TrySet(string reference, string secret)
    {
        if (string.IsNullOrWhiteSpace(reference) || string.IsNullOrEmpty(secret))
            return false;

        // env: references are read-only by design (the app cannot change its own environment
        // in a way that survives a restart).
        if (reference.StartsWith(EnvPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!reference.StartsWith(KeychainPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!TrySplitKeychainReference(reference[KeychainPrefix.Length..], out var service, out var account))
            return false;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // -U upserts: updates in place or creates the item.
            return RunSecurityCli($"add-generic-password -U -a {account} -s {service} -w {secret}") == 0;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return WindowsCredentialWrite(service, account, secret);
        }

        return false;
#pragma warning restore CA1416
    }

    private static bool TrySplitKeychainReference(string remainder, out string service, out string account)
    {
        service = string.Empty;
        account = string.Empty;

        var separator = remainder.LastIndexOf('/');
        if (separator <= 0 || separator == remainder.Length - 1)
            return false;

        service = remainder[..separator];
        account = remainder[(separator + 1)..];
        return true;
    }

    private static string? MacKeychainGet(string service, string account)
    {
        // `security find-generic-password -s <service> -a <account> -w` prints the secret
        // on stdout, exit code != 0 when the item does not exist.
        var psi = new ProcessStartInfo
        {
            FileName = "/usr/bin/security",
            ArgumentList = { "find-generic-password", "-s", service, "-a", account, "-w" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
                return null;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            return process.ExitCode == 0 ? output.Trim() : null;
        }
        catch (Exception e)
        {
            Debug.WriteExceptionToFile(e);
            return null;
        }
    }

    private static int RunSecurityCli(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "/usr/bin/security",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
                return -1;

            process.WaitForExit(5000);
            return process.ExitCode;
        }
        catch (Exception e)
        {
            Debug.WriteExceptionToFile(e);
            return -1;
        }
    }

    // ----- Windows Credential Manager (advapi32) -----

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string? Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    private const int CREDTypeGeneric = 1;
    private const int CREDPersistLocalMachine = 1;

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredWriteW(ref CREDENTIAL credential, int flags);

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredReadW(string target, int type, int flags, out IntPtr credential);

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr credential);

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static bool WindowsCredentialWrite(string service, string account, string secret)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return false;

        var blob = System.Text.Encoding.UTF8.GetBytes(secret);
        var blobPtr = Marshal.AllocHGlobal(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, blobPtr, blob.Length);
            var credential = new CREDENTIAL
            {
                Type = CREDTypeGeneric,
                TargetName = $"{service}/{account}",
                UserName = account,
                Persist = CREDPersistLocalMachine,
                CredentialBlob = blobPtr,
                CredentialBlobSize = blob.Length,
            };
            return CredWriteW(ref credential, 0);
        }
        finally
        {
            Marshal.FreeHGlobal(blobPtr);
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string? WindowsCredentialRead(string service, string account)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return null;

        if (!CredReadW($"{service}/{account}", CREDTypeGeneric, 0, out var credentialPtr))
            return null;

        try
        {
            var credential = Marshal.PtrToStructure<CREDENTIAL>(credentialPtr);
            if (credential.CredentialBlobSize == 0 || credential.CredentialBlob == IntPtr.Zero)
                return null;

            return Marshal.PtrToStringUTF8(credential.CredentialBlob, credential.CredentialBlobSize);
        }
        finally
        {
            CredFree(credentialPtr);
        }
    }
}
