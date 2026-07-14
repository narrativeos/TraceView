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


//// Note: AnalysisTreeNode is defined in PopoTreeNode.cs as a partial class.
//// We do NOT redefine it here to avoid ambiguity errors.

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

    [JsonPropertyName("error")]
    public string? Error { get; set; }
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