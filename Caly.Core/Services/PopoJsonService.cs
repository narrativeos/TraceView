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

using Avalonia;
using Caly.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Caly.Core.Services;

/// <summary>
/// Service for loading and parsing MinerU-Popo JSON files.
/// Supports three JSON formats: normalization, inference, and tree output.
/// 
/// Directory structure (per POPV-VISUALIZATION-SPEC.md §6.1):
///   outputs/
///   ├── label_normalization/{model_name}/{doc_id}.json
///   ├── inference/{model_name}/{doc_id}.json
///   └── build_tree/{model_name}/{doc_id}.json
/// </summary>
public static class PopoJsonService
{
    /// <summary>
    /// Default model name for Popo processing.
    /// </summary>
    public const string DefaultModelName = "mineru";

    /// <summary>
    /// Finds Popo JSON files for a given PDF document path.
    /// Searches the outputs/ directory following MinerU-Popo standard structure.
    /// </summary>
    /// <param name="pdfPath">Full path to the PDF file.</param>
    /// <param name="modelName">Model name (default: "mineru").</param>
    /// <returns>Tuple of (normalized, inference, tree) paths, null for each if not found.</returns>
    public static (string? normalized, string? inference, string? tree) FindPopoJsonPaths(
        string pdfPath, string modelName = DefaultModelName)
    {
        if (string.IsNullOrEmpty(pdfPath))
            return (null, null, null);

        var docId = Path.GetFileNameWithoutExtension(pdfPath);
        var outputRoot = GetOutputRootDir(pdfPath);

        if (string.IsNullOrEmpty(outputRoot))
            return (null, null, null);

        string? normalized = FindJsonInStage(outputRoot, "label_normalization", modelName, docId);
        string? inference = FindJsonInStage(outputRoot, "inference", modelName, docId);
        string? tree = FindJsonInStage(outputRoot, "build_tree", modelName, docId);

        return (normalized, inference, tree);
    }

    /// <summary>
    /// Gets the Popo output root directory.
    /// Priority: 1) POPO_OUTPUT_DIR env var, 2) PDF sibling outputs/ directory
    /// </summary>
    private static string? GetOutputRootDir(string pdfPath)
    {
        // Priority 1: Environment variable
        var envDir = Environment.GetEnvironmentVariable("POPO_OUTPUT_DIR");
        if (!string.IsNullOrEmpty(envDir) && Directory.Exists(envDir))
            return envDir;

        // Priority 2: PDF sibling outputs/ directory
        var directory = Path.GetDirectoryName(pdfPath)!;
        var siblingOutputs = Path.Combine(directory, "outputs");
        if (Directory.Exists(siblingOutputs))
            return siblingOutputs;

        return null;
    }

    /// <summary>
    /// Finds a JSON file in a specific stage directory.
    /// Searches across all model subdirectories if specific model not found.
    /// </summary>
    private static string? FindJsonInStage(string outputRoot, string stage, string modelName, string docId)
    {
        // Try specific model first
        var specificPath = Path.Combine(outputRoot, stage, modelName, $"{docId}.json");
        if (File.Exists(specificPath))
            return specificPath;

        // Try all model subdirectories as fallback
        var stageDir = Path.Combine(outputRoot, stage);
        if (Directory.Exists(stageDir))
        {
            foreach (var modelDir in Directory.GetDirectories(stageDir))
            {
                var path = Path.Combine(modelDir, $"{docId}.json");
                if (File.Exists(path))
                    return path;
            }
        }

        return null;
    }

    /// <summary>
    /// Loads a complete PopoDocument from JSON files.
    /// </summary>
    public static PopoDocument? LoadPopoDocument(string pdfPath, string modelName = DefaultModelName)
    {
        var (normalizedJson, inferenceJson, treeJson) = FindPopoJsonPaths(pdfPath, modelName);

        var doc = new PopoDocument();
        var loaded = false;

        // Load normalized JSON (pages-based structure)
        if (!string.IsNullOrEmpty(normalizedJson))
        {
            doc = LoadNormalizationJson(normalizedJson);
            loaded = true;
        }

        // Load inference JSON (flat block list)
        if (!string.IsNullOrEmpty(inferenceJson))
        {
            var inferenceBlocks = LoadInferenceJson(inferenceJson);
            if (inferenceBlocks is not null)
            {
                doc.InferenceBlocks = inferenceBlocks;
                // Also populate PagesBlocks from inference if normalized not available
                if (!loaded)
                {
                    doc.PopulatePagesBlocksFromInference();
                    loaded = true;
                }
            }
        }

        // Load tree JSON
        if (!string.IsNullOrEmpty(treeJson))
        {
            doc.TreeRoot = LoadTreeJson(treeJson);
            if (doc.TreeRoot is not null)
            {
                doc.BuildAggregationMap();
                loaded = true;
            }
        }

        return loaded ? doc : null;
    }

    /// <summary>
    /// Loads normalization JSON (label_normalization.py output).
    /// Format: { "model": "...", "doc_id": "...", "pages": { "1": [ {...}, ... ], ... } }
    /// </summary>
    internal static PopoDocument LoadNormalizationJson(string jsonPath)
    {
        var doc = new PopoDocument();
        var json = File.ReadAllText(jsonPath);
        var root = JsonDocument.Parse(json);
        var elem = root.RootElement;

        if (elem.TryGetProperty("model", out var modelElem))
            doc.ModelName = modelElem.GetString() ?? string.Empty;

        if (elem.TryGetProperty("doc_id", out var docIdElem))
            doc.DocId = docIdElem.GetString() ?? string.Empty;

        if (elem.TryGetProperty("pages", out var pagesElem))
        {
            foreach (var pageEntry in pagesElem.EnumerateObject())
            {
                if (!int.TryParse(pageEntry.Name, out var pageNum))
                    continue;

                var blocks = new List<PopoBlock>();
                var blockList = pageEntry.Value;

                int order = 0;
                foreach (var blockElem in blockList.EnumerateArray())
                {
                    var block = new PopoBlock
                    {
                        Id = order,
                        Page = pageNum
                    };

                    if (blockElem.TryGetProperty("type", out var typeElem))
                        block.Type = typeElem.GetString() ?? string.Empty;

                    if (blockElem.TryGetProperty("content", out var contentElem))
                        block.Content = contentElem.GetString() ?? string.Empty;

                    if (blockElem.TryGetProperty("bbox", out var bboxElem))
                    {
                        var bbox = ParseBbox(bboxElem);
                        block.Bbox = bbox;
                    }

                    if (blockElem.TryGetProperty("title_level", out var levelElem))
                        block.TitleLevel = levelElem.GetInt32();

                    if (blockElem.TryGetProperty("source_label", out var sourceLabelElem))
                        block.SourceLabel = sourceLabelElem.GetString() ?? string.Empty;

                    if (blockElem.TryGetProperty("source_id", out var sourceIdElem))
                    {
                        var sourceId = sourceIdElem.GetString();
                        // Extract order from "doc_id:order" format
                        if (sourceId != null && sourceId.Contains(':'))
                        {
                            var parts = sourceId.Split(':');
                            if (parts.Length > 1 && int.TryParse(parts[1], out var extractedOrder))
                                order = extractedOrder;
                        }
                    }

                    blocks.Add(block);
                    order++;
                }

                doc.PagesBlocks[pageNum] = blocks;
            }
        }

        return doc;
    }

    /// <summary>
    /// Loads inference JSON (inference.py output).
    /// Format: [ { "id": 1, "page": 1, "type": "title", ... }, ... ]
    /// </summary>
    internal static List<PopoBlock>? LoadInferenceJson(string jsonPath)
    {
        var json = File.ReadAllText(jsonPath);
        var doc = JsonDocument.Parse(json);

        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return null;

        var blocks = new List<PopoBlock>();

        foreach (var elem in doc.RootElement.EnumerateArray())
        {
            var block = new PopoBlock();

            if (elem.TryGetProperty("id", out var idElem))
                block.Id = idElem.GetInt32();

            if (elem.TryGetProperty("page", out var pageElem))
                block.Page = pageElem.GetInt32();

            if (elem.TryGetProperty("type", out var typeElem))
                block.Type = typeElem.GetString() ?? string.Empty;

            if (elem.TryGetProperty("content", out var contentElem))
                block.Content = contentElem.GetString() ?? string.Empty;

            if (elem.TryGetProperty("bbox", out var bboxElem))
            {
                var bbox = ParseBbox(bboxElem);
                block.Bbox = bbox;
            }

            if (elem.TryGetProperty("contd", out var contdElem))
                block.Contd = contdElem.GetInt32();

            if (elem.TryGetProperty("level", out var levelElem))
                block.Level = levelElem.GetInt32();

            if (elem.TryGetProperty("image", out var imageElem))
                block.Image = imageElem.GetInt32();

            if (elem.TryGetProperty("table_merge", out var mergeElem))
                block.TableMerge = mergeElem.GetInt32();

            blocks.Add(block);
        }

        return blocks;
    }

    /// <summary>
    /// Loads tree JSON (get_json_tree.py output).
    /// Format: { "type": "root", "children": [ ... ], ... }
    /// </summary>
    internal static PopoTreeNode? LoadTreeJson(string jsonPath)
    {
        var json = File.ReadAllText(jsonPath);
        var doc = JsonDocument.Parse(json);

        return ParseTreeNode(doc.RootElement);
    }

    private static PopoTreeNode ParseTreeNode(JsonElement elem)
    {
        var node = new PopoTreeNode();

        if (elem.TryGetProperty("type", out var typeElem))
            node.Type = typeElem.GetString() ?? string.Empty;

        if (elem.TryGetProperty("title", out var titleElem))
            node.Title = titleElem.GetString() ?? string.Empty;

        if (elem.TryGetProperty("metadata", out var metaElem))
            node.Metadata = metaElem.GetString() ?? string.Empty;

        if (elem.TryGetProperty("content", out var contentElem))
            node.Content = contentElem.GetString() ?? string.Empty;

        if (elem.TryGetProperty("level", out var levelElem))
            node.Level = levelElem.GetInt32();

        // Parse location
        if (elem.TryGetProperty("location", out var locElem))
        {
            foreach (var loc in locElem.EnumerateArray())
            {
                var entry = new LocationEntry();

                if (loc.TryGetProperty("bbox", out var bboxElem))
                    entry.Bbox = ParseBbox(bboxElem);

                if (loc.TryGetProperty("page", out var pageElem))
                    entry.Page = pageElem.GetInt32();

                node.Location.Add(entry);
            }
        }

        // Parse block_ids
        if (elem.TryGetProperty("block_ids", out var idsElem))
        {
            foreach (var id in idsElem.EnumerateArray())
            {
                node.BlockIds.Add(id.GetInt32());
            }
        }

        // Parse children recursively
        if (elem.TryGetProperty("children", out var childrenElem))
        {
            foreach (var child in childrenElem.EnumerateArray())
            {
                node.Children.Add(ParseTreeNode(child));
            }
        }

        return node;
    }

    private static Rect ParseBbox(JsonElement elem)
    {
        if (elem.ValueKind == JsonValueKind.Array && elem.GetArrayLength() >= 4)
        {
            var x = elem[0].GetDouble();
            var y = elem[1].GetDouble();
            var x2 = elem[2].GetDouble();
            var y2 = elem[3].GetDouble();
            return new Rect((double)x, (double)y, (double)(x2 - x), (double)(y2 - y));
        }

        return new Rect(0, 0, 0, 0);
    }

    // Extension method for PopoDocument to populate PagesBlocks from InferenceBlocks
    private static void PopulatePagesBlocksFromInference(this PopoDocument doc)
    {
        var pages = new Dictionary<int, List<PopoBlock>>();

        foreach (var block in doc.InferenceBlocks)
        {
            if (!pages.TryGetValue(block.Page, out var pageBlocks))
            {
                pageBlocks = new List<PopoBlock>();
                pages[block.Page] = pageBlocks;
            }
            pageBlocks.Add(block);
        }

        doc.PagesBlocks = pages;
    }
}