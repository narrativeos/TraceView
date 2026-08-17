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
/// AOT-safe, flat serialization model for a single enriched entity entry.
/// All type-specific metadata is stored as "key=value" string pairs.
/// </summary>
public class EnrichedEntityEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("normalized")]
    public string Normalized { get; set; } = string.Empty;

    // ── Validation ──

    [JsonPropertyName("validation_status")]
    public string ValidationStatus { get; set; } = string.Empty;

    [JsonPropertyName("validation_reason")]
    public string ValidationReason { get; set; } = string.Empty;

    // ── Enrichment ──

    [JsonPropertyName("enriched_text")]
    public string EnrichedText { get; set; } = string.Empty;

    [JsonPropertyName("external_id")]
    public string ExternalId { get; set; } = string.Empty;

    [JsonPropertyName("enrichment_source")]
    public string EnrichmentSource { get; set; } = string.Empty;

    [JsonPropertyName("enrichment_confidence")]
    public double EnrichmentConfidence { get; set; }

    /// <summary>
    /// Distinguishes between real enrichment (external knowledge added),
    /// validation-only (text unchanged, just verified), and no processing.
    /// Values: "external" | "validation" | "none"
    /// </summary>
    [JsonPropertyName("enrichment_type")]
    public string EnrichmentType { get; set; } = "none";

    // ── Type-specific metadata (flat key=value pairs, AOT-safe) ──

    [JsonPropertyName("metadata")]
    public List<string> Metadata { get; set; } = new();

    // ── Computed properties for UI display ──

    /// <summary>
    /// Whether this entity has been truly enriched with external knowledge
    /// (text changed or external ID found). Validation-only does NOT count.
    /// </summary>
    [JsonIgnore]
    public bool IsEnriched => EnrichmentType == "external";

    /// <summary>
    /// Whether this entity was validated but not externally enriched
    /// (text unchanged, source is a validator).
    /// </summary>
    [JsonIgnore]
    public bool IsOnlyValidated => EnrichmentType == "validation";

    /// <summary>
    /// Status icon for display: ✓ valid, ⚠ ambiguous, ✗ invalid, ? unknown.
    /// </summary>
    [JsonIgnore]
    public string StatusIcon => ValidationStatus switch
    {
        "valid" => "✓",
        "ambiguous" => "⚠",
        "invalid" => "✗",
        _ => "?"
    };

    /// <summary>
    /// Status color for display.
    /// </summary>
    [JsonIgnore]
    public string StatusColor => ValidationStatus switch
    {
        "valid" => "#4CAF50",
        "ambiguous" => "#FF9800",
        "invalid" => "#F44336",
        _ => "#9E9E9E"
    };

    /// <summary>
    /// Category color hex for border/icon (matches Semantic column colors).
    /// </summary>
    [JsonIgnore]
    public string CategoryColorHex => Category switch
    {
        "LOCATION" => "#4CAF50",
        "DATE" => "#FF9800",
        "PERSON" => "#2196F3",
        "NUMBER" => "#9C27B0",
        "FACILITY" => "#607D8B",
        "ORGANIZATION" => "#E91E63",
        _ => "#78909C"
    };

    /// <summary>
    /// Category background color for the entity card.
    /// </summary>
    [JsonIgnore]
    public string CategoryBackground => Category switch
    {
        "LOCATION" => "#4CAF5018",
        "DATE" => "#FF980018",
        "PERSON" => "#2196F318",
        "NUMBER" => "#9C27B018",
        "FACILITY" => "#607D8B18",
        "ORGANIZATION" => "#E91E6318",
        _ => "#546E7A18"
    };

    /// <summary>
    /// Category border color with opacity.
    /// </summary>
    [JsonIgnore]
    public string CategoryBorder => Category switch
    {
        "LOCATION" => "#4CAF5055",
        "DATE" => "#FF980055",
        "PERSON" => "#2196F355",
        "NUMBER" => "#9C27B055",
        "FACILITY" => "#607D8B55",
        "ORGANIZATION" => "#E91E6355",
        _ => "#546E7A44"
    };

    /// <summary>
    /// Category text color (light version for text).
    /// </summary>
    [JsonIgnore]
    public string CategoryTextColor => Category switch
    {
        "LOCATION" => "#C8E6C9",
        "DATE" => "#FFE0B2",
        "PERSON" => "#BBDEFB",
        "NUMBER" => "#E1BEE7",
        "FACILITY" => "#B0BEC5",
        "ORGANIZATION" => "#F8BBD0",
        _ => "#90A4AE"
    };

    /// <summary>
    /// Chinese category name for display.
    /// </summary>
    [JsonIgnore]
    public string CategoryDisplay => Category switch
    {
        "LOCATION" => "地点",
        "DATE" => "时间",
        "PERSON" => "人物",
        "NUMBER" => "数值",
        "FACILITY" => "设施",
        "ORGANIZATION" => "组织",
        _ => Category
    };

    /// <summary>
    /// Display text to show: if enriched shows "original → enriched", otherwise just original.
    /// </summary>
    [JsonIgnore]
    public string ArrowDisplayText
    {
        get
        {
            if (!string.IsNullOrEmpty(EnrichedText) && EnrichedText != Text)
                return $"{Text} → {EnrichedText}";
            return Text;
        }
    }

    /// <summary>
    /// Whether the entity text changed after enrichment (regardless of type).
    /// </summary>
    [JsonIgnore]
    public bool TextChanged => !string.IsNullOrEmpty(EnrichedText) && EnrichedText != Text;

    /// <summary>
    /// One-line summary for quick display: "source · confidence · external_id"
    /// Shows "(verified)" for validation-only results.
    /// </summary>
    [JsonIgnore]
    public string QuickSummary
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(EnrichmentSource))
            {
                parts.Add(EnrichmentSource);
                if (EnrichmentType == "validation")
                    parts.Add("(verified)");
                if (EnrichmentConfidence > 0)
                    parts.Add($"{(EnrichmentConfidence * 100):F0}%");
                if (!string.IsNullOrEmpty(ExternalId))
                    parts.Add(ExternalId);
            }
            return string.Join(" · ", parts);
        }
    }
}

/// <summary>
/// A serializable relation entry spanning all entity types.
/// </summary>
public class EnrichedRelation
{
    [JsonPropertyName("relation_type")]
    public string RelationType { get; set; } = string.Empty;

    [JsonPropertyName("source_entity_id")]
    public string SourceEntityId { get; set; } = string.Empty;

    [JsonPropertyName("target_entity_id")]
    public string TargetEntityId { get; set; } = string.Empty;

    [JsonPropertyName("entity_category")]
    public string EntityCategory { get; set; } = string.Empty;

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("evidence")]
    public string Evidence { get; set; } = string.Empty;
}

/// <summary>
/// Block-level enriched result (entities + relations).
/// </summary>
public class EnrichedBlockResult
{
    [JsonPropertyName("block_ids")]
    public List<int> BlockIds { get; set; } = new();

    [JsonPropertyName("entities")]
    public List<EnrichedEntityEntry> Entities { get; set; } = new();

    [JsonPropertyName("relations")]
    public List<EnrichedRelation> Relations { get; set; } = new();
}

/// <summary>
/// Top-level container for all enriched entity results.
/// Persisted as enriched_result.json alongside semantic_result.json.
/// </summary>
public class EnrichedResultFile
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = string.Empty;

    [JsonPropertyName("source_semantic_result")]
    public string SourceSemanticResult { get; set; } = string.Empty;

    [JsonPropertyName("blocks")]
    public List<EnrichedBlockResult> Blocks { get; set; } = new();

    [JsonPropertyName("cross_block_relations")]
    public List<EnrichedRelation> CrossBlockRelations { get; set; } = new();
}
