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
using System.Text.Json.Serialization.Metadata;

namespace Caly.Core.Services;

/// <summary>
/// Service for loading and parsing Popo JSON files.
/// Supports three JSON formats: normalization, inference, and tree output.
/// Also supports Popo service result directories.
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
    /// Shared JSON serializer options with RectJsonConverter for bbox array/object format compatibility.
    /// </summary>
    internal static readonly JsonSerializerOptions DefaultDeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        TypeInfoResolver = SourceGenerationContext.Default,
        Converters = { new RectJsonConverter() }
    };

    private static readonly JsonSerializerOptions s_defaultSerializeOptions = new()
    {
        WriteIndented = true,
        TypeInfoResolver = SourceGenerationContext.Default,
        Converters = { new RectJsonConverter() }
    };

    /// <summary>
    /// JSON serializer options for StructureDocument serialization/deserialization.
    /// Uses reflection-based resolver because StructureDocument uses [ObservableProperty]
    /// whose generated properties are not visible to the JSON source generator.
    /// </summary>
    internal static readonly JsonSerializerOptions StructureDocumentOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        Converters = { new RectJsonConverter() }
    };

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
    /// Loads a StructureDocument from a project's popo/ directory.
    /// Looks for popo.json in the project's popo/ subdirectory only.
    /// </summary>
    public static StructureDocument? LoadStructureDocumentFromProject(string? projectPath)
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
                return JsonSerializer.Deserialize<StructureDocument>(json, StructureDocumentOptions);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Saves a StructureDocument to a project's popo/ directory.
    /// </summary>
    public static void SaveStructureDocumentToProject(StructureDocument doc, string projectPath)
    {
        if (string.IsNullOrEmpty(projectPath))
            return;

        var popoDir = Path.Combine(projectPath, "popo");
        Directory.CreateDirectory(popoDir);

        var popoJsonPath = Path.Combine(popoDir, "popo.json");
        var json = JsonSerializer.Serialize(doc, StructureDocumentOptions);
        File.WriteAllText(popoJsonPath, json);
    }

    /// <summary>
    /// Loads a complete StructureDocument from JSON files.
    /// </summary>
    public static StructureDocument? LoadStructureDocument(string pdfPath, string modelName = DefaultModelName)
    {
        var (normalizedJson, inferenceJson, treeJson) = FindPopoJsonPaths(pdfPath, modelName);

        var doc = new StructureDocument();
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
    internal static StructureDocument LoadNormalizationJson(string jsonPath)
    {
        var doc = new StructureDocument();
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

                var blocks = new List<MinerUBlock>();
                var blockList = pageEntry.Value;

                int order = 0;
                foreach (var blockElem in blockList.EnumerateArray())
                {
                    var block = new MinerUBlock
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
    internal static List<MinerUBlock>? LoadInferenceJson(string jsonPath)
    {
        var json = File.ReadAllText(jsonPath);
        var doc = JsonDocument.Parse(json);

        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return null;

        var blocks = new List<MinerUBlock>();

        foreach (var elem in doc.RootElement.EnumerateArray())
        {
            var block = new MinerUBlock();

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
    internal static AnalysisTreeNode? LoadTreeJson(string jsonPath)
    {
        var json = File.ReadAllText(jsonPath);
        var doc = JsonDocument.Parse(json);

        return ParseTreeNode(doc.RootElement);
    }

    private static AnalysisTreeNode ParseTreeNode(JsonElement elem)
    {
        var node = new AnalysisTreeNode();

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

        // Parse img_path (Popo API returns relative path like "images/xxx.jpg")
        if (elem.TryGetProperty("img_path", out var imgPathElem))
            node.ImgPath = imgPathElem.GetString() ?? string.Empty;

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

    // Extension method for StructureDocument to populate PagesBlocks from InferenceBlocks
    private static void PopulatePagesBlocksFromInference(this StructureDocument doc)
    {
        var pages = new Dictionary<int, List<MinerUBlock>>();

        foreach (var block in doc.InferenceBlocks)
        {
            if (!pages.TryGetValue(block.Page, out var pageBlocks))
            {
                pageBlocks = new List<MinerUBlock>();
                pages[block.Page] = pageBlocks;
            }
            pageBlocks.Add(block);
        }

        doc.PagesBlocks = pages;
    }

    #region Popo Result Parsing

    /// <summary>
    /// Tries to parse a Popo result directory (from Popo service).
    /// Searches for popo_result.json, extract/ JSON files, standard outputs/ structure,
    /// or falls back to MinerU middle.json format.
    /// </summary>
    public static StructureDocument? TryParsePopoResultDir(string resultDir)
    {
        if (!Directory.Exists(resultDir))
            return null;

        // Strategy 1: Direct popo_result.json file
        var popoResultJson = Path.Combine(resultDir, "popo_result.json");
        if (File.Exists(popoResultJson))
        {
            var result = TryParsePopoResultJson(popoResultJson);
            if (result is not null)
                return result;
        }

        // Strategy 2: Extract subdirectory from ZIP extraction
        var extractDir = Path.Combine(resultDir, "extract");
        if (Directory.Exists(extractDir))
        {
            // 2a: Prefer standard MinerU *_middle.json files first (default MinerU output)
            var middleJsonFiles = Directory.GetFiles(extractDir, "*_middle.json", SearchOption.AllDirectories);
            foreach (var middleJson in middleJsonFiles)
            {
                var result = MinerUJsonService.TryParseMinerUMiddleJson(middleJson);
                if (result is not null)
                    return result;
            }

            // 2b: Try other JSON files as Popo-serialized StructureDocument format
            var otherJsonFiles = Directory.GetFiles(extractDir, "*.json", SearchOption.AllDirectories)
                .Where(f => !Path.GetFileName(f).EndsWith("_middle.json", StringComparison.OrdinalIgnoreCase));
            foreach (var jsonFile in otherJsonFiles)
            {
                var result = TryParsePopoResultJson(jsonFile);
                if (result is not null)
                    return result;
            }

            // 2c: Try standard outputs/ structure inside extract/
            var outputsDir = Path.Combine(extractDir, "outputs");
            if (Directory.Exists(outputsDir))
            {
                var result = LoadFromOutputsDirectory(outputsDir);
                if (result is not null)
                    return result;
            }
        }

        // Strategy 3: Standard outputs/ directory at the top level
        var topOutputsDir = Path.Combine(resultDir, "outputs");
        if (Directory.Exists(topOutputsDir))
        {
            var result = LoadFromOutputsDirectory(topOutputsDir);
            if (result is not null)
                return result;
        }

        // Strategy 4: Fallback to MinerU middle.json format
        return MinerUJsonService.TryParseMinerUFromExtractedDir(resultDir);
    }

    /// <summary>
    /// Loads a StructureDocument from the standard Popo outputs/ directory structure:
    ///   outputs/label_normalization/{model}/{docId}.json
    ///   outputs/inference/{model}/{docId}.json
    ///   outputs/build_tree/{model}/{docId}.json
    /// </summary>
    private static StructureDocument? LoadFromOutputsDirectory(string outputsDir)
    {
        // Find the first available JSON file to determine docId and model
        string? docId = null;
        string? modelName = null;

        foreach (var stage in new[] { "label_normalization", "inference", "build_tree" })
        {
            var stageDir = Path.Combine(outputsDir, stage);
            if (!Directory.Exists(stageDir))
                continue;

            foreach (var modelDir in Directory.GetDirectories(stageDir))
            {
                foreach (var jsonFile in Directory.GetFiles(modelDir, "*.json"))
                {
                    docId = Path.GetFileNameWithoutExtension(jsonFile);
                    modelName = Path.GetFileName(modelDir);
                    goto found;
                }
            }
        }
        found:

        if (docId is null)
            return null;

        var doc = new StructureDocument { DocId = docId, ModelName = modelName ?? DefaultModelName };

        // Load normalization
        var (normalized, inference, tree) = FindPopoJsonPathsInOutputs(outputsDir, modelName ?? DefaultModelName, docId);
        var loaded = false;

        if (normalized is not null)
        {
            var normDoc = LoadNormalizationJson(normalized);
            doc.PagesBlocks = normDoc.PagesBlocks;
            doc.ModelName = normDoc.ModelName;
            doc.DocId = normDoc.DocId;
            loaded = true;
        }

        if (inference is not null)
        {
            var infBlocks = LoadInferenceJson(inference);
            if (infBlocks is not null)
            {
                doc.InferenceBlocks = infBlocks;
                if (!loaded && infBlocks.Count > 0)
                    doc.PopulatePagesBlocksFromInference();
                loaded = true;
            }
        }

        if (tree is not null)
        {
            doc.TreeRoot = LoadTreeJson(tree);
            if (doc.TreeRoot is not null)
            {
                doc.BuildAggregationMap();
                loaded = true;
            }
        }

        return loaded ? doc : null;
    }

    /// <summary>
    /// Finds Popo JSON files within a given outputs/ directory.
    /// </summary>
    private static (string? normalized, string? inference, string? tree) FindPopoJsonPathsInOutputs(
        string outputsDir, string modelName, string docId)
    {
        string? FindInStage(string stage)
        {
            // Try specific model first
            var path = Path.Combine(outputsDir, stage, modelName, $"{docId}.json");
            if (File.Exists(path))
                return path;

            // Fallback: any model directory
            var stageDir = Path.Combine(outputsDir, stage);
            if (Directory.Exists(stageDir))
            {
                foreach (var modelDir in Directory.GetDirectories(stageDir))
                {
                    var fallbackPath = Path.Combine(modelDir, $"{docId}.json");
                    if (File.Exists(fallbackPath))
                        return fallbackPath;
                }
            }
            return null;
        }

        return (FindInStage("label_normalization"), FindInStage("inference"), FindInStage("build_tree"));
    }

    /// <summary>
    /// <summary>
    /// Tries to parse a single JSON file as a Popo-serialized StructureDocument.
    /// Handles two formats:
    ///   1. Direct StructureDocument JSON (e.g., popo.json)
    ///   2. PopoTaskResultResponse wrapper (e.g., result_*/popo_result.json)
    ///      which contains task_id/status/result.tree.
    /// Callers should route MinerU *_middle.json files to TryParseMinerUMiddleJson directly.
    ///
    /// Performance note: uses JsonDocument.Parse to inspect the root structure first,
    /// avoiding double deserialization. If the root has a "result" property, it's the
    /// PopoTaskResultResponse wrapper format; otherwise, it's a direct StructureDocument.
    /// </summary>
    static StructureDocument? TryParsePopoResultJson(string jsonPath)
    {
        if (!File.Exists(jsonPath))
            return null;

        try
        {
            var json = File.ReadAllText(jsonPath);

            // Inspect root structure first to determine format — avoids double deserialization
            using var rootDoc = JsonDocument.Parse(json);
            var root = rootDoc.RootElement;
            bool isWrapperFormat = root.TryGetProperty("result", out _);

            if (isWrapperFormat)
            {
                // PopoTaskResultResponse wrapper format (popo_result.json):
                //   { task_id, status, result: { doc_id, status, message, tree: {...} } }
                // Use JsonDocument to extract the tree and doc_id, then use ParseTreeNode
                // to correctly parse img_path (which reflection-based deserialization misses).
                if (root.TryGetProperty("result", out var resultElem) &&
                    resultElem.TryGetProperty("tree", out var treeElem))
                {
                    var treeRoot = ParseTreeNode(treeElem);
                    if (treeRoot is not null)
                    {
                        var docId = resultElem.TryGetProperty("doc_id", out var docIdElem)
                            ? docIdElem.GetString() ?? string.Empty
                            : string.Empty;
                        return BuildStructureDocumentFromTree(treeRoot, docId, DefaultModelName);
                    }
                }
            }
            else
            {
                // Direct StructureDocument format (popo.json):
                //   { docId, modelName, pagesBlocks, treeRoot, ... }
                var minerUDoc = JsonSerializer.Deserialize<StructureDocument>(json, StructureDocumentOptions);
                if (minerUDoc is not null && (minerUDoc.GetAllBlocks().Count > 0 || minerUDoc.TreeRoot is not null))
                    return minerUDoc;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Builds a StructureDocument from a tree root returned by the Popo API.
    /// Flattens the tree into per-page blocks, populates TreeRoot, and builds aggregation map.
    /// </summary>
    /// <param name="treeRoot">The root node of the document tree from the Popo API.</param>
    /// <param name="docId">Document ID.</param>
    /// <param name="modelName">Model name (optional, used as task/model identifier).</param>
    public static StructureDocument BuildStructureDocumentFromTree(
        AnalysisTreeNode treeRoot,
        string docId,
        string modelName = "mineru")
    {
        var doc = new StructureDocument
        {
            DocId = docId,
            ModelName = modelName,
            TreeRoot = treeRoot
        };

        var pageBlocks = new Dictionary<int, List<MinerUBlock>>();
        var allBlocks = new List<MinerUBlock>();
        int blockId = 0;

        // Flatten the tree recursively, skipping the root node itself
        ExpandTreeToBlocks(treeRoot, pageBlocks, allBlocks, ref blockId);

        doc.PagesBlocks = pageBlocks;
        doc.InferenceBlocks = allBlocks;
        doc.BuildAggregationMap();

        return doc;
    }

    /// <summary>
    /// Recursively flattens tree nodes into MinerUBlock objects, grouped by page.
    /// When a tree node has multiple locations (bboxes), creates one block per location.
    /// All blocks from the same tree node share the same OriginalBlockIds for cross-referencing.
    /// Skips the root node (level 0, type "root") and non-content nodes.
    /// </summary>
    private static void ExpandTreeToBlocks(
        AnalysisTreeNode node,
        Dictionary<int, List<MinerUBlock>> pageBlocks,
        List<MinerUBlock> allBlocks,
        ref int blockId)
    {
        // Process non-root nodes that have location data
        bool isRoot = node.Type == "root" && node.Level == 0;

        if (!isRoot && node.Location.Count > 0)
        {
            // Create one block per location entry, preserving original block_ids for cross-reference
            var originalIds = new List<int>(node.BlockIds);

            for (int i = 0; i < node.Location.Count; i++)
            {
                var loc = node.Location[i];
                int page = loc.Page;

                var block = new MinerUBlock
                {
                    Id = blockId,
                    Page = page,
                    Type = node.Type,
                    Content = !string.IsNullOrEmpty(node.Content) ? node.Content : node.Title,
                    Bbox = loc.Bbox,
                    TitleLevel = node.Level,
                    OriginalBlockIds = originalIds
                };

                if (!pageBlocks.ContainsKey(page))
                    pageBlocks[page] = new List<MinerUBlock>();

                pageBlocks[page].Add(block);
                allBlocks.Add(block);
                blockId++;
            }
        }

        // Recurse into children
        foreach (var child in node.Children)
        {
            ExpandTreeToBlocks(child, pageBlocks, allBlocks, ref blockId);
        }
    }

    /// <summary>
    /// Populates block_ids for tree nodes by matching bboxes against the middle.json.
    /// This is needed because the Popo API often returns empty block_ids for image nodes.
    /// </summary>
    public static void PopulateTreeBlockIds(StructureDocument doc, string? artifactsDir)
    {
        if (doc.TreeRoot is null || artifactsDir is null)
            return;

        // Find middle.json
        var middleJsons = Directory.GetFiles(artifactsDir, "*_middle.json", SearchOption.AllDirectories);
        if (middleJsons.Length == 0)
            return;

        var middleJson = middleJsons[0];
        try
        {
            var json = File.ReadAllText(middleJson);
            using var docJson = System.Text.Json.JsonDocument.Parse(json);
            PopulateTreeBlockIdsRecursive(doc.TreeRoot, docJson.RootElement);
        }
        catch { }
    }

    private static void PopulateTreeBlockIdsRecursive(AnalysisTreeNode node, System.Text.Json.JsonElement elem)
    {
        if (elem.ValueKind == System.Text.Json.JsonValueKind.Object &&
            elem.TryGetProperty("pdf_info", out var pdfInfo))
        {
            foreach (var page in pdfInfo.EnumerateArray())
            {
                var pageIdx = 0;
                if (page.TryGetProperty("page_idx", out var pi))
                    pageIdx = pi.GetInt32();

                var pageW = 1000.0;
                var pageH = 1000.0;
                if (page.TryGetProperty("page_size", out var ps))
                {
                    var psArr = ps.EnumerateArray().Select(v => v.GetDouble()).ToArray();
                    if (psArr.Length >= 2)
                    {
                        pageW = psArr[0];
                        pageH = psArr[1];
                    }
                }

                if (page.TryGetProperty("para_blocks", out var blocks))
                {
                    MatchTreeNodesToBlocks(node, blocks, pageIdx, pageW, pageH);
                }
            }
        }

        // Recurse into children
        foreach (var child in node.Children)
        {
            PopulateTreeBlockIdsRecursive(child, elem);
        }
    }

    private static void MatchTreeNodesToBlocks(
        AnalysisTreeNode treeNode,
        System.Text.Json.JsonElement blocks,
        int pageIdx,
        double pageW,
        double pageH)
    {
        foreach (var block in blocks.EnumerateArray())
        {
            if (treeNode.Location.Count == 0)
                continue;

            // Check if this block's page matches any of the tree node's pages
            var treePages = treeNode.Location.Select(l => l.Page).ToList();
            if (!treePages.Contains(pageIdx + 1)) // Popo uses 1-based, middle.json uses 0-based
                continue;

            // Check if bboxes overlap significantly
            if (!block.TryGetProperty("bbox", out var bbox))
                continue;

            var bboxArr = bbox.EnumerateArray().Select(v => v.GetDouble()).ToArray();
            if (bboxArr.Length < 4)
                continue;

            var absX1 = bboxArr[0];
            var absY1 = bboxArr[1];
            var absX2 = bboxArr[2];
            var absY2 = bboxArr[3];
            var normX1 = absX1 / pageW;
            var normY1 = absY1 / pageH;
            var normX2 = absX2 / pageW;
            var normY2 = absY2 / pageH;

            // Check if this bbox matches any of the tree node's locations
            foreach (var loc in treeNode.Location)
            {
                if (loc.Page != pageIdx + 1)
                    continue;

                // Check bbox overlap
                var treeX1 = loc.Bbox.X;
                var treeY1 = loc.Bbox.Y;
                var treeX2 = treeX1 + loc.Bbox.Width;
                var treeY2 = treeY1 + loc.Bbox.Height;

                // Calculate overlap
                var overlapX1 = Math.Max(normX1, treeX1);
                var overlapY1 = Math.Max(normY1, treeY1);
                var overlapX2 = Math.Min(normX2, treeX2);
                var overlapY2 = Math.Min(normY2, treeY2);

                if (overlapX1 < overlapX2 && overlapY1 < overlapY2)
                {
                    // Found overlap - get the block id
                    if (block.TryGetProperty("id", out var id))
                    {
                        if (id.ValueKind == System.Text.Json.JsonValueKind.Number)
                        {
                            treeNode.BlockIds.Add(id.GetInt32());
                        }
                    }
                }
            }
        }

        // Recurse into children
        foreach (var child in treeNode.Children)
        {
            MatchTreeNodesToBlocks(child, blocks, pageIdx, pageW, pageH);
        }
    }

    #endregion
}
