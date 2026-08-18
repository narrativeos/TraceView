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
using System.Text.Json.Serialization;
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
    
    private static void Log(string message)
    {
        System.Diagnostics.Debug.WriteLine(message);
    }

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
    /// Request for the /analyze/title endpoint.
    /// </summary>
    public class TitleRequest
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;
        
        [JsonPropertyName("target_chars")]
        public int TargetChars { get; set; } = 14;
    }
    
    /// <summary>
    /// Response from the /analyze/title endpoint.
    /// </summary>
    public class TitleResponse
    {
        [JsonPropertyName("title_text")]
        public string? TitleText { get; set; }
        
        [JsonPropertyName("method")]
        public string? Method { get; set; }
        
        [JsonPropertyName("score")]
        public double Score { get; set; }
    }
    
    /// <summary>
    /// Calls /analyze/title to generate a concise title from the given text.
    /// Uses TextRank-based extraction with configurable target length.
    /// Returns null if the API call fails or the text is empty.
    /// </summary>
    /// <param name="text">Source text to generate title from.</param>
    /// <param name="targetChars">Target character length for the title (default: 14).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<string?> GenerateTitleAsync(
        string text,
        int targetChars = 14,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            Log("[SemanticAnalysisService] GenerateTitleAsync: text is empty/null");
            return null;
        }
        
        Log($"[SemanticAnalysisService] GenerateTitleAsync called, baseUrl={_baseUrl}, text_len={text.Length}, targetChars={targetChars}");
        
        try
        {
            var request = new TitleRequest
            {
                Text = text,
                TargetChars = targetChars
            };
            
            var json = JsonSerializer.Serialize(request, SourceGenerationContext.Default.TitleRequest);
            Log($"[SemanticAnalysisService] Serialized request: {json.Substring(0, Math.Min(200, json.Length))}");
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            Log($"[SemanticAnalysisService] Sending POST to {_baseUrl}/analyze/title");
            var response = await _httpClient.PostAsync("analyze/title", content, cancellationToken);
            Log($"[SemanticAnalysisService] Response status: {response.StatusCode}");
            
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                Log($"[SemanticAnalysisService] /analyze/title failed: {response.StatusCode} - {errorBody.Substring(0, Math.Min(200, errorBody.Length))}");
                return null;
            }
            
            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            Log($"[SemanticAnalysisService] Response JSON: {responseJson.Substring(0, Math.Min(300, responseJson.Length))}");
            
            // Use source generation context for deserialization
            var titleResponse = (TitleResponse?)JsonSerializer.Deserialize(responseJson, SourceGenerationContext.Default.TitleResponse);
            
            if (titleResponse is not null && !string.IsNullOrEmpty(titleResponse.TitleText))
            {
                Log($"[SemanticAnalysisService] /analyze/title SUCCESS: \"{titleResponse.TitleText}\" (score={titleResponse.Score})");
                return titleResponse.TitleText;
            }
            
            Log($"[SemanticAnalysisService] /analyze/title returned null or empty title_text, titleResponse={titleResponse?.ToString()}, TitleText={titleResponse?.TitleText}");
            return null;
        }
        catch (Exception ex)
        {
            Log($"[SemanticAnalysisService] /analyze/title EXCEPTION: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
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
    /// <param name="node">The Popo tree node to analyze.</param>
    /// <param name="entityCategories">Optional domain-specific keyword dictionaries for entity discovery.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<SemanticBlockResult> AnalyzeAsync(
        AnalysisTreeNode node,
        Dictionary<string, List<string>>? entityCategories = null,
        CancellationToken cancellationToken = default)
    {
        System.Diagnostics.Debug.WriteLine($"[SemanticAnalysisService] Analyzing node with {node.Content.Length} chars");
        
        var cleanText = CleanOcrMarkers(node.Content);
        var request = new HanLPAnalyzeRequest
        {
            Text = cleanText,
            Source = "hanlp_v2",
            Language = "auto",
            // ── Phase 1: Enable HanLP advanced discovery features ──
            Discover = true,                  // Enable new word discovery
            Enhance = true,                   // Auto-apply discovered words & re-analyze
            AutoDiscoverEntities = true,      // Promote high-score candidates to entities
            EntityCategories = entityCategories // Domain-specific keyword injection
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

            // Extract intra-block location relationships (nn, assmod/assm, conj)
            var relations = ExtractLocationRelations(
                content_obj.Tokens, content_obj.Deps, result.Entities, node.Content);
            result.LocationRelations = relations;
        }
        else if (content_obj?.Deps is null)
        {
            System.Diagnostics.Debug.WriteLine("[SemanticAnalysisService] No deps in /analyze response");
        }

        // Save coreference chains for cross-block resolution (Phase 2 Step 6)
        result.CorefChains = content_obj?.Coreferences;

        return result;
    }

    /// <summary>
    /// Analyzes multiple nodes in sequence.
    /// </summary>
    /// <param name="nodes">Popo tree nodes to analyze.</param>
    /// <param name="outputDir">Output directory for semantic_result.json.</param>
    /// <param name="progressCallback">Progress callback (current, total).</param>
    /// <param name="entityCategories">Optional domain-specific keyword dictionaries for entity discovery.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<SemanticResultFile> ProcessAllNodesAsync(
        List<AnalysisTreeNode> nodes,
        string outputDir,
        Action<int, int> progressCallback,
        Dictionary<string, List<string>>? entityCategories = null,
        CancellationToken cancellationToken = default)
    {
        var blocks = new List<SemanticBlockResult>();

        for (int i = 0; i < nodes.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var blockResult = await AnalyzeAsync(nodes[i], entityCategories, cancellationToken);
                blocks.Add(blockResult);
            }
            catch (Exception ex)
            {
                // Store error result for this node
                blocks.Add(new SemanticBlockResult
                {
                    BlockIds = nodes[i].BlockIds,
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

        // Phase 2 Step 6: Resolve cross-block coreference relations for LOCATION entities
        fileResult.CrossBlockRelations = ResolveCrossBlockCoreferences(blocks);

        // ── Phase 1: Extract term definitions from all blocks ──
        var definitionExtractor = new TermDefinitionExtractor();
        foreach (var block in blocks)
        {
            if (block.Error != null) continue;
            block.TermDefinitions = definitionExtractor.ExtractDefinitions(block);
        }
        // Cross-block definition resolution (merge same-term definitions)
        definitionExtractor.ResolveCrossBlockDefinitions(blocks);

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
        
        // Fallback: use SourceGenerationContext with TypeInfoResolver (reflection is disabled in .NET 10)
        try
        {
            var options = new System.Text.Json.JsonSerializerOptions 
            { 
                TypeInfoResolver = SourceGenerationContext.Default,
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
            BlockIds = node.BlockIds,
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
                // Keep the HanLPEntityAttribute objects as-is for proper JSON serialization
                var attributes = entity.Attributes ?? new List<HanLPEntityAttribute>();

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

        // Cross-reference events.arguments with entities to populate spatial_role + verb_spatial_class
        if (content.Events is not null && result.Entities is not null)
        {
            var entityById = result.Entities.ToDictionary(e => e.Id, e => e);
            foreach (var evt in content.Events)
            {
                if (evt.Arguments is null) continue;
                foreach (var arg in evt.Arguments)
                {
                    if (string.IsNullOrEmpty(arg.EntityId)) continue;
                    if (!entityById.TryGetValue(arg.EntityId, out var entity)) continue;
                    if (!string.IsNullOrEmpty(arg.SpatialRole))
                        entity.SpatialRole = arg.SpatialRole;
                    if (!string.IsNullOrEmpty(arg.VerbSpatialClass))
                        entity.VerbSpatialClass = arg.VerbSpatialClass;
                }
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

    /// <summary>
    /// Extracts relationships between location entities based on dependency parsing edges.
    /// Handles three relationship types:
    /// - ParentChild: nn (noun-noun modifier) edges between two separate LOCATION entities,
    ///   or internal nn edges within a multi-token LOCATION entity.
    /// - Ownership: assmod/assm (associative modifier) edges between LOCATION entities.
    /// - Parallel: conj (conjunction) edges between LOCATION entities.
    /// </summary>
    /// <param name="tokens">HanLP tokens for this block.</param>
    /// <param name="deps">Dependency edges.</param>
    /// <param name="allEntities">All entities in this block (not just LOCATION).</param>
    /// <param name="fullText">The full original text for extracting evidence spans.</param>
    private static List<LocationRelation> ExtractLocationRelations(
        List<HanLPToken> tokens,
        List<HanLPDepEdge> deps,
        List<SemanticEntity> allEntities,
        string fullText)
    {
        var relations = new List<LocationRelation>();
        var locationEntities = allEntities.Where(e => e.Category == "LOCATION").ToList();

        if (locationEntities.Count < 2 && !HasMultiTokenLocationEntity(locationEntities, tokens))
            return relations;

        // Build lookup: tokenId → entities that contain this token
        var tokenIdToEntities = new Dictionary<int, List<SemanticEntity>>();
        foreach (var entity in allEntities)
        {
            int entityStart = entity.Span.Length >= 1 ? entity.Span[0] : 0;
            int entityEnd = entity.Span.Length >= 2 ? entity.Span[1] : entityStart;

            foreach (var token in tokens)
            {
                if (token.Span.Count < 2) continue;
                int tokenStart = token.Span[0];
                int tokenEnd = token.Span[1];
                if (tokenStart < entityEnd && tokenEnd > entityStart)
                {
                    if (!tokenIdToEntities.ContainsKey(token.Id))
                        tokenIdToEntities[token.Id] = new List<SemanticEntity>();
                    if (!tokenIdToEntities[token.Id].Contains(entity))
                        tokenIdToEntities[token.Id].Add(entity);
                }
            }
        }

        // Build lookup: tokenId → HanLPToken
        var tokenById = tokens.ToDictionary(t => t.Id, t => t);

        // Track processed entity pairs to avoid duplicates
        var processedPairs = new HashSet<string>();

        // Process each LOCATION entity's tokens for external relations
        foreach (var entity in locationEntities)
        {
            int entityStart = entity.Span.Length >= 1 ? entity.Span[0] : 0;
            int entityEnd = entity.Span.Length >= 2 ? entity.Span[1] : entityStart;

            // Find tokens belonging to this entity
            var entityTokens = tokens.Where(t =>
            {
                if (t.Span.Count < 2) return false;
                return t.Span[0] < entityEnd && t.Span[1] > entityStart;
            }).ToList();

            var entityTokenIds = new HashSet<int>(entityTokens.Select(t => t.Id));

            foreach (var token in entityTokens)
            {
                // Find the dep edge where this token is the child
                var edge = deps.FirstOrDefault(d => d.Child == token.Id);
                if (edge == null) continue;

                int headId = edge.Head;
                if (headId < 0) continue;

                // Skip self-loops
                if (headId == token.Id) continue;

                // Check if head token belongs to another entity
                if (!tokenIdToEntities.TryGetValue(headId, out var headEntities))
                    continue;

                foreach (var headEntity in headEntities)
                {
                    // Skip if head is in the same entity
                    if (headEntity.Id == entity.Id) continue;

                    var pairKey = GetPairKey(entity.Text, headEntity.Text);
                    if (processedPairs.Contains(pairKey)) continue;

                    LocationRelation? relation = null;

                    switch (edge.Rel)
                    {
                        case "nn":
                            // Noun-noun modifier: headEntity is parent of (or context for) entity
                            // Only create if headEntity is also LOCATION
                            if (headEntity.Category == "LOCATION")
                            {
                                relation = new LocationRelation
                                {
                                    RelationType = LocationRelationType.ParentChild,
                                    SourceLocation = headEntity.Text,  // parent
                                    TargetLocation = entity.Text,      // child
                                    DepRel = edge.Rel,
                                    Confidence = entity.Confidence,
                                    Evidence = ExtractEvidence(fullText, entityStart, entityEnd)
                                };
                            }
                            break;

                        case "assmod":
                        case "assm":
                            // Associative modifier: headEntity is owner of entity
                            if (headEntity.Category == "LOCATION")
                            {
                                relation = new LocationRelation
                                {
                                    RelationType = LocationRelationType.Ownership,
                                    SourceLocation = headEntity.Text,  // owner
                                    TargetLocation = entity.Text,      // owned
                                    DepRel = edge.Rel,
                                    Confidence = entity.Confidence,
                                    Evidence = ExtractEvidence(fullText, entityStart, entityEnd)
                                };
                            }
                            break;

                        case "conj":
                            // Conjunction: two parallel locations
                            if (headEntity.Category == "LOCATION")
                            {
                                relation = new LocationRelation
                                {
                                    RelationType = LocationRelationType.Parallel,
                                    SourceLocation = headEntity.Text,
                                    TargetLocation = entity.Text,
                                    DepRel = edge.Rel,
                                    Confidence = entity.Confidence,
                                    Evidence = ExtractEvidence(fullText,
                                        Math.Min(entityStart, headEntity.Span.Length >= 1 ? headEntity.Span[0] : entityStart),
                                        Math.Max(entityEnd, headEntity.Span.Length >= 2 ? headEntity.Span[1] : entityEnd))
                                };
                            }
                            break;
                    }

                    if (relation != null)
                    {
                        processedPairs.Add(pairKey);
                        relations.Add(relation);
                        System.Diagnostics.Debug.WriteLine(
                            $"[SemanticAnalysisService] Relation: {relation.SourceLocation} --({relation.DepRel})--> {relation.TargetLocation} [{relation.RelationType}]");
                    }
                }
            }

            // Handle multi-token entity internal nn edges
            // e.g., entity "北京北三环" spans tokens "北京"(id=1) and "北三环"(id=2) with 2-(nn)->1
            if (entityTokens.Count >= 2)
            {
                foreach (var token in entityTokens)
                {
                    var edge = deps.FirstOrDefault(d => d.Child == token.Id);
                    if (edge == null || edge.Rel != "nn") continue;

                    int headId = edge.Head;
                    if (!entityTokenIds.Contains(headId)) continue;
                    if (!tokenById.TryGetValue(headId, out var headToken)) continue;

                    // Internal nn: headToken is the parent context for this token within the entity
                    var internalPairKey = $"{headToken.Text}|{token.Text}|internal";
                    if (processedPairs.Contains(internalPairKey)) continue;

                    // Only create if both parts look like independent location names
                    if (IsLikelyIndependentLocation(headToken.Text) && IsLikelyIndependentLocation(token.Text))
                    {
                        relations.Add(new LocationRelation
                        {
                            RelationType = LocationRelationType.ParentChild,
                            SourceLocation = headToken.Text,
                            TargetLocation = token.Text,
                            DepRel = $"nn(internal:{entity.Text})",
                            Confidence = entity.Confidence,
                            Evidence = entity.Text
                        });
                        processedPairs.Add(internalPairKey);
                        System.Diagnostics.Debug.WriteLine(
                            $"[SemanticAnalysisService] Internal relation: {headToken.Text} --(nn)--> {token.Text} [ParentChild, entity={entity.Text}]");
                    }
                }
            }
        }

        return relations;
    }

    /// <summary>
    /// Checks if any LOCATION entity spans multiple tokens (potential internal nn).
    /// </summary>
    private static bool HasMultiTokenLocationEntity(List<SemanticEntity> locationEntities, List<HanLPToken> tokens)
    {
        foreach (var entity in locationEntities)
        {
            int entityStart = entity.Span.Length >= 1 ? entity.Span[0] : 0;
            int entityEnd = entity.Span.Length >= 2 ? entity.Span[1] : entityStart;

            int tokenCount = tokens.Count(t =>
            {
                if (t.Span.Count < 2) return false;
                return t.Span[0] < entityEnd && t.Span[1] > entityStart;
            });

            if (tokenCount >= 2) return true;
        }
        return false;
    }

    /// <summary>
    /// Heuristic: checks if a text segment looks like it could be an independent location name.
    /// Filters out particles, numbers, single-character functional words.
    /// </summary>
    private static bool IsLikelyIndependentLocation(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (text.Length <= 1) return false;
        // Contains at least one Chinese character
        if (!System.Text.RegularExpressions.Regex.IsMatch(text, @"[\u4e00-\u9fff]")) return false;
        // Not just punctuation or spaces
        if (text.All(c => char.IsPunctuation(c) || char.IsWhiteSpace(c))) return false;
        return true;
    }

    /// <summary>
    /// Extracts the evidence text span from the full text.
    /// </summary>
    private static string ExtractEvidence(string fullText, int start, int end)
    {
        if (string.IsNullOrEmpty(fullText)) return string.Empty;
        if (start < 0 || end > fullText.Length || start >= end) return string.Empty;
        return fullText.Substring(start, end - start);
    }

    /// <summary>
    /// Creates a deterministic pair key for deduplication.
    /// </summary>
    private static string GetPairKey(string a, string b)
    {
        return string.CompareOrdinal(a, b) <= 0 ? $"{a}|{b}" : $"{b}|{a}";
    }

    /// <summary>
    /// Resolves cross-block coreference chains for LOCATION entities.
    /// Matches HanLP coreference mentions to location entities across all blocks,
    /// and creates Coreference relations where different names refer to the same location.
    /// e.g., "帝都" and "北京" → Coreference(Source="帝都", Target="北京")
    /// </summary>
    private static List<LocationRelation> ResolveCrossBlockCoreferences(List<SemanticBlockResult> blocks)
    {
        var relations = new List<LocationRelation>();

        // Collect all coreference chains across all blocks
        var allChains = new List<HanLPCorefChain>();
        foreach (var block in blocks)
        {
            if (block.CorefChains != null)
                allChains.AddRange(block.CorefChains);
        }

        if (allChains.Count == 0)
        {
            System.Diagnostics.Debug.WriteLine("[SemanticAnalysisService] No coreference chains found across blocks");
            return relations;
        }

        System.Diagnostics.Debug.WriteLine($"[SemanticAnalysisService] Processing {allChains.Count} coreference chains across {blocks.Count} blocks");

        // Build a lookup: mention text → location entity (across all blocks)
        var locationTexts = new HashSet<string>();
        foreach (var block in blocks)
        {
            foreach (var entity in block.LocationEntities)
            {
                locationTexts.Add(entity.Text);
                // Also index by normalized form if available
                if (!string.IsNullOrEmpty(entity.Normalized) && entity.Normalized != entity.Text)
                    locationTexts.Add(entity.Normalized);
            }
        }

        // Process each coreference chain
        foreach (var chain in allChains)
        {
            if (chain.Mentions == null || chain.Mentions.Count < 2)
                continue;

            // Find mentions that correspond to LOCATION entities
            var locationMentions = chain.Mentions
                .Where(m => locationTexts.Contains(m.Text))
                .Select(m => m.Text)
                .Distinct()
                .ToList();

            if (locationMentions.Count < 2)
                continue;

            // Use the representative (principal mention) as the canonical name, if available
            string canonicalName;
            var principalMention = chain.Mentions.FirstOrDefault(m => m.IsPrincipal);
            if (principalMention != null && locationMentions.Contains(principalMention.Text))
            {
                canonicalName = principalMention.Text;
            }
            else
            {
                // Fallback: use the longest name as canonical
                canonicalName = locationMentions.OrderByDescending(m => m.Length).First();
            }

            // Create Coreference relations: each alias → canonical name
            foreach (var mention in locationMentions)
            {
                if (mention == canonicalName) continue;

                relations.Add(new LocationRelation
                {
                    RelationType = LocationRelationType.Coreference,
                    SourceLocation = mention,       // alias
                    TargetLocation = canonicalName,  // canonical
                    DepRel = string.Empty,
                    Confidence = chain.Confidence,
                    Evidence = $"{mention} → {canonicalName} (chain: {chain.ChainId})"
                });

                System.Diagnostics.Debug.WriteLine(
                    $"[SemanticAnalysisService] Coreference: '{mention}' → '{canonicalName}' (chain: {chain.ChainId}, confidence: {chain.Confidence:F2})");
            }
        }

        System.Diagnostics.Debug.WriteLine(
            $"[SemanticAnalysisService] Resolved {relations.Count} cross-block coreference relations");

        return relations;
    }
}
