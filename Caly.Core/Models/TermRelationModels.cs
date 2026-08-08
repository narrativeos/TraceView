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

namespace Caly.Core.Models;

/// <summary>
/// Relation types for terms (beyond the basic LocationRelation and EntityRelation).
/// </summary>
public static class TermRelationType
{
    /// <summary>X is a kind of Y (hypernym/hyponym).</summary>
    public const string Hypernym = "HYPERNYM";

    /// <summary>Y is a kind of X (reverse of HYPERNYM).</summary>
    public const string Hyponym = "HYPONYM";

    /// <summary>X is synonymous with Y (alternate name, abbreviation).</summary>
    public const string Synonym = "SYNONYM";

    /// <summary>X is composed of Y (meronym).</summary>
    public const string ComposedOf = "COMPOSED_OF";

    /// <summary>X is part of Y (reverse of COMPOSED_OF).</summary>
    public const string PartOf = "PART_OF";

    /// <summary>X has property Y.</summary>
    public const string HasProperty = "HAS_PROPERTY";

    /// <summary>X is related to Y (weak association).</summary>
    public const string RelatedTerm = "RELATED_TERM";

    /// <summary>X and Y co-occur in the same context.</summary>
    public const string CoOccurs = "CO_OCCURS";
}

/// <summary>
/// A typed relation between two terms within the terminology system.
/// Used by TermRelationExtractor and consumed by TermHierarchyBuilder.
/// </summary>
public class TermRelation
{
    /// <summary>Relation type from <see cref="TermRelationType"/>.</summary>
    public string RelationType { get; set; } = string.Empty;

    /// <summary>Normalized text of the source term.</summary>
    public string SourceTerm { get; set; } = string.Empty;

    /// <summary>Normalized text of the target term.</summary>
    public string TargetTerm { get; set; } = string.Empty;

    /// <summary>Confidence score (0.0–1.0).</summary>
    public double Confidence { get; set; }

    /// <summary>Evidence string explaining the source of this relation.</summary>
    public string? Evidence { get; set; }

    /// <summary>Source block IDs where this relation was found.</summary>
    public List<int> SourceBlockIds { get; set; } = new();

    /// <summary>Source of extraction: "definition", "dependency", "coref", "cooccur".</summary>
    public string Source { get; set; } = "definition";
}
