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
/// Represents a single bibliographic reference entry extracted from the Popo analysis result.
/// Parsed from popo_result.json's result.reference.entries array.
/// </summary>
public sealed record ReferenceEntry
{
    /// <summary>
    /// Sequential identifier for the reference (1-based).
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// Original raw citation text as it appears in the PDF (e.g., "[1]盛敬超.液压流体力学 [M].北京：机械工业出版社，1980.").
    /// This is the preferred display format.
    /// </summary>
    public string RawText { get; init; } = string.Empty;

    /// <summary>
    /// Extracted author names.
    /// </summary>
    public string? Authors { get; init; }

    /// <summary>
    /// Title of the referenced work.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Reference type code (e.g., "M" for monograph, "J" for journal article, "C" for conference paper).
    /// </summary>
    public string? RefType { get; init; }

    /// <summary>
    /// Journal name or publisher name.
    /// </summary>
    public string? JournalOrPublisher { get; init; }

    /// <summary>
    /// Publication year.
    /// </summary>
    public string? Year { get; init; }

    /// <summary>
    /// Volume number (for journal articles).
    /// </summary>
    public string? Volume { get; init; }

    /// <summary>
    /// Issue number (for journal articles).
    /// </summary>
    public string? Issue { get; init; }

    /// <summary>
    /// Page range or page count of the referenced work.
    /// </summary>
    public string? Pages { get; init; }

    /// <summary>
    /// Digital Object Identifier, if available.
    /// </summary>
    public string? Doi { get; init; }

    /// <summary>
    /// URL for the reference, if available.
    /// </summary>
    public string? Url { get; init; }

    /// <summary>
    /// Page number in the current PDF document where this reference appears (1-based).
    /// </summary>
    public int Page { get; init; }

    /// <summary>
    /// IDs of the text blocks in the Popo analysis that correspond to this reference.
    /// Used for navigation and highlighting.
    /// </summary>
    public List<int> BlockIds { get; init; } = new();
}
