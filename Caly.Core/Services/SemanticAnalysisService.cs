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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Caly.Core.Models;

namespace Caly.Core.Services;

/// <summary>
/// NLP analysis service that calls the Narrative Operator NLP API (HanLP).
/// Default base URL: http://localhost:8000
/// </summary>
public sealed class SemanticAnalysisService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    public SemanticAnalysisService(string? baseUrl = null)
    {
        _baseUrl = baseUrl ?? "http://localhost:8000";
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_baseUrl),
            Timeout = TimeSpan.FromSeconds(60)
        };
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    /// <summary>
    /// Removes OCR connection markers from text before sending to HanLP.
    /// Removes patterns like <|txt_split|>, <|line_break|>, etc.
    /// </summary>
    private static string CleanOcrMarkers(string text)
    {
        // Remove <|...|> style markers (e.g., <|txt_split|>, <|line_break|>)
        return Regex.Replace(text, "<\\|[^\\|]*\\|>", string.Empty);
    }

    /// <summary>
    /// Analyzes a single text block using the HanLP API.
    /// Calls /analyze once (deps are now merged into the response),
    /// saves dependency data to the result, and annotates LOCATION entities with syntactic roles.
    /// </summary>
    public async Task<SemanticBlockResult> AnalyzeAsync(
        AnalysisTreeNode node,
        CancellationToken cancellationToken = default)
    {
        System.Diagnostics.Debug.WriteLine($"[SemanticAnalysisService] Analyzing node with {node.Content.Length} chars");
        
        var cleanText = CleanOcrMarkers(node.Content);
        var request = new HanLPAnalyzeRequest
        {
            Text = cleanText,
            Source = "hanlp_v2",
            Language = "auto"
        };

        var json = JsonSerializer.Serialize(request, SourceGenerationContext.Default.HanLPAnalyzeRequest);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        System.Diagnostics.Debug.WriteLine($"[SemanticAnalysisService] POSTing to {_baseUrl}/analyze");
        var response = await _httpClient.PostAsync("analyze", content, cancellationToken);

        System.Diagnostics.Debug.WriteLine($"[SemanticAnalysisService] /analyze Response status: {response.StatusCode}");
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            System.Diagnostics.Debug.WriteLine($"[SemanticAnalysisService] Error response: {responseJson.Substring(0, Math.Min(300, responseJson.Length))}");
        }
        response.EnsureSuccessStatusCode();
        System.Diagnostics.Debug.WriteLine($"[SemanticAnalysisService] Response length: {responseJson.Length} chars");

        var analyzeResponse = JsonSerializer.Deserialize(responseJson, SourceGenerationContext.Default.HanLPAnalyzeResponse);

        var result = MapToSemanticResult(node, analyzeResponse);

        // Extract dependency parsing data from the /analyze response (merged in backend)
        var content_obj = analyzeResponse?.Content;
        if (content_obj?.Deps is not null && content_obj?.Tokens is not null)
        {
            // Save dep tokens (from content.tokens, which has id, text, pos)
            result.DepTokens = content_obj.Tokens.Select(t => new SemanticDepToken
            {
                Id = t.Id,
                Text = t.Text,
                Pos = t.Pos
            }).ToList();

            // Save dep edges
            result.DepEdges = content_obj.Deps.Select(d => new SemanticDepEdge
            {
                Child = d.Child,
                Head = d.Head,
                Rel = d.Rel
            }).ToList();

            System.Diagnostics.Debug.WriteLine($"[SemanticAnalysisService] Saved {result.DepTokens.Count} dep tokens, {result.DepEdges.Count} dep edges from /analyze response");

            // Annotate LOCATION entities with syntactic roles
            AnnotateLocationSyntacticRolesFromContent(content_obj.Tokens, content_obj.Deps, result.LocationEntities);
        }
        else if (content_obj?.Deps is null)
        {
            System.Diagnostics.Debug.WriteLine("[SemanticAnalysisService] No deps in /analyze response");
        }

        return result;
    }

    /// <summary>
    /// Analyzes multiple nodes in sequence.
    /// </summary>
    public async Task<SemanticResultFile> ProcessAllNodesAsync(
        List<AnalysisTreeNode> nodes,
        string outputDir,
        Action<int, int> progressCallback,
        CancellationToken cancellationToken = default)
    {
        var blocks = new List<SemanticBlockResult>();

        for (int i = 0; i < nodes.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var blockResult = await AnalyzeAsync(nodes[i], cancellationToken);
                blocks.Add(blockResult);
            }
            catch (Exception ex)
            {
                // Store error result for this node
                blocks.Add(new SemanticBlockResult
                {
                    SourceBlockIds = nodes[i].SourceBlockIds,
                    Content = nodes[i].Content,
                    Error = ex.Message
                });
            }

            progressCallback(i, nodes.Count);
        }

        var fileResult = new SemanticResultFile
        {
            Version = "1.0",
            Timestamp = DateTime.UtcNow.ToString("O"),
            Source = "hanlp_v2",
            Blocks = blocks
        };

        // Save to disk
        Directory.CreateDirectory(outputDir);
        var filePath = Path.Combine(outputDir, "semantic_result.json");
        var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
        options.TypeInfoResolver = SourceGenerationContext.Default;
        var json = JsonSerializer.Serialize(fileResult, options);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);

        return fileResult;
    }

    /// <summary>
    /// Loads a previously saved semantic analysis result.
    /// </summary>
    public static SemanticResultFile? LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        var json = File.ReadAllText(filePath);
        
        // Try source-generated deserialization first (fastest)
        try
        {
            var result = JsonSerializer.Deserialize(json, SourceGenerationContext.Default.SemanticResultFile);
            System.Diagnostics.Debug.WriteLine($"[SemanticAnalysisService] LoadFromFile (source-gen) succeeded: {result?.Blocks.Count ?? 0} blocks");
            return result;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SemanticAnalysisService] Source-gen deserialization failed: {ex.Message}");
        }
        
        // Fallback to reflection-based deserialization (slower but more tolerant)
        try
        {
            var options = new System.Text.Json.JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true 
            };
            var result = JsonSerializer.Deserialize(json, typeof(SemanticResultFile), options) as SemanticResultFile;
            System.Diagnostics.Debug.WriteLine($"[SemanticAnalysisService] LoadFromFile (fallback) succeeded: {result?.Blocks.Count ?? 0} blocks");
            return result;
        }
        catch (Exception ex2)
        {
            System.Diagnostics.Debug.WriteLine($"[SemanticAnalysisService] Fallback deserialization also failed: {ex2.Message}");
            return null;
        }
    }

    /// <summary>
    /// Maps HanLP API response to our SemanticBlockResult.
    /// </summary>
    private static SemanticBlockResult MapToSemanticResult(AnalysisTreeNode node, HanLPAnalyzeResponse? response)
    {
        var result = new SemanticBlockResult
        {
            SourceBlockIds = node.SourceBlockIds,
            Content = node.Content,
            Type = node.Type,
            Title = node.Title
        };

        if (response?.Content is null)
        {
            result.Error = "Empty response from NLP API";
            return result;
        }

        var content = response.Content;

        // Map tokens
        if (content.Tokens is not null)
        {
            result.Tokens = new List<SemanticToken>();
            foreach (var token in content.Tokens)
            {
                result.Tokens.Add(new SemanticToken
                {
                    Text = token.Text,
                    Pos = token.Pos,
                    Confidence = token.Confidence,
                    Span = token.Span.Count >= 2 
                        ? new int[] { token.Span[0], token.Span[1] } 
                        : new int[] { 0, token.Text.Length },
                    Source = token.Source
                });
            }
        }

        // Map entities
        if (content.Entities is not null)
        {
            result.Entities = new List<SemanticEntity>();
            foreach (var entity in content.Entities)
            {
                var attributes = new List<string>();
                if (entity.Attributes is not null)
                {
                    foreach (var attr in entity.Attributes)
                    {
                        attributes.Add($"{attr.Key}={attr.Value}");
                    }
                }

                result.Entities.Add(new SemanticEntity
                {
                    Id = entity.Id,
                    Text = entity.Text,
                    Category = entity.Category,
                    Normalized = entity.Normalized,
                    Source = entity.Source,
                    Confidence = entity.Confidence,
                    Span = entity.Span.Count >= 2 
                        ? new int[] { entity.Span[0], entity.Span[1] } 
                        : new int[] { 0, entity.Text.Length },
                    Attributes = attributes
                });
            }
        }

        // Map relations
        if (content.Relations is not null)
        {
            result.Relations = new List<SemanticRelation>();
            foreach (var rel in content.Relations)
            {
                result.Relations.Add(new SemanticRelation
                {
                    Id = rel.Id,
                    Subject = rel.Subject,
                    Predicate = rel.Predicate,
                    ObjectText = rel.Object,
                    PredicateVerb = rel.PredicateVerb,
                    Evidence = rel.Evidence,
                    Confidence = rel.Confidence,
                    Source = rel.Source
                });
            }
        }

        return result;
    }

    /// <summary>
    /// Annotates each LOCATION entity with its syntactic role based on
    /// the dependency parsing data from /analyze response (merged tokens + deps).
    /// Uses HanLPToken (from content.tokens) and HanLPDepEdge (from content.deps).
    /// Matches entities to tokens by span overlap instead of exact text match,
    /// because entities like "北京立方庭" may span multiple tokens ("北京" + "立方庭").
    /// </summary>
    private static void AnnotateLocationSyntacticRolesFromContent(
        List<HanLPToken> tokens,
        List<HanLPDepEdge> deps,
        List<SemanticEntity> locationEntities)
    {
        if (locationEntities.Count == 0 || tokens.Count == 0 || deps.Count == 0)
            return;

        try
        {
            // Build lookup: tokenId -> DepEdge
            var edgeByChild = deps.ToDictionary(d => d.Child, d => d);
            // Build lookup: tokenId -> token
            var tokenById = tokens.ToDictionary(t => t.Id, t => t);

            foreach (var entity in locationEntities)
            {
                // Get entity span boundaries
                int entityStart = entity.Span.Length >= 1 ? entity.Span[0] : 0;
                int entityEnd = entity.Span.Length >= 2 ? entity.Span[1] : entityStart;

                // Find all tokens whose span overlaps with the entity's span (relaxed matching)
                var entityTokens = tokens.Where(t =>
                {
                    if (t.Span.Count < 2) return false;
                    int tokenStart = t.Span[0];
                    int tokenEnd = t.Span[1];
                    // Token overlaps with entity span (either partially or fully contained)
                    return tokenStart < entityEnd && tokenEnd > entityStart;
                }).ToList();

                if (entityTokens.Count == 0)
                {
                    // Fallback 1: try text containment (entity text contains token text or vice versa)
                    entityTokens = tokens.Where(t =>
                        t.Text.Length > 0 && (entity.Text.Contains(t.Text) || t.Text.Contains(entity.Text))
                    ).ToList();
                }

                if (entityTokens.Count == 0)
                {
                    // Fallback 2: exact text match
                    var matchingToken = tokens.FirstOrDefault(t => t.Text == entity.Text);
                    if (matchingToken == null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SemanticAnalysisService] No token matches entity '{entity.Text}' (span [{entityStart},{entityEnd}]), role=Unknown");
                        entity.SyntacticRole = LocationSyntacticRole.Unknown;
                        entity.GoverningVerb = null;
                        continue;
                    }
                    entityTokens = new List<HanLPToken> { matchingToken };
                }

                // Find the external dependency edge for this entity
                var externalEdge = FindExternalDependencyEdge(entityTokens, edgeByChild);
                
                if (externalEdge == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[SemanticAnalysisService] No external dep edge for entity '{entity.Text}', role=Unknown");
                    entity.SyntacticRole = LocationSyntacticRole.Unknown;
                    entity.GoverningVerb = null;
                    continue;
                }

                var representativeToken = tokenById.TryGetValue(externalEdge.Child, out var rt) ? rt : entityTokens[0];

                // Determine the head token
                HanLPToken? headToken = null;
                if (externalEdge.Head >= 0 && tokenById.TryGetValue(externalEdge.Head, out var ht))
                {
                    headToken = ht;
                }

                // Get the head's edge (to check if the head itself is governed by a prep)
                string? headRel = null;
                if (externalEdge.Head >= 0 && edgeByChild.TryGetValue(externalEdge.Head, out var headEdge))
                {
                    headRel = headEdge.Rel;
                }

                // Handle conj (conjunction) - try to inherit role from conjoined elements
                if (externalEdge.Rel == "conj")
                {
                    var conjRole = ResolveConjRole(entityTokens, edgeByChild, tokenById);
                    entity.SyntacticRole = conjRole;
                    entity.GoverningVerb = ExtractGoverningVerb(headToken, headRel, tokenById, edgeByChild);
                    System.Diagnostics.Debug.WriteLine($"[SemanticAnalysisService] Entity '{entity.Text}' (conj, inherited) -> role={conjRole}");
                    continue;
                }

                // Determine syntactic role based on dependency relation
                var role = DetermineSyntacticRole(externalEdge.Rel, headRel, headToken?.Pos, headToken?.Text);
                entity.SyntacticRole = role;

                // Set the governing verb
                entity.GoverningVerb = ExtractGoverningVerb(headToken, headRel, tokenById, edgeByChild);

                System.Diagnostics.Debug.WriteLine($"[SemanticAnalysisService] Entity '{entity.Text}' (span [{entityStart},{entityEnd}], {entityTokens.Count} tokens) -> role={role}, rel={externalEdge.Rel}, head={headToken?.Text}, verb={entity.GoverningVerb}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SemanticAnalysisService] Syntactic role annotation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// For a conj (conjunction) relation, try to find the role of the conjunct partner.
    /// Walks up to the head of the conj, then finds other conj children of that head,
    /// and resolves their external role.
    /// </summary>
    static string ResolveConjRole(
        List<HanLPToken> entityTokens,
        Dictionary<int, HanLPDepEdge> edgeByChild,
        Dictionary<int, HanLPToken> tokenById)
    {
        var entityTokenIds = new HashSet<int>(entityTokens.Select(t => t.Id));
        
        // Find the external edge of any token in the entity
        foreach (var token in entityTokens)
        {
            if (!edgeByChild.TryGetValue(token.Id, out var conjEdge))
                continue;
            if (conjEdge.Rel != "conj")
                continue;
            
            // The head of conj is the other conjunct (or the first one)
            // Walk up from the head to find its external relation
            int headId = conjEdge.Head;
            if (headId < 0 || entityTokenIds.Contains(headId))
                continue;
            
            // Now find the external edge of the head token
            if (edgeByChild.TryGetValue(headId, out var headExternalEdge))
            {
                // If the head's external edge is also conj, keep walking up
                if (headExternalEdge.Rel == "conj")
                {
                    // Walk further up
                    int grandHeadId = headExternalEdge.Head;
                    if (grandHeadId >= 0 && edgeByChild.TryGetValue(grandHeadId, out var grandEdge))
                    {
                        return DetermineSyntacticRole(grandEdge.Rel, null, null, null);
                    }
                }
                return DetermineSyntacticRole(headExternalEdge.Rel, null, null, null);
            }
        }
        
        // Default for conj: Attributive (common pattern: "A和B的C" where A and B are both attributive)
        return LocationSyntacticRole.Attributive;
    }

    /// <summary>
    /// Extracts the governing verb from the head token, walking up through prepositions.
    /// </summary>
    static string? ExtractGoverningVerb(
        HanLPToken? headToken,
        string? headRel,
        Dictionary<int, HanLPToken> tokenById,
        Dictionary<int, HanLPDepEdge> edgeByChild)
    {
        if (headToken == null)
            return null;

        // Direct verb head
        if (IsVerb(headToken.Pos))
            return headToken.Text;

        // Head is a preposition, find the actual verb
        if (headRel == "prep" && headToken.Id >= 0 && edgeByChild.TryGetValue(headToken.Id, out var prepEdge))
        {
            if (prepEdge.Head >= 0 && tokenById.TryGetValue(prepEdge.Head, out var verbToken))
            {
                if (IsVerb(verbToken.Pos))
                    return verbToken.Text;
            }
        }

        // Head is a noun, walk up to find the verb
        if (headToken.Pos.StartsWith("N") && headToken.Id >= 0 && edgeByChild.TryGetValue(headToken.Id, out var nounEdge))
        {
            if (nounEdge.Head >= 0 && tokenById.TryGetValue(nounEdge.Head, out var upperToken))
            {
                if (IsVerb(upperToken.Pos))
                    return upperToken.Text;
            }
        }

        return null;
    }

    /// <summary>
    /// Checks if a POS tag indicates a verb.
    /// </summary>
    static bool IsVerb(string? pos)
    {
        if (pos == null) return false;
        return pos.StartsWith("V") || pos == "VV" || pos == "VA" || pos == "VC" || pos == "VE";
    }

    /// <summary>
    /// For a multi-token entity, find the dependency edge where the child is inside
    /// the entity but the head is outside. Prioritizes non-nn relations (nn is internal
    /// modification). This gives us the relation of the entity as a whole to the rest
    /// of the sentence.
    /// </summary>
    static HanLPDepEdge? FindExternalDependencyEdge(
        List<HanLPToken> entityTokens,
        Dictionary<int, HanLPDepEdge> edgeByChild)
    {
        var entityTokenIds = new HashSet<int>(entityTokens.Select(t => t.Id));

        // First pass: find non-nn external edges (more meaningful relations)
        foreach (var token in entityTokens)
        {
            if (edgeByChild.TryGetValue(token.Id, out var edge))
            {
                if (edge.Head < 0 || !entityTokenIds.Contains(edge.Head))
                {
                    if (edge.Rel != "nn" && edge.Rel != "assm")
                    {
                        return edge;
                    }
                }
            }
        }

        // Second pass: accept any external edge (including nn/assm)
        foreach (var token in entityTokens)
        {
            if (edgeByChild.TryGetValue(token.Id, out var edge2))
            {
                if (edge2.Head < 0 || !entityTokenIds.Contains(edge2.Head))
                {
                    return edge2;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Determines the syntactic role of a location token based on its dependency relation.
    ///
    /// HanLP dependency relation mapping:
    /// - nsubj, top: The location is the subject (e.g., "长安陷落了", "长安是...")
    /// - assmod, nn, amod, attr: The location is an attributive modifier (e.g., "江南的丝绸", "京沪高铁")
    /// - dobj: The location is a direct object (e.g., "攻克城池") or predicative (e.g., "到了北京")
    /// - lobj, loc: The location is an adverbial of place (e.g., "在长安城中", "位于海淀")
    /// - pobj + head's rel is prep: The location is an adverbial (e.g., "向江南")
    /// - advmod: The location modifies a verb as an adverbial
    /// </summary>
    private static string DetermineSyntacticRole(string rel, string? headRel, string? headPos, string? headText)
    {
        return rel switch
        {
            // Subject roles
            "nsubj" => LocationSyntacticRole.Subject,
            "top" => LocationSyntacticRole.Subject,  // topic as subject
            
            // Attributive roles
            "assmod" => LocationSyntacticRole.Attributive,  // attributive modifier
            "nn" => LocationSyntacticRole.Attributive,      // noun modifying noun (e.g., "京沪" in "京沪高铁")
            "amod" => LocationSyntacticRole.Attributive,    // adjective-like modifier
            "attr" => LocationSyntacticRole.Attributive,    // attribute
            
            // Object / Predicative roles
            "dobj" => DetermineObjectOrPredicative(headText),
            
            // Adverbial roles
            "lobj" => LocationSyntacticRole.Adverbial,      // location object (e.g., "高铁上看风景" -> 高铁 is lobj)
            "loc" => LocationSyntacticRole.Adverbial,       // location modifier (e.g., "位于X")
            "pobj" when headRel == "prep" => LocationSyntacticRole.Adverbial,  // prepositional object
            "advmod" => LocationSyntacticRole.Adverbial,    // adverbial modifier
            
            _ => LocationSyntacticRole.Unknown
        };
    }

    /// <summary>
    /// Distinguishes between Object and Predicative roles for "dobj" relation.
    /// If the head verb indicates arrival/becoming/location, it's a Predicative (destination/state).
    /// Otherwise it's a regular Object (target of action).
    /// </summary>
    private static string DetermineObjectOrPredicative(string? headText)
    {
        if (string.IsNullOrEmpty(headText))
            return LocationSyntacticRole.Object;

        // Verbs that indicate arrival/destination/state -> Predicative
        var predicativeVerbs = new HashSet<string>
        {
            "到", "到达", "抵达", "至", "在", "位于", "处于", "落在",
            "去", "回", "来", "进", "出", "上", "下", "过",
            "成为", "变成", "化为", "转为",
            "住", "居", "定居", "落户", "扎根",
            "建", "建立", "建造", "设置", "设立",
            "停", "停留", "驻扎", "盘踞"
        };

        if (predicativeVerbs.Contains(headText))
            return LocationSyntacticRole.Predicative;

        // Partial match: head text contains arrival-related characters
        var arrivalChars = new[] { "到", "达", "抵", "至", "在", "位", "落", "驻" };
        if (arrivalChars.Any(c => headText.Contains(c)))
            return LocationSyntacticRole.Predicative;

        return LocationSyntacticRole.Object;
    }
}
