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
using System.Net.Http;
using System.Text;
using System.Text.Json;
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
    /// Analyzes a single text block using the HanLP API.
    /// </summary>
    public async Task<SemanticBlockResult> AnalyzeAsync(
        AnalysisTreeNode node,
        CancellationToken cancellationToken = default)
    {
        System.Diagnostics.Debug.WriteLine($"[SemanticAnalysisService] Analyzing node with {node.Content.Length} chars");
        
        var request = new HanLPAnalyzeRequest
        {
            Text = node.Content,
            Source = "hanlp_v2",
            Language = "auto"
        };

        var json = JsonSerializer.Serialize(request, SourceGenerationContext.Default.HanLPAnalyzeRequest);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        System.Diagnostics.Debug.WriteLine($"[SemanticAnalysisService] POSTing to {_baseUrl}/analyze");
        var response = await _httpClient.PostAsync("analyze", content, cancellationToken);
        
        System.Diagnostics.Debug.WriteLine($"[SemanticAnalysisService] Response status: {response.StatusCode}");
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            System.Diagnostics.Debug.WriteLine($"[SemanticAnalysisService] Error response: {responseJson.Substring(0, Math.Min(300, responseJson.Length))}");
        }
        response.EnsureSuccessStatusCode();
        System.Diagnostics.Debug.WriteLine($"[SemanticAnalysisService] Response length: {responseJson.Length} chars");
        
        var analyzeResponse = JsonSerializer.Deserialize(responseJson, SourceGenerationContext.Default.HanLPAnalyzeResponse);
        
        return MapToSemanticResult(node, analyzeResponse);
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
        return JsonSerializer.Deserialize(json, SourceGenerationContext.Default.SemanticResultFile);
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
}