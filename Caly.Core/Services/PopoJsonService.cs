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
using System.IO.Compression;
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
    /// Loads a PopoDocument from a project's popo/ directory.
    /// Looks for popo.json in the project's popo/ subdirectory only.
    /// </summary>
    public static PopoDocument? LoadPopoDocumentFromProject(string? projectPath)
    {
        if (string.IsNullOrEmpty(projectPath))
            return null;

        var popoDir = Path.Combine(projectPath, "popo");
        if (!Directory.Exists(popoDir))
            return null;

        // Try to find popo.json in the popo directory
        var popoJsonPath = Path.Combine(popoDir, "popo.json");
        if (File.Exists(popoJsonPath))
        {
            try
            {
                var json = File.ReadAllText(popoJsonPath);
                return JsonSerializer.Deserialize<PopoDocument>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Loads a MinerU parse result produced in the project's mineru/ directory.
    /// This is a separate stage from Popo post-processing and may return a
    /// PopoDocument built from MinerU middle.json output.
    /// </summary>
    public static PopoDocument? LoadMinerUResultFromProject(string? projectPath)
    {
        if (string.IsNullOrEmpty(projectPath))
            return null;

        var minerUDir = Path.Combine(projectPath, "mineru");
        if (!Directory.Exists(minerUDir))
            return null;

        var minerUMiddleJsonFiles = Directory.GetFiles(minerUDir, "*_middle.json", SearchOption.AllDirectories);
        if (minerUMiddleJsonFiles.Length > 0)
        {
            return TryParseMinerUMiddleJson(minerUMiddleJsonFiles[0]);
        }

        return null;
    }

    /// <summary>
    /// Saves a PopoDocument to a project's popo/ directory.
    /// </summary>
    public static void SavePopoDocumentToProject(PopoDocument doc, string projectPath)
    {
        if (string.IsNullOrEmpty(projectPath))
            return;

        var popoDir = Path.Combine(projectPath, "popo");
        Directory.CreateDirectory(popoDir);

        var popoJsonPath = Path.Combine(popoDir, "popo.json");
        var json = JsonSerializer.Serialize(doc, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        });
        File.WriteAllText(popoJsonPath, json);
    }

    /// <summary>
    /// Saves the MinerU result ZIP file to the project's mineru/ directory.
    /// This allows the ZIP to be directly uploaded when calling Popo.
    /// </summary>
    /// <param name="zipPath">Path to the source ZIP file.</param>
    /// <param name="projectPath">The project directory path.</param>
    /// <param name="docId">Optional document ID for the filename. If null, uses the ZIP's filename.</param>
    /// <returns>The path to the saved ZIP file in the project directory.</returns>
    public static string SaveMinerUZipToProject(string zipPath, string projectPath, string? docId = null)
    {
        if (string.IsNullOrEmpty(projectPath) || !File.Exists(zipPath))
            return zipPath;

        var minerUDir = Path.Combine(projectPath, "mineru");
        Directory.CreateDirectory(minerUDir);

        var fileName = docId is not null
            ? $"{docId}_mineru.zip"
            : Path.GetFileName(zipPath);

        var destPath = Path.Combine(minerUDir, fileName);

        // Only copy if the destination doesn't exist or is different
        if (!File.Exists(destPath) || zipPath != destPath)
        {
            try
            {
                File.Copy(zipPath, destPath, overwrite: true);
            }
            catch
            {
                // If copy fails, return the original path
                return zipPath;
            }
        }

        return destPath;
    }

    /// <summary>
    /// Finds the MinerU result ZIP file in the project's mineru/ directory.
    /// </summary>
    /// <param name="projectPath">The project directory path.</param>
    /// <param name="docId">Optional document ID to find a specific ZIP.</param>
    /// <returns>The path to the ZIP file, or null if not found.</returns>
    public static string? FindMinerUZipInProject(string projectPath, string? docId = null)
    {
        if (string.IsNullOrEmpty(projectPath))
            return null;

        var minerUDir = Path.Combine(projectPath, "mineru");
        if (!Directory.Exists(minerUDir))
            return null;

        if (docId is not null)
        {
            var specificPath = Path.Combine(minerUDir, $"{docId}_mineru.zip");
            if (File.Exists(specificPath))
                return specificPath;
        }

        // Find any .zip file in the mineru directory
        var zipFiles = Directory.GetFiles(minerUDir, "*.zip");
        return zipFiles.Length > 0 ? zipFiles[0] : null;
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

    #region MinerU Output Parsing

    /// <summary>
    /// Parses MinerU middle.json output into a PopoDocument.
    /// Supports both the "pages" flat block list and optional "tree" hierarchical structure.
    /// </summary>
    public static PopoDocument? TryParseMinerUMiddleJson(string jsonPath)
    {
        if (!File.Exists(jsonPath))
            return null;

        try
        {
            var json = File.ReadAllText(jsonPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var popoDoc = new PopoDocument
            {
                DocId = GetStringProperty(root, "doc_id") ?? string.Empty,
                ModelName = GetStringProperty(root, "model_name") ?? "mineru"
            };

            // Parse page_size for coordinate normalization
            var pageSizeMap = new Dictionary<int, (double width, double height)>();
            if (root.TryGetProperty("page_size", out var pageSizeElem) && pageSizeElem.ValueKind == JsonValueKind.Object)
            {
                foreach (var entry in pageSizeElem.EnumerateObject())
                {
                    if (int.TryParse(entry.Name, out var pageNum) && entry.Value.ValueKind == JsonValueKind.Array && entry.Value.GetArrayLength() >= 2)
                    {
                        pageSizeMap[pageNum] = (
                            GetDoubleValue(entry.Value[0]),
                            GetDoubleValue(entry.Value[1]));
                    }
                }
            }

            // 1. Parse pages -> PagesBlocks
            if (root.TryGetProperty("pages", out var pagesElem))
            {
                if (pagesElem.ValueKind == JsonValueKind.Object)
                {
                    foreach (var pageEntry in pagesElem.EnumerateObject())
                    {
                        if (!int.TryParse(pageEntry.Name, out var pageNum))
                            continue;

                        var blocks = new List<PopoBlock>();
                        var pageWidth = pageSizeMap.TryGetValue(pageNum, out var size) ? size.width : 0.0;
                        var pageHeight = pageSizeMap.TryGetValue(pageNum, out size) ? size.height : 0.0;

                        foreach (var blockElem in pageEntry.Value.EnumerateArray())
                        {
                            var block = MapMinerUBlockToPopoBlock(blockElem, pageWidth, pageHeight);
                            blocks.Add(block);
                        }

                        popoDoc.PagesBlocks[pageNum] = blocks;
                    }
                }
                else if (pagesElem.ValueKind == JsonValueKind.Array)
                {
                    foreach (var pageEntry in pagesElem.EnumerateArray())
                    {
                        if (!pageEntry.TryGetProperty("page", out var pageElem))
                            continue;

                        var pageNum = GetIntValue(pageElem);
                        if (pageNum <= 0)
                            continue;

                        var blocks = new List<PopoBlock>();
                        var pageWidth = pageSizeMap.TryGetValue(pageNum, out var size) ? size.width : 0.0;
                        var pageHeight = pageSizeMap.TryGetValue(pageNum, out size) ? size.height : 0.0;

                        if (pageEntry.TryGetProperty("blocks", out var blocksElem) && blocksElem.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var blockElem in blocksElem.EnumerateArray())
                            {
                                var block = MapMinerUBlockToPopoBlock(blockElem, pageWidth, pageHeight);
                                blocks.Add(block);
                            }
                        }

                        if (blocks.Count > 0)
                            popoDoc.PagesBlocks[pageNum] = blocks;
                    }
                }
            }

            // 2. Parse pdf_info -> PagesBlocks (actual MinerU output format seen in the wild)
            if (root.TryGetProperty("pdf_info", out var pdfInfoElem) && pdfInfoElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var pageInfoElem in pdfInfoElem.EnumerateArray())
                {
                    var pageNum = pageInfoElem.TryGetProperty("page_idx", out var pageIdxElem)
                        ? GetIntValue(pageIdxElem)
                        : 0;

                    if (pageNum <= 0)
                        continue;

                    var pageWidth = pageInfoElem.TryGetProperty("page_size", out var pageSizeElemForPage) && pageSizeElemForPage.ValueKind == JsonValueKind.Array && pageSizeElemForPage.GetArrayLength() >= 2
                        ? GetDoubleValue(pageSizeElemForPage[0])
                        : 0.0;
                    var pageHeight = pageInfoElem.TryGetProperty("page_size", out var pageSizeElemForPage2) && pageSizeElemForPage2.ValueKind == JsonValueKind.Array && pageSizeElemForPage2.GetArrayLength() >= 2
                        ? GetDoubleValue(pageSizeElemForPage2[1])
                        : 0.0;

                    var blocks = new List<PopoBlock>();
                    foreach (var sectionName in new[] { "preproc_blocks", "para_blocks", "discarded_blocks" })
                    {
                        if (!pageInfoElem.TryGetProperty(sectionName, out var sectionElem) || sectionElem.ValueKind != JsonValueKind.Array)
                            continue;

                        foreach (var blockElem in sectionElem.EnumerateArray())
                        {
                            blocks.AddRange(MapMinerUPageSectionToBlocks(blockElem, pageNum, pageWidth, pageHeight));
                        }
                    }

                    if (blocks.Count > 0)
                    {
                        if (!popoDoc.PagesBlocks.ContainsKey(pageNum))
                            popoDoc.PagesBlocks[pageNum] = new List<PopoBlock>();

                        popoDoc.PagesBlocks[pageNum].AddRange(blocks);
                    }
                }
            }

            // 2. Parse tree -> TreeRoot
            if (root.TryGetProperty("tree", out var treeElem) && treeElem.ValueKind != JsonValueKind.Null)
            {
                popoDoc.TreeRoot = MapMinerUTreeNode(treeElem, pageSizeMap);
                popoDoc.BuildAggregationMap();
            }

            // 3. Build InferenceBlocks from PagesBlocks
            popoDoc.InferenceBlocks = popoDoc.GetAllBlocks();

            return popoDoc.InferenceBlocks.Count > 0 || popoDoc.TreeRoot is not null
                ? popoDoc
                : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Parses a MinerU result zip file and extracts PopoDocument.
    /// Searches for *_middle.json in the extracted files.
    /// </summary>
    public static PopoDocument? TryParseMinerUZip(string zipPath)
    {
        if (!File.Exists(zipPath))
            return null;

        var tempDir = Path.Combine(Path.GetDirectoryName(zipPath)!, $"extract_{Guid.NewGuid()}");
        try
        {
            ZipFile.ExtractToDirectory(zipPath, tempDir);
            return TryParseMinerUFromExtractedDir(tempDir);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PopoJson] Failed to parse MinerU zip: {ex.Message}");
            throw new MinerUServiceException($"Failed to parse MinerU output: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Parses an already-extracted MinerU output directory and builds a PopoDocument.
    /// Searches only for *_middle.json in the directory.
    /// </summary>
    /// <param name="extractedDir">Path to the extracted directory containing MinerU output files.</param>
    /// <returns>A PopoDocument if parsing succeeds, otherwise null.</returns>
    public static PopoDocument? TryParseMinerUFromExtractedDir(string extractedDir)
    {
        if (!Directory.Exists(extractedDir))
            return null;

        var jsonFiles = Directory.GetFiles(extractedDir, "*.json", SearchOption.AllDirectories);
        if (jsonFiles.Length == 0)
            return null;

        // Prefer standard MinerU middle JSON file names, but fall back to any parseable JSON.
        var preferredFiles = jsonFiles
            .Where(path => Path.GetFileName(path).EndsWith("_middle.json", StringComparison.OrdinalIgnoreCase)
                           || Path.GetFileName(path).Equals("middle.json", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var jsonFile in preferredFiles.Concat(jsonFiles.Except(preferredFiles)))
        {
            var popoDoc = TryParseMinerUMiddleJson(jsonFile);
            if (popoDoc is not null)
                return popoDoc;
        }

        return null;
    }

    #endregion

    #region Popo Result Parsing

    /// <summary>
    /// Tries to parse a Popo result directory (from Popo service).
    /// Searches for popo_result.json or any JSON file that contains a PopoDocument structure.
    /// </summary>
    public static PopoDocument? TryParsePopoResultDir(string resultDir)
    {
        if (!Directory.Exists(resultDir))
            return null;

        // Try popo_result.json first
        var popoResultJson = Path.Combine(resultDir, "popo_result.json");
        if (File.Exists(popoResultJson))
        {
            return TryParsePopoResultJson(popoResultJson);
        }

        // Try in extract subdirectory
        var extractDir = Path.Combine(resultDir, "extract");
        if (Directory.Exists(extractDir))
        {
            // Look for any JSON file in the extract directory
            var jsonFiles = Directory.GetFiles(extractDir, "*.json", SearchOption.AllDirectories);
            foreach (var jsonFile in jsonFiles)
            {
                var result = TryParsePopoResultJson(jsonFile);
                if (result is not null)
                    return result;
            }
        }

        // Fallback: try to parse as middle.json format
        return TryParseMinerUFromExtractedDir(resultDir);
    }

    /// <summary>
    /// Tries to parse a single JSON file as a PopoDocument result.
    /// </summary>
    static PopoDocument? TryParsePopoResultJson(string jsonPath)
    {
        if (!File.Exists(jsonPath))
            return null;

        try
        {
            var json = File.ReadAllText(jsonPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Try to deserialize directly as PopoDocument
            var popoDoc = JsonSerializer.Deserialize<PopoDocument>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (popoDoc is not null && (popoDoc.GetAllBlocks().Count > 0 || popoDoc.TreeRoot is not null))
                return popoDoc;

            // Try as middle.json format
            return TryParseMinerUMiddleJson(jsonPath);
        }
        catch
        {
            return null;
        }
    }

    #endregion

    #region MinerU Mapping Helpers

    /// <summary>
    /// Maps a MinerU block JsonElement to a PopoBlock.
    /// </summary>
    static PopoBlock MapMinerUBlockToPopoBlock(JsonElement elem, double pageWidth, double pageHeight)
    {
        var block = new PopoBlock();

        if (elem.TryGetProperty("id", out var idElem))
            block.Id = GetIntValue(idElem, block.Id);

        if (elem.TryGetProperty("page", out var pageElem))
            block.Page = GetIntValue(pageElem, block.Page);

        block.Content = GetStringProperty(elem, "content") ?? string.Empty;

        // Determine type from source_label or type field
        var minerUType = GetStringProperty(elem, "source_label")
                        ?? GetStringProperty(elem, "type")
                        ?? GetStringProperty(elem, "category")
                        ?? string.Empty;

        block.SourceLabel = minerUType;
        block.Type = MapMinerUTypeToPopoType(minerUType);

        // Parse bbox
        if (elem.TryGetProperty("bbox", out var bboxElem) || elem.TryGetProperty("box", out bboxElem))
        {
            block.Bbox = ParseMinerUBbox(bboxElem, pageWidth, pageHeight);
        }

        if (elem.TryGetProperty("contd", out var contdElem))
            block.Contd = GetIntValue(contdElem, block.Contd);

        if (elem.TryGetProperty("level", out var levelElem))
        {
            block.Level = GetIntValue(levelElem, block.Level);
            if (block.Type == "title")
                block.TitleLevel = block.Level;
        }

        if (elem.TryGetProperty("image", out var imageElem))
            block.Image = GetIntValue(imageElem, block.Image);

        if (elem.TryGetProperty("table_merge", out var mergeElem))
            block.TableMerge = GetIntValue(mergeElem, block.TableMerge);

        return block;
    }

    static IEnumerable<PopoBlock> MapMinerUPageSectionToBlocks(JsonElement sectionElem, int pageNum, double pageWidth, double pageHeight)
    {
        var results = new List<PopoBlock>();

        if (sectionElem.ValueKind != JsonValueKind.Object)
            return results;

        if (sectionElem.TryGetProperty("blocks", out var blocksElem) && blocksElem.ValueKind == JsonValueKind.Array)
        {
            foreach (var blockElem in blocksElem.EnumerateArray())
            {
                var block = MapMinerUBlockToPopoBlock(blockElem, pageWidth, pageHeight);
                if (block.Page <= 0)
                    block.Page = pageNum;
                results.Add(block);
            }
        }

        // Also support a direct content-bearing block object.
        if (sectionElem.TryGetProperty("lines", out var linesElem) && linesElem.ValueKind == JsonValueKind.Array)
        {
            var block = new PopoBlock
            {
                Id = results.Count + 1,
                Page = pageNum,
                Type = MapMinerUTypeToPopoType(GetStringProperty(sectionElem, "type") ?? string.Empty),
                SourceLabel = GetStringProperty(sectionElem, "type") ?? string.Empty,
                Content = ExtractMinerUContent(sectionElem),
                Bbox = ParseMinerUBbox(sectionElem.TryGetProperty("bbox", out var bboxElem) ? bboxElem : default, pageWidth, pageHeight)
            };
            results.Add(block);
        }

        return results;
    }

    static string ExtractMinerUContent(JsonElement elem)
    {
        if (elem.TryGetProperty("content", out var contentElem) && contentElem.ValueKind == JsonValueKind.String)
            return contentElem.GetString() ?? string.Empty;

        if (elem.TryGetProperty("lines", out var linesElem) && linesElem.ValueKind == JsonValueKind.Array)
        {
            var parts = new List<string>();
            foreach (var lineElem in linesElem.EnumerateArray())
            {
                if (lineElem.TryGetProperty("spans", out var spansElem) && spansElem.ValueKind == JsonValueKind.Array)
                {
                    foreach (var spanElem in spansElem.EnumerateArray())
                    {
                        var content = GetStringProperty(spanElem, "content");
                        if (!string.IsNullOrEmpty(content))
                            parts.Add(content);
                    }
                }
            }

            return string.Join("\n", parts);
        }

        return string.Empty;
    }

    /// <summary>
    /// Maps MinerU source_label/type to PopoBlock.Type.
    /// </summary>
    static string MapMinerUTypeToPopoType(string minerUType)
    {
        if (string.IsNullOrEmpty(minerUType))
            return "text";

        var type = minerUType.ToLowerInvariant().Trim();

        return type switch
        {
            "paragraph_title" or "title" or "section_title" => "title",
            "paragraph" or "text" or "plain_text" or "body_text" => "text",
            "figure" or "image" or "picture" or "photo" => "image",
            "table" or "tabular" => "table",
            "figure_footnote" or "table_footnote" or "caption" or "image_caption" or "table_caption" => "caption",
            "header" or "footnote" or "footer" => "text",
            "equation" or "formula" => "text",
            "list" or "list_item" => "text",
            _ => "text"  // Default to text for unknown types
        };
    }

    /// <summary>
    /// Parses a bbox array [x1, y1, x2, y2] from MinerU, with optional normalization.
    /// If page dimensions are provided and coordinates appear to be absolute (large values),
    /// normalizes to 0-1 range.
    /// </summary>
    static Rect ParseMinerUBbox(JsonElement elem, double pageWidth = 0, double pageHeight = 0)
    {
        if (elem.ValueKind != JsonValueKind.Array || elem.GetArrayLength() < 4)
            return new Rect(0, 0, 0, 0);

        var x1 = GetDoubleValue(elem[0]);
        var y1 = GetDoubleValue(elem[1]);
        var x2 = GetDoubleValue(elem[2]);
        var y2 = GetDoubleValue(elem[3]);

        // If page dimensions provided and coordinates look like absolute pixels (> 1000),
        // normalize to 0-1 range
        if (pageWidth > 0 && pageHeight > 0 && (x2 > 1000 || y2 > 1000))
        {
            x1 /= pageWidth;
            y1 /= pageHeight;
            x2 /= pageWidth;
            y2 /= pageHeight;
        }

        // Ensure valid bounds
        var width = Math.Max(0, x2 - x1);
        var height = Math.Max(0, y2 - y1);

        return new Rect((double)x1, (double)y1, (double)width, (double)height);
    }

    /// <summary>
    /// Recursively maps a MinerU tree node JsonElement to a PopoTreeNode.
    /// </summary>
    static PopoTreeNode MapMinerUTreeNode(JsonElement elem, Dictionary<int, (double width, double height)> pageSizeMap)
    {
        var node = new PopoTreeNode();

        node.Type = GetStringProperty(elem, "type") ?? string.Empty;
        node.Title = GetStringProperty(elem, "title") ?? string.Empty;
        node.Metadata = GetStringProperty(elem, "metadata") ?? string.Empty;
        node.Content = GetStringProperty(elem, "content") ?? string.Empty;

        if (elem.TryGetProperty("level", out var levelElem))
            node.Level = levelElem.GetInt32();

        // Parse location entries
        if (elem.TryGetProperty("location", out var locElem) && locElem.ValueKind == JsonValueKind.Array)
        {
            foreach (var loc in locElem.EnumerateArray())
            {
                var entry = new LocationEntry();

                if (loc.TryGetProperty("page", out var pageElem))
                    entry.Page = GetIntValue(pageElem, entry.Page);

                if (loc.TryGetProperty("bbox", out var bboxElem))
                {
                    var pw = pageSizeMap.TryGetValue(entry.Page, out var s) ? s.width : 0.0;
                    var ph = pageSizeMap.TryGetValue(entry.Page, out s) ? s.height : 0.0;
                    entry.Bbox = ParseMinerUBbox(bboxElem, pw, ph);
                }

                node.Location.Add(entry);
            }
        }

        // Parse block_ids
        if (elem.TryGetProperty("block_ids", out var idsElem) && idsElem.ValueKind == JsonValueKind.Array)
        {
            foreach (var id in idsElem.EnumerateArray())
            {
                node.BlockIds.Add(GetIntValue(id));
            }
        }

        // Recursively parse children
        if (elem.TryGetProperty("children", out var childrenElem) && childrenElem.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in childrenElem.EnumerateArray())
            {
                node.Children.Add(MapMinerUTreeNode(child, pageSizeMap));
            }
        }

        return node;
    }

    /// <summary>
    /// Helper to safely get a string property from a JsonElement.
    /// </summary>
    static string? GetStringProperty(JsonElement elem, string propertyName)
    {
        if (elem.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
            return value.GetString();
        return null;
    }

    static int GetIntValue(JsonElement elem, int defaultValue = 0)
    {
        return elem.ValueKind switch
        {
            JsonValueKind.Number => elem.GetInt32(),
            JsonValueKind.String when int.TryParse(elem.GetString(), out var value) => value,
            _ => defaultValue
        };
    }

    static double GetDoubleValue(JsonElement elem, double defaultValue = 0.0)
    {
        return elem.ValueKind switch
        {
            JsonValueKind.Number => elem.GetDouble(),
            JsonValueKind.String when double.TryParse(elem.GetString(), out var value) => value,
            _ => defaultValue
        };
    }

    #endregion
}
