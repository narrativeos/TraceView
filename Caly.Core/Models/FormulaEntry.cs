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
using System.Linq;
using System.Text.Json.Serialization;

namespace Caly.Core.Models;

/// <summary>
/// Represents a single formula/equation entry extracted from the document.
/// Extracted from MinerU blocks of type "equation" and Popo tree nodes.
/// Stored as standard metadata alongside references and TOC entries.
/// </summary>
public sealed class FormulaEntry
{
    /// <summary>
    /// Globally unique formula identifier (e.g., "Eq.1.1", "Eq.p5.f2").
    /// Format: Eq.{chapter}.{sequence} or Eq.{page}.{sequence}
    /// </summary>
    [JsonPropertyName("formula_id")]
    public string FormulaId { get; set; } = string.Empty;

    /// <summary>
    /// Sequential identifier for the formula (1-based).
    /// </summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>
    /// Formula code/label as it appears in the document (e.g., "(1)", "(2.1)").
    /// </summary>
    [JsonPropertyName("formula_code")]
    public string FormulaCode { get; set; } = string.Empty;

    /// <summary>
    /// Original raw formula content as it appears in the PDF (LaTeX, Unicode math, etc.).
    /// </summary>
    [JsonPropertyName("raw_content")]
    public string RawContent { get; set; } = string.Empty;

    /// <summary>
    /// Caption or description text associated with the formula (if any).
    /// </summary>
    [JsonPropertyName("caption")]
    public string? Caption { get; set; }

    /// <summary>
    /// Formula type: "display" for block equations, "inline" for inline math.
    /// </summary>
    [JsonPropertyName("formula_type")]
    public string FormulaType { get; set; } = "display";

    /// <summary>
    /// Page number in the current PDF document where this formula appears (1-based).
    /// </summary>
    [JsonPropertyName("page")]
    public int Page { get; set; }

    /// <summary>
    /// Chapter or section number where this formula appears (if determinable).
    /// </summary>
    [JsonPropertyName("chapter")]
    public int? Chapter { get; set; }

    /// <summary>
    /// Bounding box of the formula block [x0, y0, x1, y1].
    /// </summary>
    [JsonPropertyName("bbox")]
    public double[]? Bbox { get; set; }

    /// <summary>
    /// IDs of the text blocks in the Popo/MinerU analysis that correspond to this formula.
    /// </summary>
    [JsonPropertyName("block_ids")]
    public List<int> BlockIds { get; set; } = new();

    /// <summary>
    /// Source block IDs from MinerU (string identifiers).
    /// </summary>
    [JsonPropertyName("source_block_ids")]
    public List<string> SourceBlockIds { get; set; } = new();

    /// <summary>
    /// Placeholder string used to replace this formula in text before NLP analysis.
    /// Format: "[Eq.{formula_id}]" using the globally unique formula ID.
    /// </summary>
    [JsonPropertyName("placeholder")]
    public string Placeholder { get; set; } = string.Empty;

    /// <summary>
    /// Generates the placeholder string if not already set.
    /// </summary>
    public void EnsurePlaceholder()
    {
        if (string.IsNullOrEmpty(Placeholder))
        {
            Placeholder = $"[Eq.{FormulaId}]";
        }
    }
}

/// <summary>
/// Container for a batch of extracted formulas from a single block/node.
/// Used during NLP preprocessing to track which formulas were extracted.
/// </summary>
public sealed class ExtractedFormulas
{
    /// <summary>
    /// Text with formulas replaced by placeholders.
    /// </summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Formulas extracted from this text, in order of appearance.
    /// </summary>
    [JsonPropertyName("formulas")]
    public List<FormulaEntry> Formulas { get; set; } = new();

    /// <summary>
    /// Mapping from placeholder to formula index for restoration.
    /// </summary>
    [JsonPropertyName("placeholder_map")]
    public Dictionary<string, int> PlaceholderMap { get; set; } = new();
}

/// <summary>
/// Lightweight formula reference for use in semantic analysis results.
/// Only stores the formula ID for traceability, not the full content.
/// This keeps semantic_result.json small while maintaining traceability.
/// </summary>
public sealed class FormulaReference
{
    [JsonPropertyName("formula_id")]
    public string FormulaId { get; set; } = string.Empty;

    [JsonPropertyName("placeholder")]
    public string Placeholder { get; set; } = string.Empty;

    [JsonPropertyName("position")]
    public int? Position { get; set; }

    public static FormulaReference FromFormulaEntry(FormulaEntry entry)
    {
        entry.EnsurePlaceholder();
        return new FormulaReference
        {
            FormulaId = entry.FormulaId,
            Placeholder = entry.Placeholder
        };
    }
}

/// <summary>
/// Independent formula index file for the semantic directory.
/// Contains all formulas extracted from the document.
/// Saved as semantic/formula_index.json.
/// </summary>
public sealed class FormulaIndex
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = string.Empty;

    [JsonPropertyName("source_document")]
    public string SourceDocument { get; set; } = string.Empty;

    [JsonPropertyName("formulas")]
    public List<FormulaEntry> Formulas { get; set; } = new();

    [JsonPropertyName("statistics")]
    public FormulaIndexStatistics Statistics { get; set; } = new();

    public FormulaEntry? GetFormulaById(string formulaId)
    {
        return Formulas.FirstOrDefault(f => f.FormulaId == formulaId);
    }

    public List<FormulaEntry> GetFormulasByPage(int page)
    {
        return Formulas.Where(f => f.Page == page).ToList();
    }
}

/// <summary>
/// Summary statistics for the formula index.
/// </summary>
public sealed class FormulaIndexStatistics
{
    [JsonPropertyName("total_formulas")]
    public int TotalFormulas { get; set; }

    [JsonPropertyName("display_formulas")]
    public int DisplayFormulas { get; set; }

    [JsonPropertyName("inline_formulas")]
    public int InlineFormulas { get; set; }

    [JsonPropertyName("pages_with_formulas")]
    public int PagesWithFormulas { get; set; }

    [JsonPropertyName("average_per_page")]
    public double AveragePerPage { get; set; }
}