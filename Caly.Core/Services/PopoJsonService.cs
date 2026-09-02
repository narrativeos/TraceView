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
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;

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
        Converters = { new RectJsonConverter() },
        MaxDepth = 256
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
        Converters = { new RectJsonConverter(), new TableHeaderJsonConverter() },
        MaxDepth = 256
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

    internal static AnalysisTreeNode ParseTreeNode(JsonElement elem)
    {
        var node = new AnalysisTreeNode();

        // Guard: if the element itself is not an Object (e.g., a plain string in children array),
        // return an empty node rather than throwing.
        if (elem.ValueKind != JsonValueKind.Object)
        {
            // Store non-object values as content so they aren't silently lost.
            if (elem.ValueKind == JsonValueKind.String)
                node.Content = elem.GetString() ?? string.Empty;
            return node;
        }

        if (elem.TryGetProperty("type", out var typeElem))
            node.Type = typeElem.GetString() ?? string.Empty;

        if (elem.TryGetProperty("title", out var titleElem))
            node.Title = titleElem.GetString() ?? string.Empty;

        if (elem.TryGetProperty("metadata", out var metaElem))
            node.Metadata = metaElem.GetString() ?? string.Empty;

        if (elem.TryGetProperty("content", out var contentElem))
            node.Content = contentElem.GetString() ?? string.Empty;

        // For table nodes: if content is empty or a placeholder like "[table]",
        // extract actual content from table_body HTML and table_caption fields.
        if (node.Type == "table" && IsTablePlaceholder(node.Content))
        {
            var tableContent = ExtractTableContent(elem);
            if (!string.IsNullOrEmpty(tableContent))
                node.Content = tableContent;
        }

        // For legacy table nodes with HTML content: parse into a grid for DataGrid display
        if (node.Type == "table" && node.Content.Contains("<table", StringComparison.OrdinalIgnoreCase))
        {
            node.GridRows = HtmlTableParser.Parse(node.Content);
        }

        if (elem.TryGetProperty("level", out var levelElem))
            node.Level = levelElem.GetInt32();

        // Parse location — guard against non-array values
        if (elem.TryGetProperty("location", out var locElem) &&
            locElem.ValueKind == JsonValueKind.Array)
        {
            foreach (var loc in locElem.EnumerateArray())
            {
                // Guard: skip non-object entries in location array
                if (loc.ValueKind != JsonValueKind.Object)
                    continue;

                var entry = new LocationEntry();

                if (loc.TryGetProperty("bbox", out var bboxElem))
                    entry.Bbox = ParseBbox(bboxElem);

                if (loc.TryGetProperty("page", out var pageElem))
                    entry.Page = pageElem.GetInt32();

                // Parse row_range (row span within the source table, for structured table sub-nodes)
                if (loc.TryGetProperty("row_range", out var rowRangeElem) &&
                    rowRangeElem.ValueKind == JsonValueKind.Array &&
                    rowRangeElem.GetArrayLength() >= 2)
                {
                    entry.RowRange = new int[rowRangeElem.GetArrayLength()];
                    for (int i = 0; i < rowRangeElem.GetArrayLength(); i++)
                        entry.RowRange[i] = rowRangeElem[i].GetInt32();
                }

                node.Location.Add(entry);
            }
        }

        // Parse img_path (Popo API returns relative path like "images/xxx.jpg")
        if (elem.TryGetProperty("img_path", out var imgPathElem))
            node.ImgPath = imgPathElem.GetString() ?? string.Empty;

        // Parse node_id (unique node identifier from the Popo API)
        if (elem.TryGetProperty("node_id", out var nodeIdElem))
            node.NodeId = nodeIdElem.GetString() ?? string.Empty;

        // Parse block_ids — guard against non-array values
        if (elem.TryGetProperty("block_ids", out var idsElem) &&
            idsElem.ValueKind == JsonValueKind.Array)
        {
            foreach (var id in idsElem.EnumerateArray())
            {
                node.BlockIds.Add(id.GetInt32());
            }
        }

        // Parse source_block_ids (UUID strings from MinerU middle.json) — guard against non-array values
        if (elem.TryGetProperty("source_block_ids", out var sourceIdsElem) &&
            sourceIdsElem.ValueKind == JsonValueKind.Array)
        {
            foreach (var sourceId in sourceIdsElem.EnumerateArray())
            {
                var sourceIdStr = sourceId.GetString();
                if (!string.IsNullOrEmpty(sourceIdStr))
                    node.SourceBlockIds.Add(sourceIdStr);
            }
        }

        // Parse children recursively — guard against non-array values
        if (elem.TryGetProperty("children", out var childrenElem) &&
            childrenElem.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in childrenElem.EnumerateArray())
            {
                node.Children.Add(ParseTreeNode(child));
            }
        }

        // Parse headers (column definitions for table_data nodes) — guard against non-array values
        if (elem.TryGetProperty("headers", out var headersElem) &&
            headersElem.ValueKind == JsonValueKind.Array)
        {
            var headers = new List<TableHeader>();
            foreach (var header in headersElem.EnumerateArray())
            {
                // Headers may be plain strings (legacy) or objects:
                // { "index": 0, "name": "...", "group": "...", "unit": "..." }
                if (header.ValueKind == JsonValueKind.String)
                {
                    var name = header.GetString();
                    if (!string.IsNullOrEmpty(name))
                        headers.Add(new TableHeader { Name = name });
                }
                else if (header.ValueKind == JsonValueKind.Object)
                {
                    var tableHeader = new TableHeader();
                    if (header.TryGetProperty("index", out var indexElem) && indexElem.ValueKind == JsonValueKind.Number)
                        tableHeader.Index = indexElem.GetInt32();
                    if (header.TryGetProperty("name", out var nameElem))
                        tableHeader.Name = nameElem.GetString() ?? string.Empty;
                    if (header.TryGetProperty("group", out var groupElem) && groupElem.ValueKind == JsonValueKind.String)
                        tableHeader.Group = groupElem.GetString();
                    if (header.TryGetProperty("unit", out var unitElem) && unitElem.ValueKind == JsonValueKind.String)
                        tableHeader.Unit = unitElem.GetString();
                    if (!string.IsNullOrEmpty(tableHeader.Name))
                        headers.Add(tableHeader);
                }
            }
            node.Headers = headers;
        }

        // Parse rows (structured data rows for table_data nodes) — guard against non-array values
        if (elem.TryGetProperty("rows", out var rowsElem) &&
            rowsElem.ValueKind == JsonValueKind.Array)
        {
            var rows = new List<TableDataRow>();
            foreach (var rowElem in rowsElem.EnumerateArray())
            {
                // Guard: skip non-object entries in rows array
                if (rowElem.ValueKind != JsonValueKind.Object)
                    continue;

                var row = new TableDataRow();
                if (rowElem.TryGetProperty("row_index", out var rowIndexElem))
                    row.RowIndex = rowIndexElem.GetInt32();
                if (rowElem.TryGetProperty("page", out var rowPageElem))
                    row.Page = rowPageElem.GetInt32();
                if (rowElem.TryGetProperty("cells", out var cellsElem) &&
                    cellsElem.ValueKind == JsonValueKind.Object)
                {
                    foreach (var cellProp in cellsElem.EnumerateObject())
                    {
                        row.Cells[cellProp.Name] = ParseTableCellValue(cellProp.Value);
                    }
                }
                rows.Add(row);
            }
            node.Rows = rows;
        }

        // Parse pairs (key-value pairs for table_key_value nodes) — guard against non-array values
        if (elem.TryGetProperty("pairs", out var pairsElem) &&
            pairsElem.ValueKind == JsonValueKind.Array)
        {
            var pairs = new List<TableKeyValuePair>();
            foreach (var pairElem in pairsElem.EnumerateArray())
            {
                // Guard: skip non-object entries in pairs array
                if (pairElem.ValueKind != JsonValueKind.Object)
                    continue;

                var pair = new TableKeyValuePair();
                if (pairElem.TryGetProperty("key", out var pairKeyElem))
                    pair.Key = pairKeyElem.GetString() ?? string.Empty;
                if (pairElem.TryGetProperty("value", out var pairValueElem))
                    pair.Value = ParseTableCellValue(pairValueElem);
                if (pairElem.TryGetProperty("row_index", out var pairRowIndexElem))
                    pair.RowIndex = pairRowIndexElem.GetInt32();
                if (pairElem.TryGetProperty("page", out var pairPageElem))
                    pair.Page = pairPageElem.GetInt32();
                pairs.Add(pair);
            }
            node.Pairs = pairs;
        }

        // Parse node-level toc (in-table sections for table nodes) — guard against non-array values
        if (elem.TryGetProperty("toc", out var tocElem) &&
            tocElem.ValueKind == JsonValueKind.Array)
        {
            var toc = new List<TocEntry>();
            foreach (var tocEntry in tocElem.EnumerateArray())
            {
                // Guard: skip non-object entries in toc array
                if (tocEntry.ValueKind != JsonValueKind.Object)
                    continue;

                var entry = new TocEntry { Type = "toc" };
                if (tocEntry.TryGetProperty("title", out var tocTitleElem))
                    entry.Title = tocTitleElem.GetString() ?? string.Empty;
                if (tocEntry.TryGetProperty("numbering", out var tocNumberingElem))
                    entry.Numbering = tocNumberingElem.GetString() ?? string.Empty;
                if (tocEntry.TryGetProperty("level", out var tocLevelElem))
                    entry.Level = tocLevelElem.GetInt32();
                if (tocEntry.TryGetProperty("page", out var tocPageElem))
                    entry.Page = tocPageElem.GetInt32();
                if (tocEntry.TryGetProperty("source", out var tocSourceElem))
                    entry.Source = tocSourceElem.GetString() ?? string.Empty;
                if (tocEntry.TryGetProperty("row_range", out var tocRowRangeElem) &&
                    tocRowRangeElem.ValueKind == JsonValueKind.Array &&
                    tocRowRangeElem.GetArrayLength() >= 2)
                {
                    entry.RowRange = new int[tocRowRangeElem.GetArrayLength()];
                    for (int i = 0; i < tocRowRangeElem.GetArrayLength(); i++)
                        entry.RowRange[i] = tocRowRangeElem[i].GetInt32();
                }
                toc.Add(entry);
            }
            node.Toc = toc;
        }

        // Parse ordered_lists (ordered lists detected in the node's content) — guard against non-array values
        if (elem.TryGetProperty("ordered_lists", out var orderedListsElem) &&
            orderedListsElem.ValueKind == JsonValueKind.Array)
        {
            var orderedLists = new List<OrderedListEntry>();
            foreach (var listElem in orderedListsElem.EnumerateArray())
            {
                // Guard: skip non-object entries in ordered_lists array
                if (listElem.ValueKind != JsonValueKind.Object)
                    continue;

                var list = new OrderedListEntry();
                if (listElem.TryGetProperty("items", out var itemsElem) &&
                    itemsElem.ValueKind == JsonValueKind.Array)
                {
                    foreach (var itemElem in itemsElem.EnumerateArray())
                    {
                        if (itemElem.ValueKind != JsonValueKind.Object)
                            continue;

                        var item = new OrderedListItem();
                        if (itemElem.TryGetProperty("num", out var numElem) &&
                            numElem.ValueKind == JsonValueKind.Number)
                            item.Num = numElem.GetInt32();
                        if (itemElem.TryGetProperty("text", out var itemTextElem))
                            item.Text = itemTextElem.GetString() ?? string.Empty;
                        list.Items.Add(item);
                    }
                }
                if (listElem.TryGetProperty("segment_range", out var segRangeElem) &&
                    segRangeElem.ValueKind == JsonValueKind.Array &&
                    segRangeElem.GetArrayLength() >= 2)
                {
                    list.SegmentRange = new int[segRangeElem.GetArrayLength()];
                    for (int i = 0; i < segRangeElem.GetArrayLength(); i++)
                        list.SegmentRange[i] = segRangeElem[i].GetInt32();
                }
                if (listElem.TryGetProperty("starts_at_one", out var startsAtOneElem) &&
                    startsAtOneElem.ValueKind == JsonValueKind.True)
                    list.StartsAtOne = true;
                orderedLists.Add(list);
            }
            node.OrderedLists = orderedLists;
        }

        return node;
    }

    /// <summary>
    /// Builds the structured display lines of a node's content: the content is
    /// split on &lt;|txt_split|&gt; into segments, and segments falling inside an
    /// ordered list's segment_range (0-based, inclusive) are rendered as
    /// numbered list items (using the list's item number and cleaned text);
    /// all other non-empty segments are rendered as plain paragraphs.
    /// Returns an empty list when the node has no ordered lists or no content,
    /// or when the node is an aggregation node (it carries its children's
    /// ordered lists but no content of its own — the children render the
    /// lists, so rendering them here would duplicate them).
    /// </summary>
    internal static List<ContentDisplayLine> BuildContentLines(AnalysisTreeNode node)
    {
        var lines = new List<ContentDisplayLine>();
        if (node.OrderedLists is not { Count: > 0 } lists)
            return lines;

        // Aggregation nodes: the Popo API aggregates a chapter's ordered lists
        // onto the chapter node (which has no content of its own). The children
        // render their own lists, so return empty here to avoid duplicating them.
        // Nodes that have their own content are never treated as aggregation
        // nodes, even when their children also carry lists.
        if (string.IsNullOrEmpty(node.Content) &&
            node.Children.Any(c => c.OrderedLists is { Count: > 0 } childLists && childLists.Any(l => l.Items.Count > 0)))
            return lines;

        var segments = string.IsNullOrEmpty(node.Content)
            ? Array.Empty<string>()
            : node.Content.Split("<|txt_split|>");

        // Map segment index (1-based) -> (list, position within the list).
        // The API reports segment_range as 0-based indices into the split
        // content, so each index is shifted by one to match the 1-based
        // segment loop below.
        var segmentMap = new Dictionary<int, (OrderedListEntry list, int pos)>();
        foreach (var list in lists)
        {
            if (list.SegmentRange is not { Length: >= 2 } range)
                continue;
            for (int i = range[0]; i <= range[1] && i - range[0] < list.Items.Count; i++)
            {
                segmentMap[i + 1] = (list, i - range[0]);
            }
        }

        if (segments.Length == 0)
        {
            // No content to interleave with: render the lists' items directly.
            foreach (var list in lists)
            {
                for (int i = 0; i < list.Items.Count; i++)
                {
                    lines.Add(new ContentDisplayLine
                    {
                        Number = list.Items[i].Num.ToString(),
                        Text = StripHtmlTags(list.Items[i].Text),
                        IsListStart = lines.Count > 0 && !lines[^1].IsListItem
                    });
                }
            }
            return lines;
        }

        for (int i = 1; i <= segments.Length; i++)
        {
            if (segmentMap.TryGetValue(i, out var entry))
            {
                var (list, pos) = entry;
                lines.Add(new ContentDisplayLine
                {
                    Number = list.Items[pos].Num.ToString(),
                    Text = StripHtmlTags(list.Items[pos].Text),
                    // Separator only when the previous line is not a list item
                    // (i.e., this item starts a new list after a paragraph).
                    IsListStart = lines.Count > 0 && !lines[^1].IsListItem
                });
            }
            else
            {
                var segment = StripHtmlTags(segments[i - 1].Trim());
                if (segment.Length > 0)
                    lines.Add(new ContentDisplayLine { Text = segment });
            }
        }
        return lines;
    }

    /// <summary>
    /// Generates markdown for a node's content with its ordered lists rendered
    /// as markdown numbered lists. Returns an empty string when the node has no
    /// ordered lists (callers should fall back to the plain content).
    /// </summary>
    internal static string GenerateOrderedListMarkdown(AnalysisTreeNode node)
    {
        var lines = BuildContentLines(node);
        if (lines.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            if (line.IsListItem)
            {
                sb.AppendLine($"{line.Number}. {line.Text}");
            }
            else
            {
                if (sb.Length > 0)
                    sb.AppendLine();
                sb.AppendLine(line.Text);
            }
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Parses a table cell value object: { "raw": "...", "value": ..., "unit": "..." }.
    /// The value is polymorphic (string, number, or null); Raw is used for display.
    /// </summary>
    private static TableCellValue ParseTableCellValue(JsonElement elem)
    {
        var cell = new TableCellValue();

        if (elem.ValueKind != JsonValueKind.Object)
        {
            // Fallback: treat non-object values as plain raw strings
            cell.Raw = elem.ValueKind == JsonValueKind.String
                ? elem.GetString() ?? string.Empty
                : elem.GetRawText();
            cell.Value = elem;
            cell.IsNumeric = elem.ValueKind == JsonValueKind.Number;
            return cell;
        }

        if (elem.TryGetProperty("raw", out var rawElem))
            cell.Raw = rawElem.GetString() ?? string.Empty;
        if (elem.TryGetProperty("value", out var valueElem))
        {
            cell.Value = valueElem;
            // Capture the type now, while the source JsonDocument is guaranteed
            // to be alive — render-time code must not rely on Value being valid.
            cell.IsNumeric = valueElem.ValueKind == JsonValueKind.Number;
        }
        if (elem.TryGetProperty("unit", out var unitElem))
            cell.Unit = unitElem.GetString();

        return cell;
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
    /// Tries to parse a single JSON file as a Popo-serialized StructureDocument.
    /// Handles two formats:
    ///   1. Direct StructureDocument JSON (e.g., popo.json)
    ///   2. PopoTaskResultResponse wrapper (e.g., popo_result.json)
    ///      which contains task_id/status/result.tree.
    /// Callers should route MinerU *_middle.json files to TryParseMinerUMiddleJson directly.
    ///
    /// Performance note: uses JsonDocument.Parse to inspect the root structure first,
    /// avoiding double deserialization. If the root has a "result" property, it's the
    /// PopoTaskResultResponse wrapper format; otherwise, it's a direct StructureDocument.
    /// </summary>
    public static StructureDocument? TryParsePopoResultJson(string jsonPath)
    {
        if (!File.Exists(jsonPath))
            return null;

        try
        {
            var json = File.ReadAllText(jsonPath);

            // Inspect root structure first to determine format — avoids double deserialization.
            // IMPORTANT: do NOT dispose this document (no `using`). ParseTreeNode stores
            // JsonElement references (e.g. TableCellValue.Value) that point into it; the
            // elements keep the document alive for as long as the parsed tree is reachable,
            // mirroring LoadTreeJson. Disposing here would make those elements throw
            // ObjectDisposedException on later access.
            var rootDoc = JsonDocument.Parse(json);
            var root = rootDoc.RootElement;
            bool isWrapperFormat = root.TryGetProperty("result", out _);

            if (isWrapperFormat)
            {
                // PopoTaskResultResponse wrapper format (popo_result.json):
                //   { task_id, status, result: { doc_id, status, message, tree, toc, metadata } }
                // Newer versions include dedicated toc.entries and metadata dict at the result level.
                if (root.TryGetProperty("result", out var resultElem) &&
                    resultElem.TryGetProperty("tree", out var treeElem))
                {
                    var treeRoot = ParseTreeNode(treeElem);
                    if (treeRoot is not null)
                    {
                        var docId = resultElem.TryGetProperty("doc_id", out var docIdElem)
                            ? docIdElem.GetString() ?? string.Empty
                            : string.Empty;
                        var doc = BuildStructureDocumentFromTree(treeRoot, docId, DefaultModelName);

                        // Parse result.metadata (book copyright info) — newer format has it as a dict
                        if (resultElem.TryGetProperty("metadata", out var metaElem) &&
                            metaElem.ValueKind == JsonValueKind.Object)
                        {
                            doc.BookMetadataJson = metaElem.GetRawText();
                            // Also set on the tree root for backward compatibility
                            treeRoot.Metadata = metaElem.GetRawText();
                        }

                        // Parse result.toc.entries (structured table of contents) — newer format
                        // Guard: toc may be null (JSON null) when the field exists but has no value
                        if (resultElem.TryGetProperty("toc", out var tocElem) &&
                            tocElem.ValueKind == JsonValueKind.Object &&
                            tocElem.TryGetProperty("entries", out var entriesElem) &&
                            entriesElem.ValueKind == JsonValueKind.Array)
                        {
                            var tocEntries = new List<TocEntry>();
                            foreach (var entry in entriesElem.EnumerateArray())
                            {
                                // Guard: skip non-object entries (e.g., plain strings) in toc array
                                if (entry.ValueKind != JsonValueKind.Object)
                                    continue;

                                // Prefer linked_body_title (actual body title) over TOC page title
                                string title = string.Empty;
                                if (entry.TryGetProperty("linked_body_title", out var linkedTitleElem) &&
                                    !string.IsNullOrEmpty(linkedTitleElem.GetString()))
                                {
                                    title = linkedTitleElem.GetString()!;
                                }
                                else if (entry.TryGetProperty("title", out var titleElem))
                                {
                                    title = titleElem.GetString() ?? string.Empty;
                                }

                                if (!string.IsNullOrEmpty(title))
                                {
                                    int level = 1;
                                    if (entry.TryGetProperty("level", out var lvlElem) && lvlElem.ValueKind == JsonValueKind.Number)
                                        level = lvlElem.GetInt32();

                                    int page = 0;
                                    if (entry.TryGetProperty("linked_body_page", out var pageElem) && pageElem.ValueKind == JsonValueKind.Number)
                                    {
                                        page = pageElem.GetInt32();
                                    }
                                    else if (entry.TryGetProperty("page", out var fallbackPageElem) && fallbackPageElem.ValueKind == JsonValueKind.Number)
                                    {
                                        page = fallbackPageElem.GetInt32();
                                    }

                                    tocEntries.Add(new TocEntry
                                    {
                                        Title = title,
                                        Level = level,
                                        Page = page,
                                        Type = "toc"
                                    });
                                }
                            }
                            doc.TocEntries = tocEntries;
                            System.Diagnostics.Debug.WriteLine($"[Popo] result.toc.entries parsed: {tocEntries.Count} entries from {Path.GetFileName(jsonPath)}");
                        }
                        else
                        {
                            // Log toc availability for debugging — helps track whether API returns toc
                            string tocStatus = "missing";
                            if (resultElem.TryGetProperty("toc", out var tocCheck))
                            {
                                if (tocCheck.ValueKind == JsonValueKind.Null)
                                    tocStatus = "present but null";
                                else if (tocCheck.ValueKind == JsonValueKind.Object)
                                {
                                    if (!tocCheck.TryGetProperty("entries", out _))
                                        tocStatus = "present but no entries field";
                                    else
                                        tocStatus = "present but entries is not an array";
                                }
                                else
                                    tocStatus = $"present but unexpected type: {tocCheck.ValueKind}";
                            }
                            System.Diagnostics.Debug.WriteLine($"[Popo] result.toc: {tocStatus} in {Path.GetFileName(jsonPath)} (no TOC entries available)");
                        }

                        // Parse result.reference (bibliographic references) — newer format
                        // Guard: reference may be null (JSON null) when the field exists but has no value
                        if (resultElem.TryGetProperty("reference", out var refElem) &&
                            refElem.ValueKind == JsonValueKind.Object &&
                            refElem.TryGetProperty("entries", out var refEntriesElem) &&
                            refEntriesElem.ValueKind == JsonValueKind.Array)
                        {
                            var referenceEntries = new List<ReferenceEntry>();
                            foreach (var entry in refEntriesElem.EnumerateArray())
                            {
                                if (entry.ValueKind != JsonValueKind.Object)
                                    continue;

                                var refEntry = new ReferenceEntry
                                {
                                    Id = TryGetNullableInt32(entry, "id") ?? 0,
                                    RawText = entry.TryGetProperty("raw_text", out var rawTextElem) ? rawTextElem.GetString() ?? string.Empty : string.Empty,
                                    Authors = TryGetNullableString(entry, "authors"),
                                    Title = TryGetNullableString(entry, "title"),
                                    RefType = TryGetNullableString(entry, "ref_type"),
                                    JournalOrPublisher = TryGetNullableString(entry, "journal_or_publisher"),
                                    Year = TryGetNullableString(entry, "year"),
                                    Volume = TryGetNullableString(entry, "volume"),
                                    Issue = TryGetNullableString(entry, "issue"),
                                    Pages = TryGetNullableString(entry, "pages"),
                                    Doi = TryGetNullableString(entry, "doi"),
                                    Url = TryGetNullableString(entry, "url"),
                                    Page = TryGetNullableInt32(entry, "page") ?? 0,
                                };

                                // Parse block_ids array
                                if (entry.TryGetProperty("block_ids", out var blockIdsElem) &&
                                    blockIdsElem.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var blockId in blockIdsElem.EnumerateArray())
                                    {
                                        if (blockId.ValueKind == JsonValueKind.Number)
                                            refEntry.BlockIds.Add(blockId.GetInt32());
                                    }
                                }

                                referenceEntries.Add(refEntry);
                            }
                            doc.ReferenceEntries = referenceEntries;
                        }

                        // Extract formulas from the document's blocks
                        // Formulas are extracted from MinerU blocks of type "equation"
                        // and from text blocks containing display formulas
                        var formulaEntries = FormulaExtractor.ExtractFromDocument(doc, doc.PagesBlocks);
                        if (formulaEntries.Count > 0)
                        {
                            doc.FormulaEntries = formulaEntries;
                            System.Diagnostics.Debug.WriteLine($"[Popo] Extracted {formulaEntries.Count} formulas from document");
                        }

                        // Parse _artifactsDir (cached image directory path) — injected by PopoService
                        if (root.TryGetProperty("_artifactsDir", out var artifactsDirElem) &&
                            artifactsDirElem.ValueKind == JsonValueKind.String)
                        {
                            doc.ArtifactsDir = artifactsDirElem.GetString();
                            System.Diagnostics.Debug.WriteLine($"[Popo] _artifactsDir loaded: {doc.ArtifactsDir}");
                        }

                        return doc;
                    }
                }
            }
            else
            {
                // Direct StructureDocument format (popo.json):
                //   { docId, modelName, pagesBlocks, treeRoot, ... }
                var minerUDoc = JsonSerializer.Deserialize<StructureDocument>(json, StructureDocumentOptions);
                if (minerUDoc is not null && (minerUDoc.GetAllBlocks().Count > 0 || minerUDoc.TreeRoot is not null))
                {
                    // Extract formulas from the document's blocks
                    if (minerUDoc.PagesBlocks != null && minerUDoc.PagesBlocks.Count > 0)
                    {
                        var formulaEntries = FormulaExtractor.ExtractFromDocument(minerUDoc, minerUDoc.PagesBlocks);
                        if (formulaEntries.Count > 0)
                        {
                            minerUDoc.FormulaEntries = formulaEntries;
                            System.Diagnostics.Debug.WriteLine($"[Popo] Extracted {formulaEntries.Count} formulas from document (direct format)");
                        }
                    }
                    return minerUDoc;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PopoJsonService] TryParsePopoResultJson failed for '{jsonPath}': {ex.GetType().Name}: {ex.Message}");
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
        // Skip structured table sub-nodes (table_data / table_key_value): they are
        // subdivisions of the parent table node, which already carries the full
        // HTML content and the same block_ids. Flattening them would create
        // duplicate empty-content blocks in PagesBlocks and the aggregation map.
        if (node.Type is "table_data" or "table_key_value")
            return;

        // Process non-root nodes that have location data
        bool isRoot = node.Type == "root" && node.Level == 0;

        if (!isRoot && node.Location.Count > 0)
        {
            // Create one block per location entry, preserving original block_ids for cross-reference
            var originalIds = new List<int>(node.BlockIds);
            var sourceBlockIds = new List<string>(node.SourceBlockIds);

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
                    OriginalBlockIds = originalIds,
                    SourceBlockIds = sourceBlockIds
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

        // Find middle.json - search in artifactsDir and parent directories
        // artifactsDir may be the images subdirectory, but middle.json is in hybrid_auto (parent)
        var searchDir = artifactsDir;
        var middleJsons = Directory.GetFiles(searchDir, "*_middle.json", SearchOption.AllDirectories);

        // If not found in artifactsDir, try parent directory (hybrid_auto)
        if (middleJsons.Length == 0 && Directory.Exists(searchDir))
        {
            var parentDir = Directory.GetParent(searchDir)?.FullName;
            if (parentDir is not null)
            {
                middleJsons = Directory.GetFiles(parentDir, "*_middle.json", SearchOption.AllDirectories);
            }
        }

        // If still not found, try grandparent directory
        if (middleJsons.Length == 0)
        {
            var parentDir = Directory.GetParent(artifactsDir)?.Parent?.FullName;
            if (parentDir is not null)
            {
                middleJsons = Directory.GetFiles(parentDir, "*_middle.json", SearchOption.AllDirectories);
            }
        }

        if (middleJsons.Length == 0)
        {
            System.Diagnostics.Debug.WriteLine($"[PopoJson] WARNING: No *_middle.json found near {artifactsDir}");
            return;
        }

        System.Diagnostics.Debug.WriteLine($"[PopoJson] Found middle.json: {middleJsons[0]}");

        var middleJson = middleJsons[0];
        System.Diagnostics.Debug.WriteLine($"[PopoJson] Found middle.json: {middleJson}");
        try
        {
            var json = File.ReadAllText(middleJson);
            using var docJson = System.Text.Json.JsonDocument.Parse(json);
            PopulateTreeBlockIdsRecursive(doc.TreeRoot, docJson.RootElement);

            // Now populate ImgPath for image nodes that have empty img_path
            PopulateTreeImgPaths(doc.TreeRoot, artifactsDir);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PopoJson] ERROR parsing middle.json: {ex.Message}");
        }
    }

    /// <summary>
    /// Populates ImgPath for image nodes that have empty img_path from the Popo API.
    /// Reads from content_list.json which contains the actual img_path mappings.
    /// Optimized: reads content_list.json only ONCE and caches image files for fallback matching.
    /// </summary>
    public static void PopulateTreeImgPaths(AnalysisTreeNode node, string artifactsDir)
    {
        // Read content_list.json ONCE at the top level
        var contentListPath = FindContentListJson(artifactsDir);
        System.Text.Json.JsonDocument? parsedContentList = null;
        if (contentListPath is not null)
        {
            try
            {
                var json = File.ReadAllText(contentListPath);
                parsedContentList = System.Text.Json.JsonDocument.Parse(json);
                System.Diagnostics.Debug.WriteLine($"[PopoJson] Loaded content_list.json: {contentListPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PopoJson] ERROR reading content_list.json: {ex.Message}");
            }
        }

        // Cache image files ONCE for fallback matching
        var cachedImageFiles = GetImageFiles(artifactsDir);
        System.Diagnostics.Debug.WriteLine($"[PopoJson] Cached {cachedImageFiles.Count} image files for fallback matching");

        PopulateTreeImgPathsRecursive(node, artifactsDir, parsedContentList, cachedImageFiles);
    }

    /// <summary>
    /// Internal recursive implementation that uses pre-parsed content_list.json and cached image files.
    /// </summary>
    private static void PopulateTreeImgPathsRecursive(
        AnalysisTreeNode node,
        string artifactsDir,
        System.Text.Json.JsonDocument? parsedContentList,
        List<string> cachedImageFiles)
    {
        if (node.Type == "image" && string.IsNullOrEmpty(node.ImgPath))
        {
            // Strategy 1: Try to find matching image from pre-parsed content_list.json
            if (parsedContentList is not null &&
                parsedContentList.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var elem in parsedContentList.RootElement.EnumerateArray())
                {
                    if (!elem.TryGetProperty("type", out var type) || type.GetString() != "image")
                        continue;
                    if (!elem.TryGetProperty("img_path", out var imgPath) || string.IsNullOrEmpty(imgPath.GetString()))
                        continue;
                    if (!elem.TryGetProperty("bbox", out var bbox))
                        continue;
                    if (!elem.TryGetProperty("page_idx", out var pageIdx))
                        continue;

                    // Match by bbox and page
                    if (MatchNodeToBlock(node, bbox, pageIdx.GetInt32()))
                    {
                        node.ImgPath = imgPath.GetString()!;
                        System.Diagnostics.Debug.WriteLine($"[PopoJson] Populated ImgPath from content_list.json: {node.ImgPath}");
                        break;
                    }
                }
            }

            // Strategy 2: Fallback - try to match by block_id in image filename (using cached files)
            if (string.IsNullOrEmpty(node.ImgPath) && node.BlockIds.Count > 0)
            {
                foreach (var blockId in node.BlockIds)
                {
                    var matchedFile = cachedImageFiles.FirstOrDefault(f =>
                        Path.GetFileName(f).Contains(blockId.ToString()));
                    if (matchedFile is not null)
                    {
                        node.ImgPath = "images/" + Path.GetFileName(matchedFile);
                        System.Diagnostics.Debug.WriteLine($"[PopoJson] Populated ImgPath for block {blockId}: {node.ImgPath}");
                        break;
                    }
                }
            }
        }

        foreach (var child in node.Children)
        {
            PopulateTreeImgPathsRecursive(child, artifactsDir, parsedContentList, cachedImageFiles);
        }
    }

    /// <summary>
    /// Finds content_list.json in the artifacts directory or parent directories.
    /// </summary>
    private static string? FindContentListJson(string artifactsDir)
    {
        // Search in artifactsDir and parent directories
        var searchDir = artifactsDir;
        var files = Directory.GetFiles(searchDir, "*_content_list*.json", SearchOption.AllDirectories);
        if (files.Length > 0) return files[0];

        var parentDir = Directory.GetParent(searchDir)?.FullName;
        if (parentDir is not null)
        {
            files = Directory.GetFiles(parentDir, "*_content_list*.json", SearchOption.AllDirectories);
            if (files.Length > 0) return files[0];
        }

        var grandparentDir = Directory.GetParent(artifactsDir)?.Parent?.FullName;
        if (grandparentDir is not null)
        {
            files = Directory.GetFiles(grandparentDir, "*_content_list*.json", SearchOption.AllDirectories);
            if (files.Length > 0) return files[0];
        }

        return null;
    }

    /// <summary>
    /// Matches a tree node to a block from content_list.json by comparing bbox and page.
    /// </summary>
    private static bool MatchNodeToBlock(AnalysisTreeNode node, System.Text.Json.JsonElement bbox, int pageIdx)
    {
        if (node.Location.Count == 0) return false;

        var bboxArr = bbox.EnumerateArray().Select(v => v.GetDouble()).ToArray();
        if (bboxArr.Length < 4) return false;

        var blockPage = pageIdx + 1; // content_list uses 0-based, tree uses 1-based
        if (!node.Location.Any(l => l.Page == blockPage)) return false;

        // Check if bboxes overlap
        var blockX1 = bboxArr[0];
        var blockY1 = bboxArr[1];
        var blockX2 = bboxArr[2];
        var blockY2 = bboxArr[3];

        foreach (var loc in node.Location)
        {
            if (loc.Page != blockPage) continue;

            var treeX1 = loc.Bbox.X;
            var treeY1 = loc.Bbox.Y;
            var treeX2 = treeX1 + loc.Bbox.Width;
            var treeY2 = treeY1 + loc.Bbox.Height;

            // Check overlap
            var overlapX1 = Math.Max(blockX1, treeX1);
            var overlapY1 = Math.Max(blockY1, treeY1);
            var overlapX2 = Math.Min(blockX2, treeX2);
            var overlapY2 = Math.Min(blockY2, treeY2);

            if (overlapX1 < overlapX2 && overlapY1 < overlapY2)
                return true;
        }

        return false;
    }

    private static List<string> GetImageFiles(string dir)
    {
        try
        {
            return Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories)
                .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg")
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
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

                // Also match from preproc_blocks (they have block_id UUIDs)
                if (page.TryGetProperty("preproc_blocks", out var preprocBlocks))
                {
                    MatchTreeNodesToBlocksById(node, preprocBlocks, pageIdx, pageW, pageH);
                }

                // Also match from para_blocks (they have block_id UUIDs)
                if (page.TryGetProperty("para_blocks", out var paraBlocks2))
                {
                    MatchTreeNodesToBlocksById(node, paraBlocks2, pageIdx, pageW, pageH);
                }

                // Also match from discarded_blocks (they have block_id UUIDs)
                if (page.TryGetProperty("discarded_blocks", out var discardedBlocks))
                {
                    MatchTreeNodesToBlocksById(node, discardedBlocks, pageIdx, pageW, pageH);
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

                    // For table nodes with placeholder content, extract actual content from middle.json block
                    if (treeNode.Type == "table" && IsTablePlaceholder(treeNode.Content))
                    {
                        var tableContent = ExtractTableContentFromBlock(block);
                        if (!string.IsNullOrEmpty(tableContent))
                        {
                            System.Diagnostics.Debug.WriteLine(
                                $"[Popo] Extracted table content for node on page {pageIdx + 1}: {tableContent.Length} chars");
                            treeNode.Content = tableContent;
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine(
                                $"[Popo] No table content found in middle.json block for node on page {pageIdx + 1}");
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

    /// <summary>
    /// Matches tree nodes to blocks (preproc_blocks, para_blocks, or discarded_blocks) from middle.json
    /// to populate SourceBlockIds (UUID strings). This is needed because the Popo API may not return
    /// source_block_ids in popo_result.json, or the source_block_ids may not match the blocks shown
    /// in the MinerU column.
    /// </summary>
    private static void MatchTreeNodesToBlocksById(
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
                    // Found overlap - get the block_id (UUID string)
                    if (block.TryGetProperty("block_id", out var blockId))
                    {
                        var blockIdStr = blockId.GetString();
                        if (!string.IsNullOrEmpty(blockIdStr) && !treeNode.SourceBlockIds.Contains(blockIdStr))
                        {
                            treeNode.SourceBlockIds.Add(blockIdStr);
                        }
                    }
                }
            }
        }

        // Recurse into children
        foreach (var child in treeNode.Children)
        {
            MatchTreeNodesToBlocksById(child, blocks, pageIdx, pageW, pageH);
        }
    }

    #endregion

    #region Table of Contents

    /// <summary>
    /// Builds a flat table of contents list from the Popo analysis result.
    /// Only uses pre-parsed TocEntries from StructureDocument (popo_result.json result.toc.entries).
    /// No local fallback - if the API did not return toc entries, returns an empty list.
    /// </summary>
    /// <param name="root">The root node of the document tree (unused, kept for backward compatibility).</param>
    /// <param name="doc">StructureDocument with pre-parsed TOC entries from the API.</param>
    /// <returns>A list of TOC entries from the API, or an empty list if the API did not return any.</returns>
    public static List<TocEntry> BuildTableOfContents(AnalysisTreeNode root, StructureDocument? doc = null)
    {
        // Use pre-parsed TocEntries from the API (popo_result.json result.toc.entries)
        // plus node-level toc entries (in-table sections from newer Popo output).
        // No local fallback to tree traversal.
        var entries = new List<TocEntry>();
        if (doc?.TocEntries is { Count: > 0 })
            entries.AddRange(doc.TocEntries);

        CollectNodeTocEntries(root, entries);

        return entries;
    }

    /// <summary>
    /// Recursively collects node-level toc entries (in-table sections carried by
    /// table nodes in newer Popo output) into the given list.
    /// </summary>
    private static void CollectNodeTocEntries(AnalysisTreeNode node, List<TocEntry> entries)
    {
        if (node.Toc is { Count: > 0 })
            entries.AddRange(node.Toc);

        foreach (var child in node.Children)
        {
            CollectNodeTocEntries(child, entries);
        }
    }


    /// <summary>
    /// Recursively searches the tree for a text node with title "目录" (Table of Contents).
    /// </summary>
    private static AnalysisTreeNode? FindTocNode(AnalysisTreeNode node)
    {
        if (node.Type == "text" && node.Title == "目录")
            return node;

        foreach (var child in node.Children)
        {
            var found = FindTocNode(child);
            if (found is not null)
                return found;
        }
        return null;
    }

    /// <summary>
    /// Finds the minimum level among text nodes in a subtree (excluding the root node itself).
    /// </summary>
    private static void FindMinTocLevel(AnalysisTreeNode node, ref int minLevel)
    {
        if (node.Type == "text" && !string.IsNullOrEmpty(node.Title) && node.Level < minLevel)
            minLevel = node.Level;

        foreach (var child in node.Children)
        {
            FindMinTocLevel(child, ref minLevel);
        }
    }

    /// <summary>
    /// Recursively extracts TOC entries from a subtree, applying a level offset
    /// to normalize the hierarchy (makes the first level become 1).
    /// </summary>
    private static void BuildTocFromSubtree(AnalysisTreeNode node, List<TocEntry> entries, int levelOffset)
    {
        if (node.Type == "text" && !string.IsNullOrEmpty(node.Title))
        {
            if (node.Location.Count > 0)
            {
                entries.Add(new TocEntry
                {
                    Title = node.Title.Trim(),
                    Level = node.Level + levelOffset,
                    Page = node.Location[0].Page,
                    Type = node.Type
                });
            }
        }

        foreach (var child in node.Children)
        {
            BuildTocFromSubtree(child, entries, levelOffset);
        }
    }

    /// <summary>
    /// Recursively traverses the tree to extract TOC entries (legacy fallback).
    /// Skips root, page_number, header, footer, image-only nodes without meaningful titles.
    /// </summary>
    private static void BuildTocRecursive(AnalysisTreeNode node, List<TocEntry> entries)
    {
        bool isRoot = node.Type == "root" && node.Level == 0;

        if (!isRoot)
        {
            var type = node.Type;
            var level = node.Level;
            var title = node.Title;
            var content = node.Content;

            // Only extract text/title nodes with meaningful heading content
            if (type is "text" or "title" && level > 0 && (!string.IsNullOrEmpty(title) || !string.IsNullOrEmpty(content)))
            {
                // Get first page from location — skip entries with no valid page number
                if (node.Location.Count == 0)
                {
                    // No location data available, skip this entry but continue traversing children
                }
                else
                {
                    int page = node.Location[0].Page;

                    // Use title if available, otherwise fall back to first segment of content
                    var displayTitle = !string.IsNullOrEmpty(title)
                        ? title
                        : content.Split(new[] { "<|txt_split|>" }, StringSplitOptions.None)[0];

                    entries.Add(new TocEntry
                    {
                        Title = displayTitle,
                        Level = level,
                        Page = page,
                        Type = type
                    });
                }
            }
        }

        foreach (var child in node.Children)
        {
            BuildTocRecursive(child, entries);
        }
    }

    /// <summary>
    /// Saves the table of contents to both JSON and Markdown files in the specified output directory.
    /// Creates the directory if it doesn't exist.
    /// </summary>
    /// <param name="doc">The structure document containing the tree root.</param>
    /// <param name="outputDir">Directory where toc.json and toc.md will be written.</param>
    /// <returns>The list of TOC entries that were saved.</returns>
    public static async Task<List<TocEntry>> SaveTocFilesAsync(StructureDocument doc, string outputDir)
    {
        if (doc.TreeRoot is null)
            return new List<TocEntry>();

        Directory.CreateDirectory(outputDir);

        var entries = BuildTableOfContents(doc.TreeRoot, doc);

        // Save toc.json
        var jsonPath = Path.Combine(outputDir, "toc.json");
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            TypeInfoResolver = SourceGenerationContext.Default
        };
        var json = JsonSerializer.Serialize(entries, jsonOptions);
        await File.WriteAllTextAsync(jsonPath, json);

        // Save toc.md
        var mdPath = Path.Combine(outputDir, "toc.md");
        var md = GenerateTocMarkdown(entries);
        await File.WriteAllTextAsync(mdPath, md);

        return entries;
    }

    /// <summary>
    /// Estimates the visual width of a string in a monospace font, accounting for
    /// CJK (Chinese/Japanese/Korean) characters which typically render at 2x width.
    /// </summary>
    private static int GetVisualWidth(string text)
    {
        int width = 0;
        foreach (char c in text)
        {
            // CJK Unified Ideographs, CJK Radicals, Bopomofo, Hangul, etc.
            // These ranges cover the most common East Asian character blocks
            if ((c >= 0x2E80 && c <= 0x9FFF) ||   // CJK Radicals Supplement, CJK Ideographs
                (c >= 0xF900 && c <= 0xFAFF) ||   // CJK Compatibility Ideographs
                (c >= 0xFF00 && c <= 0xFFEF) ||   // Fullwidth ASCII variants
                (c >= 0x3000 && c <= 0x30FF) ||   // CJK Symbols and Punctuation, Hiragana, Katakana
                (c >= 0xAC00 && c <= 0xD7AF))     // Hangul Syllables
            {
                width += 2;
            }
            else
            {
                width += 1;
            }
        }
        return width;
    }

    /// <summary>
    /// Generates Markdown for a table node:
    /// table_data → pipe table (headers + rows),
    /// table_key_value → "- key: value" list,
    /// legacy table with parsed HTML grid → pipe table (first row as header).
    /// Returns an empty string for nodes without table data.
    /// </summary>
    internal static string GenerateTableMarkdown(AnalysisTreeNode node)
    {
        var sb = new StringBuilder();

        switch (node.Type)
        {
            case "table_data" when node.Headers is { Count: > 0 } headers && node.Rows is not null:
                sb.AppendLine("| " + string.Join(" | ", headers.Select(h => EscapeMarkdownCell(h.Name))) + " |");
                sb.AppendLine("| " + string.Join(" | ", headers.Select(_ => "---")) + " |");
                foreach (var row in node.Rows)
                {
                    var cells = headers.Select(h => EscapeMarkdownCell(row.Cells.TryGetValue(h.Name, out var cell) ? cell.Raw : string.Empty));
                    sb.AppendLine("| " + string.Join(" | ", cells) + " |");
                }
                break;

            case "table_key_value" when node.Pairs is not null:
                foreach (var pair in node.Pairs)
                {
                    var display = string.IsNullOrEmpty(pair.Value.Unit)
                        ? pair.Value.Raw
                        : $"{pair.Value.Raw} {pair.Value.Unit}";
                    sb.AppendLine($"- {pair.Key}: {display}");
                }
                break;

            case "table" when node.GridRows is { Count: > 0 } grid:
                AppendMarkdownGrid(grid, sb);
                break;
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Appends a 2D grid as a Markdown pipe table. The first row is used as the
    /// header row (Markdown tables require one); ragged rows are padded with empties.
    /// </summary>
    private static void AppendMarkdownGrid(List<List<string>> grid, StringBuilder sb)
    {
        var colCount = grid.Max(row => row.Count);
        if (colCount == 0)
            return;

        var header = grid[0];
        sb.AppendLine("| " + string.Join(" | ",
            Enumerable.Range(0, colCount).Select(i => EscapeMarkdownCell(i < header.Count ? header[i] : string.Empty))) + " |");
        sb.AppendLine("| " + string.Join(" | ", Enumerable.Repeat("---", colCount)) + " |");

        for (int r = 1; r < grid.Count; r++)
        {
            var row = grid[r];
            sb.AppendLine("| " + string.Join(" | ",
                Enumerable.Range(0, colCount).Select(i => EscapeMarkdownCell(i < row.Count ? row[i] : string.Empty))) + " |");
        }
    }

    /// <summary>
    /// Escapes pipe characters so cell content doesn't break the markdown table layout.
    /// </summary>
    internal static string EscapeMarkdownCell(string text) => text.Replace("|", "\\|");

    /// <summary>
    /// Generates a formatted Markdown table of contents with hierarchical indentation
    /// and dotted page number alignment. Uses visual-width-aware padding for proper
    /// alignment with CJK (Chinese) characters.
    /// </summary>
    internal static string GenerateTocMarkdown(List<TocEntry> entries)
    {
        if (entries.Count == 0)
            return "# 目录\n\n(空)\n";

        var sb = new StringBuilder();
        sb.AppendLine("# 目录");
        sb.AppendLine();

        // Target visual width for dot leaders (accounts for CJK double-width characters)
        const int targetVisualWidth = 50;

        foreach (var entry in entries)
        {
            var indent = new string(' ', (entry.Level - 1) * 2);
            var title = entry.Title.Trim();
            var page = entry.Page;

            // Calculate visual width of indent + title (CJK chars count as 2)
            var visualWidth = GetVisualWidth(indent) + GetVisualWidth(title);
            var dotCount = Math.Max(1, targetVisualWidth - visualWidth);
            var dots = new string('.', dotCount);

            sb.AppendLine($"{indent}{title} {dots} {page}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Generates a formatted Markdown reference list with CJK visual-width-aware alignment.
    /// Prefers the raw_text field for display; falls back to structured fields.
    /// </summary>
    internal static string GenerateReferenceMarkdown(List<ReferenceEntry> entries)
    {
        if (entries.Count == 0)
            return "# 参考文献\n\n(空)\n";

        var sb = new StringBuilder();
        sb.AppendLine("# 参考文献");
        sb.AppendLine($"共 {entries.Count} 条");
        sb.AppendLine();

        foreach (var entry in entries)
        {
            // Prefer raw_text (already formatted by the Popo API)
            if (!string.IsNullOrEmpty(entry.RawText))
            {
                sb.AppendLine(entry.RawText);
                sb.AppendLine();
            }
            else
            {
                // Fallback: construct citation from structured fields
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(entry.Authors))
                    parts.Add(entry.Authors);
                if (!string.IsNullOrEmpty(entry.Title))
                {
                    var titlePart = entry.Title;
                    if (!string.IsNullOrEmpty(entry.RefType))
                        titlePart += $" [{entry.RefType}]";
                    parts.Add(titlePart);
                }
                if (!string.IsNullOrEmpty(entry.JournalOrPublisher))
                    parts.Add(entry.JournalOrPublisher);
                if (!string.IsNullOrEmpty(entry.Year))
                    parts.Add(entry.Year);
                if (!string.IsNullOrEmpty(entry.Pages))
                    parts.Add($"pp.{entry.Pages}");

                var citation = $"[{entry.Id}] {string.Join(". ", parts)}.";
                if (entry.Page > 0)
                    citation += $"  (p.{entry.Page})";

                sb.AppendLine(citation);
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Saves the reference list to both JSON and Markdown files in the specified output directory.
    /// Creates the directory if it doesn't exist.
    /// </summary>
    /// <param name="doc">The structure document containing reference entries.</param>
    /// <param name="outputDir">Directory where reference.json and reference.md will be written.</param>
    /// <returns>The list of reference entries that were saved.</returns>
    public static async Task<List<ReferenceEntry>> SaveReferenceFilesAsync(StructureDocument doc, string outputDir)
    {
        var entries = doc.ReferenceEntries ?? new List<ReferenceEntry>();
        if (entries.Count == 0)
            return entries;

        Directory.CreateDirectory(outputDir);

        // Save reference.json
        var jsonPath = Path.Combine(outputDir, "reference.json");
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            TypeInfoResolver = SourceGenerationContext.Default
        };
        var json = JsonSerializer.Serialize(entries, jsonOptions);
        await File.WriteAllTextAsync(jsonPath, json);

        // Save reference.md
        var mdPath = Path.Combine(outputDir, "reference.md");
        var md = GenerateReferenceMarkdown(entries);
        await File.WriteAllTextAsync(mdPath, md);

        return entries;
    }

    /// <summary>
    /// Helper to safely get a nullable string from a JSON property.
    /// Returns null if the property is missing, null, or empty.
    /// </summary>
    private static string? TryGetNullableString(System.Text.Json.JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) &&
            prop.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            var value = prop.GetString();
            return string.IsNullOrEmpty(value) ? null : value;
        }
        return null;
    }

    /// <summary>
    /// Helper to safely get a nullable int from a JSON property.
    /// Returns null if the property is missing, null, or not a number.
    /// </summary>
    private static int? TryGetNullableInt32(System.Text.Json.JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) &&
            prop.ValueKind == System.Text.Json.JsonValueKind.Number)
        {
            return prop.GetInt32();
        }
        return null;
    }

    /// <summary>
    /// Persists resolved image paths back to popo_result.json.
    /// After PopulateTreeImgPaths resolves image paths in-memory, this method
    /// updates the img_path field for each image node in the JSON file so that
    /// subsequent loads can skip the expensive fallback matching logic.
    /// </summary>
    public static void UpdatePopoResultImgPaths(StructureDocument doc, string popoDir)
    {
        if (doc.TreeRoot is null)
            return;

        var resultPath = Path.Combine(popoDir, "popo_result.json");
        if (!File.Exists(resultPath))
            return;

        try
        {
            var content = File.ReadAllText(resultPath);
            using var jsonDoc = JsonDocument.Parse(content);
            var root = jsonDoc.RootElement;

            System.Text.Json.JsonElement? treeElement = null;
            if (root.TryGetProperty("result", out var resultElem) &&
                resultElem.TryGetProperty("tree", out var treeElem))
            {
                treeElement = treeElem;
            }
            else if (root.TryGetProperty("tree", out var directTree))
            {
                treeElement = directTree;
            }

            if (treeElement is null)
            {
                System.Diagnostics.Debug.WriteLine("[Popo] No tree found in popo_result.json for img_path update");
                return;
            }

            var ms = new MemoryStream();
            using var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true });
            CopyJsonWithUpdatedImgPaths(writer, root, doc.TreeRoot);
            writer.Flush();
            var updatedContent = System.Text.Encoding.UTF8.GetString(ms.ToArray());
            File.WriteAllText(resultPath, updatedContent);
            System.Diagnostics.Debug.WriteLine("[Popo] Updated popo_result.json with resolved image paths");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Popo] Failed to update popo_result.json img_paths: {ex.Message}");
        }
    }

    /// <summary>
    /// Recursively copies a JSON element, updating img_path for matching image nodes.
    /// </summary>
    private static void CopyJsonWithUpdatedImgPaths(
        System.Text.Json.Utf8JsonWriter writer,
        System.Text.Json.JsonElement jsonNode,
        AnalysisTreeNode modelNode)
    {
        if (jsonNode.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            jsonNode.WriteTo(writer);
            return;
        }

        writer.WriteStartObject();
        bool needsImgPathUpdate = false;
        string? resolvedImgPath = null;

        if (modelNode.Type == "image" && !string.IsNullOrEmpty(modelNode.ImgPath))
        {
            if (jsonNode.TryGetProperty("type", out var jsonType) && jsonType.GetString() == "image")
            {
                string? jsonImgPath = null;
                if (jsonNode.TryGetProperty("img_path", out var jsonImgPathElem))
                    jsonImgPath = jsonImgPathElem.GetString();
                if (string.IsNullOrEmpty(jsonImgPath) || jsonImgPath != modelNode.ImgPath)
                {
                    needsImgPathUpdate = true;
                    resolvedImgPath = modelNode.ImgPath;
                }
            }
        }

        foreach (var prop in jsonNode.EnumerateObject())
        {
            if (prop.Name == "img_path" && needsImgPathUpdate)
            {
                writer.WriteString("img_path", resolvedImgPath);
                System.Diagnostics.Debug.WriteLine($"[Popo] Updated img_path for node '{modelNode.Title}': {resolvedImgPath}");
            }
            else if (prop.Name == "children" && prop.Value.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                writer.WritePropertyName("children");
                writer.WriteStartArray();
                var jsonChildren = prop.Value.EnumerateArray();
                var modelChildren = modelNode.Children;
                int jsonIndex = 0;
                foreach (var jsonChild in jsonChildren)
                {
                    if (jsonIndex < modelChildren.Count)
                        CopyJsonWithUpdatedImgPaths(writer, jsonChild, modelChildren[jsonIndex]);
                    else
                        jsonChild.WriteTo(writer);
                    jsonIndex++;
                }
                writer.WriteEndArray();
            }
            else
            {
                prop.WriteTo(writer);
            }
        }

        if (needsImgPathUpdate && !jsonNode.TryGetProperty("img_path", out _))
            writer.WriteString("img_path", resolvedImgPath);

        writer.WriteEndObject();
    }

    #region Table Content Extraction

    /// <summary>
    /// Checks if the given content is a placeholder that should be replaced with actual table data.
    /// </summary>
    static bool IsTablePlaceholder(string content)
    {
        var trimmed = content.Trim();
        return string.IsNullOrEmpty(trimmed) ||
               trimmed == "[table]" ||
               trimmed == "[table_body]" ||
               trimmed == "<table>" ||
               trimmed.StartsWith("<table") ||
               trimmed.StartsWith("<Table");
    }

    /// <summary>
    /// Extracts structured text from a table node's table_body HTML and table_caption fields.
    /// Parses table_body HTML into a readable format: "列名: 值 | 列名: 值" per row.
    /// Also includes table_caption as the title.
    /// </summary>
    static string ExtractTableContent(JsonElement elem)
    {
        var parts = new List<string>();

        // Extract table caption
        if (elem.TryGetProperty("table_caption", out var captionElem) && captionElem.ValueKind == JsonValueKind.Array)
        {
            foreach (var cap in captionElem.EnumerateArray())
            {
                if (cap.ValueKind == JsonValueKind.String)
                {
                    var text = (cap.GetString() ?? string.Empty).Trim();
                    // Remove HTML tags from caption (e.g., <sup>)
                    text = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", "");
                    if (!string.IsNullOrEmpty(text))
                    {
                        parts.Add(text);
                    }
                }
            }
        }

        // Parse table_body HTML
        if (elem.TryGetProperty("table_body", out var bodyElem) && bodyElem.ValueKind == JsonValueKind.String)
        {
            var html = bodyElem.GetString() ?? string.Empty;
            var rows = ParseHtmlTableRows(html);
            parts.AddRange(rows);
        }

        return string.Join("\n", parts);
    }

    /// <summary>
    /// Parses HTML table into structured text rows.
    /// First row is treated as headers; subsequent rows are formatted as "header: value | header: value".
    /// </summary>
    static List<string> ParseHtmlTableRows(string html)
    {
        var results = new List<string>();

        // Extract all <tr>...</tr>
        var rowRegex = new System.Text.RegularExpressions.Regex("<tr[^>]*>(.*?)</tr>", System.Text.RegularExpressions.RegexOptions.Singleline);
        var headerCells = new List<string>();
        bool isFirstRow = true;

        foreach (System.Text.RegularExpressions.Match match in rowRegex.Matches(html))
        {
            var rowHtml = match.Groups[1].Value;
            var cells = ExtractHtmlCells(rowHtml);

            if (isFirstRow)
            {
                headerCells = cells;
                isFirstRow = false;
            }
            else
            {
                // Format as "header: value | header: value"
                var pairs = new List<string>();
                int maxCols = Math.Max(headerCells.Count, cells.Count);
                for (int i = 0; i < maxCols; i++)
                {
                    var key = i < headerCells.Count ? StripHtmlTags(headerCells[i]) : string.Empty;
                    var val = i < cells.Count ? StripHtmlTags(cells[i]) : string.Empty;

                    // Skip if both key and value are empty
                    if (string.IsNullOrEmpty(key) && string.IsNullOrEmpty(val))
                        continue;

                    pairs.Add($"{key}: {val}");
                }

                if (pairs.Count > 0)
                {
                    results.Add(string.Join(" | ", pairs));
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Extracts cell content from <td> and <th> tags.
    /// </summary>
    static List<string> ExtractHtmlCells(string rowHtml)
    {
        var cells = new List<string>();
        var cellRegex = new System.Text.RegularExpressions.Regex("<t[hd][^>]*>(.*?)</t[hd]>", System.Text.RegularExpressions.RegexOptions.Singleline);

        foreach (System.Text.RegularExpressions.Match match in cellRegex.Matches(rowHtml))
        {
            cells.Add(match.Groups[1].Value);
        }

        return cells;
    }

    /// <summary>
    /// Strips HTML tags from a string.
    /// </summary>
    static string StripHtmlTags(string html)
    {
        return System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", "").Trim();
    }

    /// <summary>
    /// Extracts table content from a middle.json block element.
    /// This handles both preproc_blocks (which have table_body HTML) and para_blocks (which have table_caption).
    /// For middle.json format, table HTML is nested in blocks[0].lines[0].spans[0].html.
    /// </summary>
    static string ExtractTableContentFromBlock(JsonElement block)
    {
        var parts = new List<string>();

        // Extract table_caption (from para_blocks)
        if (block.TryGetProperty("table_caption", out var captionElem) && captionElem.ValueKind == JsonValueKind.Array)
        {
            foreach (var cap in captionElem.EnumerateArray())
            {
                if (cap.ValueKind == JsonValueKind.String)
                {
                    var text = (cap.GetString() ?? string.Empty).Trim();
                    text = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", "");
                    if (!string.IsNullOrEmpty(text))
                    {
                        parts.Add(text);
                    }
                }
            }
        }

        // Try direct table_body property (from some preproc_blocks formats)
        if (block.TryGetProperty("table_body", out var bodyElem) && bodyElem.ValueKind == JsonValueKind.String)
        {
            var html = bodyElem.GetString() ?? string.Empty;
            var rows = ParseHtmlTableRows(html);
            parts.AddRange(rows);
        }

        // Try nested structure from middle.json: blocks[0].lines[0].spans[0].html
        // This is the actual structure used by MinerU's middle.json for table blocks
        if (parts.Count == 0 && block.TryGetProperty("blocks", out var blocksElem) && blocksElem.ValueKind == JsonValueKind.Array)
        {
            foreach (var subBlock in blocksElem.EnumerateArray())
            {
                // Look for table_body sub-block
                if (subBlock.TryGetProperty("type", out var typeElem) && 
                    typeElem.ValueKind == JsonValueKind.String &&
                    typeElem.GetString() == "table_body")
                {
                    if (subBlock.TryGetProperty("lines", out var linesElem) && linesElem.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var line in linesElem.EnumerateArray())
                        {
                            if (line.TryGetProperty("spans", out var spansElem) && spansElem.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var span in spansElem.EnumerateArray())
                                {
                                    if (span.TryGetProperty("html", out var htmlElem) && htmlElem.ValueKind == JsonValueKind.String)
                                    {
                                        var html = htmlElem.GetString() ?? string.Empty;
                                        var rows = ParseHtmlTableRows(html);
                                        parts.AddRange(rows);
                                        System.Diagnostics.Debug.WriteLine(
                                            $"[Popo] Extracted table HTML from nested blocks structure: {html.Length} chars");
                                        break;
                                    }
                                }
                            }
                            if (parts.Count > 0) break;
                        }
                    }
                }
                if (parts.Count > 0) break;
            }
        }

        return string.Join("\n", parts);
    }

    #endregion

    #endregion
}
