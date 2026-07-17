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
    public List<string> Attributes { get; set; } = new();

    [JsonPropertyName("syntactic_role")]
    public string SyntacticRole { get; set; } = "";

    [JsonPropertyName("governing_verb")]
    public string? GoverningVerb { get; set; }

    /// <summary>
    /// Chinese display name for the syntactic role (for LOCATION entities).
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string SyntacticRoleDisplay => LocationSyntacticRole.ToDisplay(SyntacticRole);
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
/// Represents the NLP analysis result for a single Popo block/node.
/// </summary>
public class SemanticBlockResult
{
    [JsonPropertyName("block_ids")]
    public List<int> BlockIds { get; set; } = new();

    [JsonPropertyName("source_block_ids")]
    public List<string> SourceBlockIds { get; set; } = new();

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

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    /// <summary>
    /// Whether this block has expandable details (tokens or relations).
    /// Used to show/hide the collapsible details Expander in the UI.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasExpandableDetails => Tokens.Count > 0 || Relations.Count > 0;

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
}