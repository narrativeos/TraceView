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

namespace Caly.Core.Services.EntityProcessors.Temporal.Models;

/// <summary>
/// Types of temporal relations between two DATE entities.
/// </summary>
public enum TemporalRelationType
{
    /// <summary>Entity A occurs before Entity B in time.</summary>
    Before,

    /// <summary>Entity A occurs after Entity B in time.</summary>
    After,

    /// <summary>Entity A's time range contains Entity B (e.g., "2024年" contains "2024年3月").</summary>
    Contains,

    /// <summary>Entity A is contained within Entity B's time range.</summary>
    ContainedBy,

    /// <summary>Two expressions refer to the same point/period in time (e.g., "2024年" = "甲辰年").</summary>
    Equivalent,

    /// <summary>Two time ranges partially overlap.</summary>
    Overlaps,

    /// <summary>Relationship cannot be determined.</summary>
    Unknown
}

/// <summary>
/// Classification of a temporal expression's type,
/// used by <see cref="TemporalValidator"/> to determine how to normalize it.
/// </summary>
public enum TemporalType
{
    /// <summary>
    /// Absolute date that can be directly converted to ISO 8601.
    /// HanLP has already populated <see cref="SemanticEntity.Normalized"/>.
    /// Examples: "2024年3月15日", "2024-03-15".
    /// </summary>
    Absolute,

    /// <summary>
    /// Chinese era name + year number. Requires lookup in the era table.
    /// Examples: "乾隆十年", "康熙五十二年".
    /// </summary>
    Era,

    /// <summary>
    /// Relative time expression that needs a reference anchor to resolve.
    /// v1: marked as Ambiguous, offset recorded in metadata.
    /// Examples: "去年", "三天前", "次年".
    /// </summary>
    Relative,

    /// <summary>
    /// Time range with explicit start and end.
    /// Examples: "2020年至2023年", "上世纪80年代".
    /// </summary>
    Range,

    /// <summary>
    /// Vague temporal expression that cannot be precisely normalized.
    /// Examples: "很久以前", "近年", "当时".
    /// </summary>
    Vague
}

/// <summary>
/// Precision level of a normalized temporal value.
/// Used for sorting and relationship determination.
/// </summary>
public enum TemporalPrecision
{
    /// <summary>Precision to a specific day (e.g., "2024-03-15").</summary>
    Day,

    /// <summary>Precision to a month (e.g., "2024-03").</summary>
    Month,

    /// <summary>Precision to a year (e.g., "2024").</summary>
    Year,

    /// <summary>Precision to a decade (e.g., "1980s").</summary>
    Decade,

    /// <summary>Precision to a century (e.g., "18世纪").</summary>
    Century,

    /// <summary>Precision unknown or too vague.</summary>
    Unknown
}

/// <summary>
/// Result of normalizing a temporal expression.
/// </summary>
public class TemporalNormalizationResult
{
    /// <summary>ISO 8601 representation (e.g., "2024-03-15", "1745").</summary>
    public string? Iso8601 { get; set; }

    /// <summary>Start of a range, ISO 8601 format.</summary>
    public string? IsoStart { get; set; }

    /// <summary>End of a range, ISO 8601 format.</summary>
    public string? IsoEnd { get; set; }

    /// <summary>Classification of the original expression.</summary>
    public TemporalType Type { get; set; }

    /// <summary>Precision level of the normalization.</summary>
    public TemporalPrecision Precision { get; set; }

    /// <summary>Whether this result needs an anchor to be fully resolved (relative times).</summary>
    public bool NeedsAnchor { get; set; }

    /// <summary>Era name if type is Era (e.g., "乾隆").</summary>
    public string? EraName { get; set; }

    /// <summary>Era year number if type is Era (e.g., 10 for "乾隆十年").</summary>
    public int? EraYear { get; set; }

    /// <summary>Source of the normalization: "HanLP", "EraTable", "None".</summary>
    public string? Source { get; set; }

    /// <summary>Human-readable description of the normalization.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// For relative times: the direction, unit, and value of the offset.
    /// Example: { direction: "before", unit: "day", value: 3 }
    /// </summary>
    public Dictionary<string, object>? RelativeOffset { get; set; }

    /// <summary>
    /// Returns the best available ISO 8601 value for sorting.
    /// For ranges, returns the start date.
    /// </summary>
    public string? GetSortKey() => Iso8601 ?? IsoStart;
}
