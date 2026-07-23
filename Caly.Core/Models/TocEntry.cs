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

namespace Caly.Core.Models;

/// <summary>
/// Represents a single entry in a table of contents extracted from the Popo analysis tree.
/// </summary>
public sealed record TocEntry
{
    /// <summary>
    /// Display title of the section.
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// Hierarchical level (1-based; root is 0).
    /// </summary>
    public int Level { get; init; }

    /// <summary>
    /// Starting page number (1-based).
    /// </summary>
    public int Page { get; init; }

    /// <summary>
    /// Node type from the Popo tree (e.g., "text", "title", "image").
    /// </summary>
    public string Type { get; init; } = string.Empty;
}
