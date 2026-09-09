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
using Caly.Core.Services;

namespace Caly.Core.Models;

/// <summary>
/// Syntactic role constants for LOCATION entities.
/// Used to classify how a location functions within a sentence based on dependency parsing.
/// </summary>
public static class LocationSyntacticRole
{
    public const string Unknown = "Unknown";
    public const string Adverbial = "Adverbial";       // 状语：动作的背景/容器（在客栈歇息、向江南进发）
    public const string Subject = "Subject";           // 主语：动作的施事者/拟人化主体（长安陷落了、江南多烟雨）
    public const string Object = "Object";             // 宾语：动作的受事者/目标（攻克城池、烧毁村庄）
    public const string Attributive = "Attributive";   // 定语：实体的属性/修饰语（江南的丝绸、京城的权贵）
    public const string Predicative = "Predicative";   // 表语/补语：状态的结果/终点（他到了北京、这地方真像仙境）

    /// <summary>
    /// Converts a syntactic role string to its Chinese display name.
    /// </summary>
    public static string ToDisplay(string role) => role switch
    {
        Adverbial => "状语",
        Subject => "主语",
        Object => "宾语",
        Attributive => "定语",
        Predicative => "表语",
        _ => "未知"
    };

    /// <summary>
    /// Provides a narrative description of the spatial function for a given syntactic role.
    /// </summary>
    public static string ToNarrativeDescription(string role) => role switch
    {
        Adverbial => "动作的背景/容器",
        Subject => "动作的施事者/拟人化主体",
        Object => "动作的受事者/目标",
        Attributive => "实体的属性/修饰语",
        Predicative => "状态的结果/终点",
        _ => ""
    };
}

/// <summary>

//// Note: AnalysisTreeNode is defined in PopoTreeNode.cs as a partial class.
//// We do NOT redefine it here to avoid ambiguity errors.

/// <summary>
/// Represents a dependency parsing token for syntactic role analysis.
/// </summary>
public class SemanticDepToken
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("pos")]
    public string Pos { get; set; } = string.Empty;
}

/// <summary>
/// Represents a dependency edge for syntactic role analysis.
/// Child token depends on Head token with the given relation.
/// </summary>
public class SemanticDepEdge
{
    [JsonPropertyName("child")]
    public int Child { get; set; }

    [JsonPropertyName("head")]
    public int Head { get; set; }

    [JsonPropertyName("rel")]
    public string Rel { get; set; } = string.Empty;
}

/// <summary>
/// Represents a tokenized word from NLP analysis.
/// </summary>
/// <summary>
/// A block-level noun-phrase signal (Step A POS gating + Step B syntactic role).
/// Not an entity — a linguistic candidate for caller-side aggregation/clustering/audit.
/// </summary>
public class SemanticNounSignal
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("pos")]
    public string Pos { get; set; } = string.Empty;

    [JsonPropertyName("syntactic_role")]
    public string SyntacticRole { get; set; } = "unknown";

    [JsonPropertyName("score")]
    public double Score { get; set; }

    [JsonPropertyName("span")]
    public int[] Span { get; set; } = new int[2];

    [JsonPropertyName("evidence")]
    public Dictionary<string, object>? Evidence { get; set; }
}

/// <summary>
/// A document-level aggregated noun signal (Step C): the same noun text
/// deduplicated across all blocks, with occurrence count, dominant syntactic
/// role, best block-level score, and sample evidence. Feeds the "addition"
/// (加法) discovery of high-value nouns that HanLP did not surface as entities.
/// </summary>
public class DocumentNounSignal
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("pos")]
    public string Pos { get; set; } = string.Empty;

    /// <summary>Dominant syntactic role (highest-priority role observed: Subject &gt; Object &gt; Attributive &gt; Adverbial).</summary>
    [JsonPropertyName("syntactic_role")]
    public string SyntacticRole { get; set; } = "unknown";

    /// <summary>Highest block-level salience score observed for this text.</summary>
    [JsonPropertyName("best_score")]
    public double BestScore { get; set; }

    /// <summary>Number of times this text appeared as a noun signal across all blocks.</summary>
    [JsonPropertyName("occurrences")]
    public int Occurrences { get; set; }

    /// <summary>Number of distinct blocks in which this text appeared.</summary>
    [JsonPropertyName("block_count")]
    public int BlockCount { get; set; }

    /// <summary>Distinct syntactic roles observed for this text (highest-priority first).</summary>
    [JsonPropertyName("roles")]
    public List<string> Roles { get; set; } = new();

    /// <summary>Sample evidence strings (capped), e.g. "head_rel=dobj; governing_verb=进行".</summary>
    [JsonPropertyName("evidence")]
    public List<string> Evidence { get; set; } = new();
}

/// <summary>
/// Document-level entity quality assessment combining F3 (cross-block frequency
/// demotion) and F4 (cross-signal validation). Pure aggregation over block-level
/// results; no I/O.
/// </summary>
public class DocumentEntityQuality
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    /// <summary>F3: number of times this entity text appears across the document (deduplicated by text).</summary>
    [JsonPropertyName("occurrences")]
    public int Occurrences { get; set; }

    /// <summary>F4 signal: appears in the aggregated noun-signal list.</summary>
    [JsonPropertyName("in_noun_signals")]
    public bool InNounSignals { get; set; }

    /// <summary>F4 signal: appears as a Subject/Object (core) component in the dependency parse.</summary>
    [JsonPropertyName("in_dep_core")]
    public bool InDepCore { get; set; }

    /// <summary>F4: count of supporting signals (1 = entity only; +1 noun; +1 dep core). Range 1-3.</summary>
    [JsonPropertyName("signal_support")]
    public int SignalSupport { get; set; }

    /// <summary>F4: boosted by multi-evidence (present in noun signals AND dep core).</summary>
    [JsonPropertyName("boosted")]
    public bool Boosted { get; set; }

    /// <summary>F3: flagged as high-frequency noise (occurrences &gt;= threshold).</summary>
    [JsonPropertyName("is_high_frequency_noise")]
    public bool IsHighFrequencyNoise { get; set; }

    /// <summary>Final keep decision (false = demoted by F3/F4/block-level).</summary>
    [JsonPropertyName("keep")]
    public bool Keep { get; set; } = true;

    /// <summary>Primary demotion tag (null when kept), e.g. "high_frequency_noise".</summary>
    [JsonPropertyName("filter")]
    public string? Filter { get; set; }

    /// <summary>Audit trail of every quality decision applied.</summary>
    [JsonPropertyName("reasons")]
    public List<string> Reasons { get; set; } = new();
}

/// <summary>
/// Result of document-level aggregation (Step C noun-signal aggregation + F3
/// frequency demotion + F4 cross-signal validation).
/// </summary>
public class DocumentAggregationResult
{
    [JsonPropertyName("total_blocks")]
    public int TotalBlocks { get; set; }

    [JsonPropertyName("total_entities")]
    public int TotalEntities { get; set; }

    [JsonPropertyName("total_noun_signals")]
    public int TotalNounSignals { get; set; }

    /// <summary>Step C: document-level noun signals, sorted by (role priority, best score, occurrences).</summary>
    [JsonPropertyName("noun_signals")]
    public List<DocumentNounSignal> NounSignals { get; set; } = new();

    /// <summary>F3 + F4: per-entity-text quality assessment, kept entities first.</summary>
    [JsonPropertyName("entity_quality")]
    public List<DocumentEntityQuality> EntityQuality { get; set; } = new();
}

public class SemanticToken
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("pos")]
    public string Pos { get; set; } = string.Empty;

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("span")]
    public int[] Span { get; set; } = new int[2];

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;
}

/// <summary>
/// Represents an entity extracted from NLP analysis.
/// </summary>
public class SemanticEntity
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("normalized")]
    public string Normalized { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("span")]
    public int[] Span { get; set; } = new int[2];

    [JsonPropertyName("attributes")]
    public List<HanLPEntityAttribute> Attributes { get; set; } = new();

    [JsonPropertyName("syntactic_role")]
    public string SyntacticRole { get; set; } = "";

    [JsonPropertyName("governing_verb")]
    public string? GoverningVerb { get; set; }

    /// <summary>
    /// Spatial role from NLP events: CONTAINER, TARGET, ORIGIN, PATH, ACTOR, MODIFIER, STATIC.
    /// </summary>
    [JsonPropertyName("spatial_role")]
    public string? SpatialRole { get; set; }

    /// <summary>
    /// Verb spatial class: MOVE_TO, MOVE_FROM, MOVE_ALONG, STATIC_EXIST, STATIC_ACTION, VIEW, NONE.
    /// </summary>
    [JsonPropertyName("verb_spatial_class")]
    public string? VerbSpatialClass { get; set; }

    /// <summary>
    /// Whether downstream should consume this entity. False = soft-demoted by the NLP
    /// quality pipeline (F1/F2). Defaults to true for backward compatibility.
    /// </summary>
    [JsonPropertyName("keep")]
    public bool Keep { get; set; } = true;

    /// <summary>
    /// Which quality layer demoted this entity (e.g. "F1_shape", "F2_confidence"). Null when kept.
    /// </summary>
    [JsonPropertyName("filter")]
    public string? Filter { get; set; }

    /// <summary>
    /// Human-readable, auditable reason for demotion. Null when kept.
    /// </summary>
    [JsonPropertyName("filter_reason")]
    public string? FilterReason { get; set; }

    /// <summary>
    /// Chinese display name for the syntactic role (for LOCATION entities).
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string SyntacticRoleDisplay => LocationSyntacticRole.ToDisplay(SyntacticRole);

    /// <summary>
    /// Enriched metadata from entity processors (Location, Person, Organization, Temporal).
    /// Stored as a nested dictionary for JSON serialization compatibility.
    /// Key format: "{EntityType}:{stage}" (e.g., "LOCATION:enrichment", "PERSON:validation").
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Dictionary<string, object> EnrichedMetadata { get; set; } = new();

    // ── Enrichment computed properties (binding-friendly for UI) ───

    /// <summary>Best display text: enriched name if available, otherwise raw text.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string EnrichedDisplayText =>
        GetEnrichmentString("enriched_text") is { Length: > 0 } s ? s : Text;

    /// <summary>Confidence from the enrichment stage (0.0–1.0), or -1 if not enriched.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public double EnrichedConfidence
    {
        get
        {
            var e = GetEnrichmentDict();
            return e != null && e.TryGetValue("confidence", out var c) && c is double d ? d : -1;
        }
    }

    /// <summary>Whether the entity passed pattern validation.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsValidated
    {
        get
        {
            var v = GetValidationDict();
            return v != null && v.TryGetValue("is_valid", out var iv) && iv is bool b && b;
        }
    }

    /// <summary>Validation status: "valid", "ambiguous", "invalid", or "unknown".</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string ValidationStatus
    {
        get
        {
            var v = GetValidationDict();
            if (v == null) return "unknown";
            if (v.TryGetValue("is_valid", out var iv) && iv is bool bv && !bv) return "invalid";
            if (v.TryGetValue("severity", out var sv) && sv is string s && s == "Warning") return "ambiguous";
            return "valid";
        }
    }

    /// <summary>Source of enrichment (e.g., "Nominatim", "Wikidata", "EraTable").</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string EnrichmentSource => GetEnrichmentString("source") ?? "";

    /// <summary>External ID from enrichment (Wikidata Q-ID, OSM node/way ID).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string ExternalId => GetEnrichmentString("external_id") ?? "";

    /// <summary>One-line enrichment summary for tooltip display.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string EnrichmentSummary
    {
        get
        {
            var parts = new System.Collections.Generic.List<string>();
            if (EnrichmentSource is { Length: > 0 } s) parts.Add(s);
            if (EnrichedConfidence >= 0) parts.Add($"{(EnrichedConfidence * 100):F0}%");
            if (ExternalId is { Length: > 0 } eid) parts.Add(eid);
            if (ValidationStatus == "ambiguous") parts.Add("\u26a0");
            return parts.Count > 0 ? string.Join(" \u00b7 ", parts) : Text;
        }
    }

    // ── Private enrichment helpers ─────────────────────────────────

    private Dictionary<string, object>? GetValidationDict()
    {
        var key = $"{Category}:validation";
        return EnrichedMetadata.TryGetValue(key, out var v) && v is Dictionary<string, object> d ? d : null;
    }

    private Dictionary<string, object>? GetEnrichmentDict()
    {
        var key = $"{Category}:enrichment";
        return EnrichedMetadata.TryGetValue(key, out var v) && v is Dictionary<string, object> d ? d : null;
    }

    private string? GetEnrichmentString(string field)
    {
        var e = GetEnrichmentDict();
        return e != null && e.TryGetValue(field, out var v) && v is string s ? s : null;
    }
}

/// <summary>
/// Represents a relation/triple extracted from NLP analysis.
/// </summary>
public class SemanticRelation
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("subject")]
    public string Subject { get; set; } = string.Empty;

    [JsonPropertyName("predicate")]
    public string Predicate { get; set; } = string.Empty;

    [JsonPropertyName("predicate_verb")]
    public string PredicateVerb { get; set; } = string.Empty;

    [JsonPropertyName("object")]
    public string ObjectText { get; set; } = string.Empty;

    [JsonPropertyName("evidence")]
    public string Evidence { get; set; } = string.Empty;

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;
}

/// <summary>
/// Represents a term definition extracted from text patterns (e.g., "X是Y", "X者Y也").
/// Used by TermDefinitionExtractor and consumed by TermProcessor.
/// </summary>
public class TermDefinition
{
    /// <summary>The term being defined (e.g., "丞相").</summary>
    [JsonPropertyName("term")]
    public string TermText { get; set; } = string.Empty;

    /// <summary>The definition or hypernym (e.g., "百官之长").</summary>
    [JsonPropertyName("definition")]
    public string DefinitionText { get; set; } = string.Empty;

    /// <summary>Relation type: "IS_A" (hypernym), "SYNONYM" (alternate name).</summary>
    [JsonPropertyName("relation_type")]
    public string RelationType { get; set; } = "IS_A";

    /// <summary>Confidence score (0.0–1.0).</summary>
    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    /// <summary>The matched evidence text.</summary>
    [JsonPropertyName("evidence")]
    public string Evidence { get; set; } = string.Empty;

    /// <summary>Character span of the evidence in the source text.</summary>
    [JsonPropertyName("evidence_span")]
    public int[] EvidenceSpan { get; set; } = new int[2];

    /// <summary>Source of extraction: "pattern" or "dependency".</summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = "pattern";

    /// <summary>Cross-block occurrence count (set during cross-block resolution).</summary>
    [JsonPropertyName("cross_block_count")]
    public int CrossBlockCount { get; set; } = 1;

    /// <summary>Whether this is an alternate (lower-confidence) definition.</summary>
    [JsonPropertyName("is_alternate")]
    public bool IsAlternateDefinition { get; set; }

    /// <summary>Primary definition text if this is an alternate.</summary>
    [JsonPropertyName("primary_definition")]
    public string? PrimaryDefinition { get; set; }
}

/// <summary>
/// Represents the NLP analysis result for a single Popo block/node.
/// </summary>
public class SemanticBlockResult
{
    [JsonPropertyName("block_ids")]
    public List<int> BlockIds { get; set; } = new();

    [JsonPropertyName("source_block_ids")]
    public List<string> SourceBlockIds { get; set; } = new();

    /// <summary>
    /// Section path (e.g. "第5章 总则 &gt; 5.13 绝缘电阻试验") that was prepended to
    /// the NLP request text as a 【...】 prefix. Null when no prefix was applied.
    /// </summary>
    [JsonPropertyName("section_path")]
    public string? SectionPath { get; set; }

    /// <summary>
    /// Length of the 【section path】 prefix prepended to the NLP request text.
    /// Entity/token spans are relative to the prefixed text; subtract this value
    /// to map them back onto <see cref="Content"/> (which stores the original text).
    /// </summary>
    [JsonPropertyName("prefix_len")]
    public int PrefixLen { get; set; }

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("tokens")]
    public List<SemanticToken> Tokens { get; set; } = new();

    [JsonPropertyName("entities")]
    public List<SemanticEntity> Entities { get; set; } = new();

    [JsonPropertyName("relations")]
    public List<SemanticRelation> Relations { get; set; } = new();

    /// <summary>
    /// Dependency parsing tokens from /analyze/dep endpoint.
    /// Saved for later syntactic role annotation of LOCATION entities.
    /// </summary>
    [JsonPropertyName("dep_tokens")]
    public List<SemanticDepToken> DepTokens { get; set; } = new();

    /// <summary>
    /// Dependency parsing edges from /analyze/dep endpoint.
    /// Saved for later syntactic role annotation of LOCATION entities.
    /// </summary>
    [JsonPropertyName("dep_edges")]
    public List<SemanticDepEdge> DepEdges { get; set; } = new();

    /// <summary>
    /// Location relationships extracted within this block from dependency parsing.
    /// Includes ParentChild (nn), Ownership (assmod/assm), and Parallel (conj) relations.
    /// </summary>
    [JsonPropertyName("location_relations")]
    public List<LocationRelation> LocationRelations { get; set; } = new();

    /// <summary>
    /// Term definitions extracted from this block by TermDefinitionExtractor.
    /// Includes IS_A (hypernym) and SYNONYM (alternate name) relations.
    /// </summary>
    [JsonPropertyName("term_definitions")]
    public List<TermDefinition> TermDefinitions { get; set; } = new();

    /// <summary>
    /// Block-level noun-phrase signals from the NLP (Step A POS gating + Step B
    /// syntactic role). Not entities — linguistic candidates for caller-side
    /// aggregation, clustering, and audit (see entity-extraction-quality-spec §6).
    /// </summary>
    [JsonPropertyName("noun_signals")]
    public List<SemanticNounSignal> NounSignals { get; set; } = new();

    /// <summary>
    /// Block-level enriched metadata from entity processors.
    /// Key format: "{EntityType}:relations" → list of relation dicts for non-LOCATION types.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Dictionary<string, object> EnrichedMetadata { get; set; } = new();

    /// <summary>
    /// Temporary storage for HanLP coreference chains from a single block's NLP response.
    /// Used during ProcessAllNodesAsync for cross-block coreference resolution.
    /// Not serialized to semantic_result.json.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    internal List<HanLPCorefChain>? CorefChains { get; set; }

    /// <summary>
    /// Formulas extracted from this block before NLP analysis.
    /// Contains the original formula content and placeholder mapping.
    /// Used for restoring formulas after NLP analysis and for formula metadata.
    /// Not serialized to semantic_result.json (stored in EnrichedMetadata instead).
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public ExtractedFormulas? ExtractedFormulas { get; set; }

    /// <summary>
    /// Lightweight formula references for serialization.
    /// Only stores formula IDs and placeholders, not full formula content.
    /// This keeps semantic_result.json small while maintaining traceability.
    /// </summary>
    [JsonPropertyName("formula_references")]
    public List<FormulaReference> FormulaReferences { get; set; } = new();

    /// <summary>
    /// Restores the original content with formulas (replaces placeholders with actual formulas).
    /// Returns the original content if no formulas were extracted.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string RestoredContent
    {
        get
        {
            if (ExtractedFormulas is null || ExtractedFormulas.Formulas.Count == 0)
                return Content;
            return FormulaExtractor.RestoreFormulas(Content, ExtractedFormulas);
        }
    }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    /// <summary>
    /// Whether this block has expandable details (tokens or relations).
    /// Used to show/hide the collapsible details Expander in the UI.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasExpandableDetails => Tokens.Count > 0 || Relations.Count > 0;

    /// <summary>
    /// Whether this block has dependency parsing data to display.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasDepTree => DepTokens.Count > 0 && DepEdges.Count > 0;

    /// <summary>
    /// Whether this block has a title or type to display in the header.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasTitleOrType => !string.IsNullOrEmpty(Title) || !string.IsNullOrEmpty(Type);

    /// <summary>
    /// Whether this block has content to preview.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasContent => !string.IsNullOrEmpty(Content);

    /// <summary>
    /// Whether this block has any entities.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasEntities => Entities.Count > 0;

    /// <summary>
    /// Content preview (truncated to 100 chars).
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string ContentPreview
    {
        get
        {
            if (string.IsNullOrEmpty(Content))
                return string.Empty;
            return Content.Length > 100 ? Content.Substring(0, 100) + "..." : Content;
        }
    }

    /// <summary>
    /// Summary string for entities: "3 地点, 2 时间" etc.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string EntitySummary
    {
        get
        {
            if (Entities.Count == 0)
                return string.Empty;

            var categoryCounts = new Dictionary<string, int>();
            foreach (var entity in Entities)
            {
                if (!categoryCounts.ContainsKey(entity.Category))
                    categoryCounts[entity.Category] = 0;
                categoryCounts[entity.Category]++;
            }

            return string.Join(", ", categoryCounts.Select(kvp => $"{kvp.Value} {kvp.Key}"));
        }
    }

    /// <summary>
    /// Type color for the badge (balanced contrast colors matching entity colors).
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string TypeColorHex
    {
        get
        {
            return Type switch
            {
                "text" => "#4CAF50",
                "page_number" => "#78909C",
                "image" => "#2196F3",
                "image_footnote" => "#2196F3",
                "table" => "#FF9800",
                "title" => "#9C27B0",
                "root" => "#8D6E63",
                _ => "#607D8B",
            };
        }
    }

    /// <summary>
    /// LOCATION entities.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public List<SemanticEntity> LocationEntities
    {
        get => Entities.Where(e => e.Category == "LOCATION").ToList();
    }

    /// <summary>
    /// DATE entities.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public List<SemanticEntity> DateEntities
    {
        get => Entities.Where(e => e.Category == "DATE").ToList();
    }

    /// <summary>
    /// PERSON entities.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public List<SemanticEntity> PersonEntities
    {
        get => Entities.Where(e => e.Category == "PERSON").ToList();
    }

    /// <summary>
    /// NUMBER entities.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public List<SemanticEntity> NumberEntities
    {
        get => Entities.Where(e => e.Category == "NUMBER").ToList();
    }

    /// <summary>
    /// FACILITY entities.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public List<SemanticEntity> FacilityEntities
    {
        get => Entities.Where(e => e.Category == "FACILITY").ToList();
    }

    /// <summary>
    /// ORGANIZATION entities.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public List<SemanticEntity> OrganizationEntities
    {
        get => Entities.Where(e => e.Category == "ORGANIZATION").ToList();
    }

    /// <summary>
    /// Other entities (UNKNOWN, MATERIAL, etc.).
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public List<SemanticEntity> OtherEntities
    {
        get
        {
            var knownCategories = new HashSet<string>
            {
                "LOCATION", "DATE", "PERSON", "NUMBER", "FACILITY", "ORGANIZATION"
            };
            return Entities.Where(e => !knownCategories.Contains(e.Category)).ToList();
        }
    }
}

/// <summary>
/// Container for all semantic analysis results of a document.
/// Saved as semantic_result.json in the semantic directory.
/// </summary>
public class SemanticResultFile
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; set; } = "hanlp_v2";

    [JsonPropertyName("blocks")]
    public List<SemanticBlockResult> Blocks { get; set; } = new();

    /// <summary>
    /// Cross-block location relationships (coreferences) extracted from HanLP coreference chains.
    /// These span multiple blocks and are resolved after all blocks are processed.
    /// </summary>
    [JsonPropertyName("cross_block_relations")]
    public List<LocationRelation> CrossBlockRelations { get; set; } = new();

    /// <summary>
    /// Formula index for this document.
    /// Contains all formulas extracted during semantic analysis.
    /// Can also be loaded from semantic/formula_index.json.
    /// </summary>
    [JsonPropertyName("formula_index")]
    public FormulaIndex? FormulaIndex { get; set; }

    /// <summary>
    /// Document-level aggregation (Step C noun signals + F3/F4 entity quality),
    /// computed after all blocks are analyzed. See <c>DocumentSignalAggregator</c>.
    /// </summary>
    [JsonPropertyName("document_aggregation")]
    public DocumentAggregationResult? DocumentAggregation { get; set; }

    /// <summary>
    /// SECTION_REF anchor table: the document TOC entries (numbering → title/page),
    /// persisted for resolving "见 5.13"-style section references.
    /// Null for results produced before this field existed.
    /// </summary>
    [JsonPropertyName("section_anchors")]
    public List<TocEntry>? SectionAnchors { get; set; }
}