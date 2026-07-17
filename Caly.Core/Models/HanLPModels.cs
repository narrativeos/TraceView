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
/// Request model for the Narrative Operator NLP API (POST /analyze).
/// </summary>
public class HanLPAnalyzeRequest
{
    /// <summary>
    /// Raw text to analyze (required).
    /// </summary>
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    /// <summary>
    /// NLP engine identifier (default: "hanlp_v2").
    /// </summary>
    [JsonPropertyName("source")]
    public string? Source { get; set; }

    /// <summary>
    /// Language for analysis.
    /// </summary>
    [JsonPropertyName("language")]
    public string? Language { get; set; }

    /// <summary>
    /// Custom dictionary words to force-combine during tokenization.
    /// </summary>
    [JsonPropertyName("dict_combine")]
    public List<string>? DictCombine { get; set; }

    /// <summary>
    /// Enable new word discovery.
    /// </summary>
    [JsonPropertyName("discover")]
    public bool? Discover { get; set; }

    /// <summary>
    /// Auto-apply discovered new words and re-analyze.
    /// </summary>
    [JsonPropertyName("enhance")]
    public bool? Enhance { get; set; }

    /// <summary>
    /// Optional entity dictionary for classical Chinese.
    /// </summary>
    [JsonPropertyName("entity_dict")]
    public Dictionary<string, string>? EntityDict { get; set; }

    /// <summary>
    /// Domain-specific keyword injection.
    /// </summary>
    public Dictionary<string, List<string>>? EntityCategories { get; set; }

    /// <summary>
    /// Run new word discovery and promote high-score candidates.
    /// </summary>
    public bool? AutoDiscoverEntities { get; set; }
}

/// <summary>
/// Response wrapper from the NLP API.
/// </summary>
public class HanLPAnalyzeResponse
{
    [JsonPropertyName("meta")]
    public Dictionary<string, object>? Meta { get; set; }
    
    [JsonPropertyName("content")]
    public HanLPContent? Content { get; set; }
    
    [JsonPropertyName("true_new_words")]
    public List<string>? TrueNewWords { get; set; }
}

/// <summary>
/// The content of the NLP analysis result.
/// </summary>
public class HanLPContent
{
    [JsonPropertyName("tokens")]
    public List<HanLPToken>? Tokens { get; set; }
    
    [JsonPropertyName("entities")]
    public List<HanLPEntity>? Entities { get; set; }
    
    [JsonPropertyName("relations")]
    public List<HanLPRelation>? Relations { get; set; }
    
    [JsonPropertyName("patterns")]
    public List<HanLPPattern>? Patterns { get; set; }
    
    [JsonPropertyName("coreferences")]
    public List<HanLPCorefChain>? Coreferences { get; set; }
    
    [JsonPropertyName("sentences")]
    public List<HanLPSentence>? Sentences { get; set; }
    
    [JsonPropertyName("structural")]
    public Dictionary<string, object>? Structural { get; set; }
    
    /// <summary>
    /// Dependency parsing edges from /analyze endpoint (merged in backend).
    /// Token IDs reference the tokens array in this same content object.
    /// </summary>
    [JsonPropertyName("deps")]
    public List<HanLPDepEdge>? Deps { get; set; }
}

/// <summary>
/// Token from HanLP analysis.
/// </summary>
public class HanLPToken
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";
    
    [JsonPropertyName("pos")]
    public string Pos { get; set; } = "";
    
    [JsonPropertyName("span")]
    public List<int> Span { get; set; } = new();
    
    [JsonPropertyName("source")]
    public string Source { get; set; } = "";
    
    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }
}

/// <summary>
/// Named entity from HanLP analysis.
/// </summary>
public class HanLPEntity
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";
    
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";
    
    [JsonPropertyName("category")]
    public string Category { get; set; } = "";
    
    [JsonPropertyName("span")]
    public List<int> Span { get; set; } = new();
    
    [JsonPropertyName("normalized")]
    public string Normalized { get; set; } = "";
    
    [JsonPropertyName("source")]
    public string Source { get; set; } = "";
    
    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }
    
    [JsonPropertyName("attributes")]
    public List<HanLPEntityAttribute>? Attributes { get; set; }
    
    [JsonPropertyName("parent_entity_id")]
    public string? ParentEntityId { get; set; }
    
    [JsonPropertyName("evidence")]
    public HanLPEvidence? Evidence { get; set; }
}

public class HanLPEntityAttribute
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";
    
    [JsonPropertyName("value")]
    public string Value { get; set; } = "";
    
    [JsonPropertyName("predicate_verb")]
    public string PredicateVerb { get; set; } = "";
    
    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }
    
    [JsonPropertyName("source_relation_id")]
    public string? SourceRelationId { get; set; }
}

public class HanLPEvidence
{
    [JsonPropertyName("tokenizer")]
    public bool Tokenizer { get; set; }
    
    [JsonPropertyName("seed_dict")]
    public bool SeedDict { get; set; }
    
    [JsonPropertyName("seed_dict_source")]
    public string SeedDictSource { get; set; } = "";
    
    [JsonPropertyName("cbdp")]
    public bool Cbdp { get; set; }
    
    [JsonPropertyName("cbdp_category")]
    public string CbdpCategory { get; set; } = "";
}

/// <summary>
/// Relation triple from HanLP analysis.
/// </summary>
public class HanLPRelation
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";
    
    [JsonPropertyName("subject")]
    public string Subject { get; set; } = "";
    
    [JsonPropertyName("subject_raw")]
    public string SubjectRaw { get; set; } = "";
    
    [JsonPropertyName("subject_ent_id")]
    public string? SubjectEntId { get; set; }
    
    [JsonPropertyName("predicate")]
    public string Predicate { get; set; } = "";
    
    [JsonPropertyName("predicate_verb")]
    public string PredicateVerb { get; set; } = "";
    
    [JsonPropertyName("object")]
    public string Object { get; set; } = "";
    
    [JsonPropertyName("object_raw")]
    public string ObjectRaw { get; set; } = "";
    
    [JsonPropertyName("object_ent_id")]
    public string? ObjectEntId { get; set; }
    
    [JsonPropertyName("evidence")]
    public string Evidence { get; set; } = "";
    
    [JsonPropertyName("evidence_span")]
    public List<int> EvidenceSpan { get; set; } = new();
    
    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }
    
    [JsonPropertyName("source")]
    public string Source { get; set; } = "";
    
    [JsonPropertyName("modifiers")]
    public List<HanLPModifier>? Modifiers { get; set; }
}

public class HanLPModifier
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";
    
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";
    
    [JsonPropertyName("span")]
    public List<int> Span { get; set; } = new();
    
    [JsonPropertyName("matched_dict")]
    public string MatchedDict { get; set; } = "";
}

/// <summary>
/// Sentence pattern from HanLP analysis.
/// </summary>
public class HanLPPattern
{
    public string Sentence { get; set; } = "";
    public string SentenceType { get; set; } = "";
    public string StructuralType { get; set; } = "";
    public string Polarity { get; set; } = "";
    public string Voice { get; set; } = "";
    public List<string> SubTypes { get; set; } = new();
    public string RhetoricalForm { get; set; } = "";
    public string SentenceLengthTier { get; set; } = "";
    public string Template { get; set; } = "";
    public List<string> EntitySequence { get; set; } = new();
    public List<string> Predicates { get; set; } = new();
    public List<string> RelationSummary { get; set; } = new();
    public int AttributeCount { get; set; }
    public int WordCount { get; set; }
    public int ClauseCount { get; set; }
    public string PunctuationMark { get; set; } = "";
    public List<string> Limitations { get; set; } = new();
}

/// <summary>
/// Coreference chain from HanLP analysis.
/// </summary>
public class HanLPCorefChain
{
    public string ChainId { get; set; } = "";
    public List<HanLPCorefMention> Mentions { get; set; } = new();
    public string Representative { get; set; } = "";
    public double Confidence { get; set; }
    public string Language { get; set; } = "";
    public string QualityFlag { get; set; } = "";
    public string ModelVersion { get; set; } = "";
}

public class HanLPCorefMention
{
    public string Text { get; set; } = "";
    public List<int> Span { get; set; } = new();
    public string MentionType { get; set; } = "";
    public string? EntityId { get; set; }
    public bool IsPrincipal { get; set; }
}

/// <summary>
/// Sentence segmentation result.
/// </summary>
public class HanLPSentence
{
    public string Text { get; set; } = "";
    public List<int> Span { get; set; } = new();
    public string Label { get; set; } = "";
    public double Confidence { get; set; }
}

// =============================================================================
// Dependency Parsing Response Models (from /analyze/dep endpoint)
// =============================================================================

/// <summary>
/// Response from the /analyze/dep endpoint.
/// Contains token list + dependency edges for syntactic role analysis.
/// </summary>
public class HanLPDepResponse
{
    [JsonPropertyName("tokens")]
    public List<HanLPDepToken> Tokens { get; set; } = new();

    [JsonPropertyName("deps")]
    public List<HanLPDepEdge> Deps { get; set; } = new();

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";
}

/// <summary>
/// A single token in the dependency parsing result.
/// </summary>
public class HanLPDepToken
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("pos")]
    public string Pos { get; set; } = "";
}

/// <summary>
/// A single dependency edge in the parsing result.
/// Child token depends on Head token with the given relation.
/// </summary>
public class HanLPDepEdge
{
    [JsonPropertyName("child")]
    public int Child { get; set; }

    [JsonPropertyName("head")]
    public int Head { get; set; }

    [JsonPropertyName("rel")]
    public string Rel { get; set; } = "";
}
