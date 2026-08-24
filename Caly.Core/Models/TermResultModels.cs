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
/// A single term entry in the terminology discovery output.
/// Contains term text, score, definition, hierarchy info, and block references.
/// </summary>
public class TermEntry
{
    /// <summary>Normalized term text.</summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    /// <summary>Original entity category from NLP.</summary>
    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    /// <summary>Original (pre-normalization) text.</summary>
    [JsonPropertyName("original_text")]
    public string OriginalText { get; set; } = string.Empty;

    /// <summary>Importance score (0.0–1.0).</summary>
    [JsonPropertyName("score")]
    public double Score { get; set; }

    /// <summary>Definition text, if available.</summary>
    [JsonPropertyName("definition")]
    public string? Definition { get; set; }

    /// <summary>Hypernym (parent term) texts.</summary>
    [JsonPropertyName("hypernyms")]
    public List<string> Hypernyms { get; set; } = new();

    /// <summary>Hyponym (child term) texts.</summary>
    [JsonPropertyName("hyponyms")]
    public List<string> Hyponyms { get; set; } = new();

    /// <summary>Synonym texts.</summary>
    [JsonPropertyName("synonyms")]
    public List<string> Synonyms { get; set; } = new();

    /// <summary>Block IDs where this term appears.</summary>
    [JsonPropertyName("block_ids")]
    public List<int> BlockIds { get; set; } = new();

    /// <summary>Occurrence count across the document.</summary>
    [JsonPropertyName("frequency")]
    public int Frequency { get; set; }

    /// <summary>Number of blocks this term appears in.</summary>
    [JsonPropertyName("block_count")]
    public int BlockCount { get; set; }

    /// <summary>Context summary snippet.</summary>
    [JsonPropertyName("context")]
    public string? Context { get; set; }

    /// <summary>Depth in the term tree (0 = root).</summary>
    [JsonPropertyName("depth")]
    public int Depth { get; set; }
    
    // ── P2: Category confidence and traceability annotations ──
    
    /// <summary>
    /// Confidence level of the NLP category assignment: "high", "medium", "low", or "unknown".
    /// - "high": Named entity types (PERSON, ORGANIZATION, LOCATION) from quality NER
    /// - "medium": Domain-specific types (MATERIAL, PRODUCT, STANDARD, FACILITY)
    /// - "low": UNKNOWN category or entities with noise indicators
    /// - "unknown": No category information available
    /// </summary>
    [JsonPropertyName("category_confidence")]
    public string CategoryConfidence { get; set; } = "unknown";
    
    /// <summary>
    /// Source of the category assignment (e.g., "hanlp_ner", "rule_based", "unknown").
    /// </summary>
    [JsonPropertyName("category_source")]
    public string CategorySource { get; set; } = "hanlp_ner";
    
    /// <summary>
    /// If true, the category may be incorrect (e.g., company name detected as PERSON).
    /// Downstream applications can use this to flag entities for review.
    /// </summary>
    [JsonPropertyName("is_likely_misclassified")]
    public bool IsLikelyMisclassified { get; set; }
    
    /// <summary>
    /// Reason for potential misclassification (e.g., "company_name_as_person", "formula_fragment", "segmentation_artifact").
    /// Empty string if not flagged.
    /// </summary>
    [JsonPropertyName("misclassification_reason")]
    public string MisclassificationReason { get; set; } = string.Empty;
    
    /// <summary>
    /// Traceability information: first occurrence context and extraction method.
    /// </summary>
    [JsonPropertyName("traceability")]
    public TermTraceability? Traceability { get; set; }
}

/// <summary>
/// Traceability information for a term, supporting downstream review and audit.
/// </summary>
public class TermTraceability
{
    /// <summary>Block ID of the first occurrence.</summary>
    [JsonPropertyName("first_occurrence_block")]
    public int? FirstOccurrenceBlock { get; set; }
    
    /// <summary>Character span of the first occurrence [start, end].</summary>
    [JsonPropertyName("first_occurrence_span")]
    public int[]? FirstOccurrenceSpan { get; set; }
    
    /// <summary>Context snippet around the first occurrence.</summary>
    [JsonPropertyName("context_snippet")]
    public string? ContextSnippet { get; set; }
    
    /// <summary>Extraction method that produced this term (e.g., "ner", "definition", "dependency_parse").</summary>
    [JsonPropertyName("extraction_method")]
    public string ExtractionMethod { get; set; } = "ner";
    
    /// <summary>Formula placeholders that were replaced in the source text.</summary>
    [JsonPropertyName("formula_context")]
    public List<string> FormulaContext { get; set; } = new();
    
    /// <summary>Segmentation warnings (e.g., "可能由换行切分").</summary>
    [JsonPropertyName("segmentation_warnings")]
    public List<string> SegmentationWarnings { get; set; } = new();
}

/// <summary>
/// A typed relation between two terms in the output.
/// </summary>
public class TermOutputRelation
{
    /// <summary>Relation type: HYPERNYM, HYPONYM, SYNONYM, COMPOSED_OF, etc.</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>Source term text.</summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    /// <summary>Target term text.</summary>
    [JsonPropertyName("target")]
    public string Target { get; set; } = string.Empty;

    /// <summary>Confidence score (0.0–1.0).</summary>
    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    /// <summary>Evidence string.</summary>
    [JsonPropertyName("evidence")]
    public string? Evidence { get; set; }

    /// <summary>Source block IDs.</summary>
    [JsonPropertyName("block_ids")]
    public List<int> BlockIds { get; set; } = new();
}

/// <summary>
/// A node in the term hierarchy tree (for serialization).
/// </summary>
public class TermTreeNodeOutput
{
    /// <summary>Term text.</summary>
    [JsonPropertyName("term")]
    public string Term { get; set; } = string.Empty;

    /// <summary>Category.</summary>
    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    /// <summary>Score.</summary>
    [JsonPropertyName("score")]
    public double Score { get; set; }

    /// <summary>Definition.</summary>
    [JsonPropertyName("definition")]
    public string? Definition { get; set; }

    /// <summary>Depth in the tree.</summary>
    [JsonPropertyName("depth")]
    public int Depth { get; set; }

    /// <summary>Child nodes.</summary>
    [JsonPropertyName("children")]
    public List<TermTreeNodeOutput> Children { get; set; } = new();
    
    // ── P2: Relation metadata for UI display ──
    
    /// <summary>Relation type from parent (e.g., "HYPERNYM", "IS_A"). Null for root nodes.</summary>
    [JsonPropertyName("parent_relation_type")]
    public string? ParentRelationType { get; set; }
    
    /// <summary>Confidence of the relation from parent (0.0-1.0). Null for root nodes.</summary>
    [JsonPropertyName("parent_relation_confidence")]
    public double? ParentRelationConfidence { get; set; }
    
    /// <summary>Evidence text for the relation from parent. Null for root nodes.</summary>
    [JsonPropertyName("parent_relation_evidence")]
    public string? ParentRelationEvidence { get; set; }
}

/// <summary>
/// Top-level container for term discovery results.
/// Saved as term_result.json in the semantic directory.
/// </summary>
public class TermResultFile
{
    /// <summary>Format version.</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

    /// <summary>ISO 8601 timestamp.</summary>
    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = string.Empty;

    /// <summary>Path to the source semantic_result.json.</summary>
    [JsonPropertyName("source_semantic_result")]
    public string SourceSemanticResult { get; set; } = string.Empty;

    /// <summary>Flat list of all terms with scores and metadata.</summary>
    [JsonPropertyName("terms")]
    public List<TermEntry> Terms { get; set; } = new();

    /// <summary>All term relations.</summary>
    [JsonPropertyName("relations")]
    public List<TermOutputRelation> Relations { get; set; } = new();

    /// <summary>Root nodes of the term hierarchy tree.</summary>
    [JsonPropertyName("term_tree")]
    public List<TermTreeNodeOutput> TermTree { get; set; } = new();

    /// <summary>Summary statistics.</summary>
    [JsonPropertyName("statistics")]
    public TermStatistics Statistics { get; set; } = new();
}

/// <summary>
/// Summary statistics for term discovery results.
/// </summary>
public class TermStatistics
{
    /// <summary>Total number of terms discovered.</summary>
    [JsonPropertyName("total_terms")]
    public int TotalTerms { get; set; }

    /// <summary>Number of terms with explicit definitions.</summary>
    [JsonPropertyName("defined_terms")]
    public int DefinedTerms { get; set; }

    /// <summary>Number of HYPERNYM relations.</summary>
    [JsonPropertyName("hypernym_relations")]
    public int HypernymRelations { get; set; }

    /// <summary>Number of SYNONYM relations.</summary>
    [JsonPropertyName("synonym_relations")]
    public int SynonymRelations { get; set; }

    /// <summary>Average term score.</summary>
    [JsonPropertyName("average_score")]
    public double AverageScore { get; set; }

    /// <summary>Number of root nodes in the tree.</summary>
    [JsonPropertyName("root_nodes")]
    public int RootNodes { get; set; }

    /// <summary>Maximum tree depth.</summary>
    [JsonPropertyName("max_depth")]
    public int MaxDepth { get; set; }
}
