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

using System.Diagnostics.CodeAnalysis;

namespace Caly.Core.Services;

/// <summary>
/// Resolves secret references (e.g. MCP server bearer tokens) from platform
/// secure storage. Settings JSON only ever stores references, never raw secrets.
/// </summary>
internal interface ISecretStore
{
    /// <summary>
    /// Resolves a reference of the form <c>keychain:&lt;service&gt;/&lt;account&gt;</c>
    /// (OS secure storage) or <c>env:&lt;VARIABLE_NAME&gt;</c> (environment fallback).
    /// </summary>
    /// <returns>false when the reference is empty, malformed, or the secret is not present.</returns>
    bool TryResolve(string reference, [NotNullWhen(true)] out string? secret);

    /// <summary>
    /// Stores a secret so that <paramref name="reference"/> resolves to it later.
    /// Not supported for <c>env:</c> references.
    /// </summary>
    bool TrySet(string reference, string secret);
}
