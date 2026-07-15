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
using Caly.Core.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

namespace Caly.Core.Services;

/// <summary>
/// Service for parsing MinerU JSON output files (middle.json, ZIP results).
/// Converts MinerU's raw output into MinerUDocument structures for visualization.
/// </summary>
public static class MinerUJsonService
{
    /// <summary>
    /// Loads a MinerU parse result produced in the project's mineru/ directory.
    /// This is a separate stage from Popo post-processing and may return a
    /// MinerUDocument built from MinerU middle.json output.
    /// </summary>
    public static StructureDocument? LoadMinerUResultFromProject(string? projectPath)
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
            ? $"{docId}.zip"
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
            var specificPath = Path.Combine(minerUDir, $"{docId}.zip");
            if (File.Exists(specificPath))
                return specificPath;
        }

        // Find any .zip file in the mineru directory
        var zipFiles = Directory.GetFiles(minerUDir, "*.zip");
        return zipFiles.Length > 0 ? zipFiles[0] : null;
    }

    #region MinerU Output Parsing

    /// <summary>
    /// Parses MinerU middle.json output into a MinerUDocument.
    /// Supports both the "pages" flat block list and optional "tree" hierarchical structure.
    /// </summary>
    public static StructureDocument? TryParseMinerUMiddleJson(string jsonPath)
    {
        if (!File.Exists(jsonPath))
            return null;

        try
        {
            var json = File.ReadAllText(jsonPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var minerUDoc = new StructureDocument
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

                        var blocks = new List<MinerUBlock>();
                        var pageWidth = pageSizeMap.TryGetValue(pageNum, out var size) ? size.width : 0.0;
                        var pageHeight = pageSizeMap.TryGetValue(pageNum, out size) ? size.height : 0.0;

                        foreach (var blockElem in pageEntry.Value.EnumerateArray())
                        {
                            var block = MapMinerUBlockToMinerUBlock(blockElem, pageWidth, pageHeight);
                            blocks.Add(block);
                        }

                        minerUDoc.PagesBlocks[pageNum] = blocks;
                    }
                }
                else if (pagesElem.ValueKind == JsonValueKind.Array)
                {
                    foreach (var pageEntry in pagesElem.EnumerateArray())
                    {
                        if (!pageEntry.TryGetProperty("page", out var pageElem))
                            continue;

                        var pageNum = GetIntValue(pageElem);
                        if (pageNum < 0)
                            continue;

                        var blocks = new List<MinerUBlock>();
                        var pageWidth = pageSizeMap.TryGetValue(pageNum, out var size) ? size.width : 0.0;
                        var pageHeight = pageSizeMap.TryGetValue(pageNum, out size) ? size.height : 0.0;

                        if (pageEntry.TryGetProperty("blocks", out var blocksElem) && blocksElem.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var blockElem in blocksElem.EnumerateArray())
                            {
                                var block = MapMinerUBlockToMinerUBlock(blockElem, pageWidth, pageHeight);
                                blocks.Add(block);
                            }
                        }

                        if (blocks.Count > 0)
                            minerUDoc.PagesBlocks[pageNum] = blocks;
                    }
                }
            }

            // 2. Parse pdf_info -> PagesBlocks (actual MinerU output format seen in the wild)
            if (root.TryGetProperty("pdf_info", out var pdfInfoElem) && pdfInfoElem.ValueKind == JsonValueKind.Array)
            {
                // Global ID counter to ensure unique IDs across ALL pages and sections.
                // Without this, blocks from different pages would have duplicate IDs (e.g., page1.id=1 and page2.id=1),
                // causing connection lines to point to the wrong blocks.
                int globalNextBlockId = 1;

                foreach (var pageInfoElem in pdfInfoElem.EnumerateArray())
                {
                    var pageNum = pageInfoElem.TryGetProperty("page_idx", out var pageIdxElem)
                        ? GetIntValue(pageIdxElem)
                        : 0;

                    if (pageNum < 0)
                        continue;

                    var pageWidth = pageInfoElem.TryGetProperty("page_size", out var pageSizeElemForPage) && pageSizeElemForPage.ValueKind == JsonValueKind.Array && pageSizeElemForPage.GetArrayLength() >= 2
                        ? GetDoubleValue(pageSizeElemForPage[0])
                        : 0.0;
                    var pageHeight = pageInfoElem.TryGetProperty("page_size", out var pageSizeElemForPage2) && pageSizeElemForPage2.ValueKind == JsonValueKind.Array && pageSizeElemForPage2.GetArrayLength() >= 2
                        ? GetDoubleValue(pageSizeElemForPage2[1])
                        : 0.0;

                    // Parse page_type if present (MinerU v2.3+)
                    var pageTypeStr = GetStringProperty(pageInfoElem, "page_type");
                    if (!string.IsNullOrEmpty(pageTypeStr))
                    {
                        var pageType = ParsePageType(pageTypeStr);
                        // Store with 0-based page index (will be normalized to 1-based later)
                        minerUDoc.PageTypes[pageNum] = pageType;
                    }

                    var blocks = new List<MinerUBlock>();
                    // Parse para_blocks (semantic paragraphs) and discarded_blocks for the MinerU Blocks column.
                    // This allows users to see which blocks were adopted/merged (para_blocks) vs rejected (discarded_blocks),
                    // providing a clear contrast with the PDF overlay layer which uses preproc_blocks (raw detection).
                    var paraBlocks = new List<MinerUBlock>();
                    var discardedBlocks = new List<MinerUBlock>();
                    // Use the global ID counter (declared outside the page loop) to ensure unique IDs
                    // across all pages. Without this, page 1 and page 2 would both have blocks with id=1,2,3...
                    if (pageInfoElem.TryGetProperty("para_blocks", out var paraElem) && paraElem.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var blockElem in paraElem.EnumerateArray())
                        {
                            var paraList = MapMinerUPageSectionToBlocks(blockElem, pageNum, pageWidth, pageHeight);
                            foreach (var b in paraList)
                            {
                                b.Id = globalNextBlockId++;
                                b.BlockSource = MinerUConstants.SourcePara;
                                paraBlocks.Add(b);
                            }
                        }
                    }
                    
                    if (pageInfoElem.TryGetProperty("discarded_blocks", out var discardedElem) && discardedElem.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var blockElem in discardedElem.EnumerateArray())
                        {
                            var discList = MapMinerUPageSectionToBlocks(blockElem, pageNum, pageWidth, pageHeight);
                            foreach (var b in discList)
                            {
                                b.Id = globalNextBlockId++;
                                b.BlockSource = MinerUConstants.SourceDiscarded;
                                discardedBlocks.Add(b);
                            }
                        }
                    }
                    
                    // Build a lookup map from block_id to block for fast matching
                    var paraBlockById = new Dictionary<string, MinerUBlock>();
                    foreach (var b in paraBlocks)
                    {
                        if (!string.IsNullOrEmpty(b.BlockId))
                            paraBlockById[b.BlockId] = b;
                    }
                    var discardedBlockById = new Dictionary<string, MinerUBlock>();
                    foreach (var b in discardedBlocks)
                    {
                        if (!string.IsNullOrEmpty(b.BlockId))
                            discardedBlockById[b.BlockId] = b;
                    }

                    // Build reverse mapping: for each preproc_block, find which para/discarded block it belongs to
                    // Use block_id (UUID) as the primary alignment key between PDF overlay and MinerU Blocks column.
                    // The new MinerU middle.json structure includes block_id in each block and block_ids array in para_blocks
                    // referencing the source preproc_block IDs. This provides exact matching without bbox overlap heuristics.
                    var pagePreprocBlocks = new List<MinerUBlock>();
                    if (pageInfoElem.TryGetProperty("preproc_blocks", out var preprocElem) && preprocElem.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var blockElem in preprocElem.EnumerateArray())
                        {
                            var preprocList = MapMinerUPageSectionToBlocks(blockElem, pageNum, pageWidth, pageHeight);
                            foreach (var preprocBlock in preprocList)
                            {
                                preprocBlock.Id = globalNextBlockId++;

                                // Try to match using block_id first (exact match via block_ids array in para_blocks)
                                MinerUBlock? matched = null;

                                // Strategy 1: Check if any para_block's SourceBlockIds contains this preprocBlock's BlockId
                                if (!string.IsNullOrEmpty(preprocBlock.BlockId))
                                {
                                    foreach (var paraBlock in paraBlocks)
                                    {
                                        if (paraBlock.SourceBlockIds.Contains(preprocBlock.BlockId))
                                        {
                                            matched = paraBlock;
                                            break;
                                        }
                                    }

                                    // If not found in para_blocks, check discarded_blocks
                                    if (matched == null)
                                    {
                                        foreach (var discBlock in discardedBlocks)
                                        {
                                            // First try: match via SourceBlockIds (for discarded_blocks with sub-blocks)
                                            if (discBlock.SourceBlockIds.Contains(preprocBlock.BlockId))
                                            {
                                                matched = discBlock;
                                                break;
                                            }
                                        }
                                        
                                        // Second try: if no match via SourceBlockIds, match discarded_blocks directly by BlockId
                                        // This handles the case where a discarded_block is a single block (no sub-blocks)
                                        // and its own BlockId is the same as the preproc_block's BlockId
                                        if (matched == null)
                                        {
                                            foreach (var discBlock in discardedBlocks)
                                            {
                                                if (discBlock.SourceBlockIds.Count == 0 && discBlock.BlockId == preprocBlock.BlockId)
                                                {
                                                    matched = discBlock;
                                                    break;
                                                }
                                            }
                                        }
                                    }
                                }

                                // Strategy 2: If block_id matching failed, fall back to bbox overlap
                                bool isFallbackMatch = false;
                                if (matched == null)
                                {
                                    matched = FindMatchingBlock(preprocBlock, paraBlocks, discardedBlocks);
                                    // Bbox overlap is a fallback match
                                    isFallbackMatch = true;
                                }
                                else
                                {
                                    // Check if the match was from a fallback inheritance (multiple block_ids inherited)
                                    // If matched block has multiple SourceBlockIds, it was a fallback inheritance
                                    if (matched.SourceBlockIds.Count > 1)
                                    {
                                        isFallbackMatch = true;
                                    }
                                }

                                if (matched != null)
                                {
                                    preprocBlock.DestinationType = matched.BlockSource;
                                    preprocBlock.IsFallbackMatch = isFallbackMatch;
                                    matched.IsFallbackMatch = isFallbackMatch;
                                    // Use BlockId (UUID string) for bidirectional relationship
                                    preprocBlock.RelatedBlockIds.Add(matched.BlockId);
                                    matched.RelatedBlockIds.Add(preprocBlock.BlockId);
                                }
                                pagePreprocBlocks.Add(preprocBlock);
                            }
                        }
                    }
                    
                    // Store preproc_blocks for this page
                    if (pagePreprocBlocks.Count > 0)
                    {
                        if (!minerUDoc.PreprocBlocks.ContainsKey(pageNum))
                            minerUDoc.PreprocBlocks[pageNum] = new List<MinerUBlock>();
                        minerUDoc.PreprocBlocks[pageNum].AddRange(pagePreprocBlocks);
                    }
                    
                    blocks.AddRange(paraBlocks);
                    blocks.AddRange(discardedBlocks);

                    if (blocks.Count > 0)
                    {
                        if (!minerUDoc.PagesBlocks.ContainsKey(pageNum))
                            minerUDoc.PagesBlocks[pageNum] = new List<MinerUBlock>();

                        minerUDoc.PagesBlocks[pageNum].AddRange(blocks);
                    }
                }
            }

            // 2. Parse tree -> TreeRoot
            if (root.TryGetProperty("tree", out var treeElem) && treeElem.ValueKind != JsonValueKind.Null)
            {
                minerUDoc.TreeRoot = MapMinerUTreeNode(treeElem, pageSizeMap);
                minerUDoc.BuildAggregationMap();
            }

            // 3. Normalize 0-based page indices to 1-based for both PagesBlocks and PreprocBlocks.
            // MinerU may use 0-based page numbers (page_idx 0 = first page),
            // while PageViewModel.PageNumber is 1-based.
            if (minerUDoc.PagesBlocks.Count > 0 && minerUDoc.PagesBlocks.ContainsKey(0))
            {
                var normalizedPages = new Dictionary<int, List<MinerUBlock>>();
                foreach (var kvp in minerUDoc.PagesBlocks)
                {
                    int newKey = kvp.Key + 1;
                    foreach (var block in kvp.Value)
                    {
                        if (block.Page == kvp.Key)
                            block.Page = newKey;
                    }
                    normalizedPages[newKey] = kvp.Value;
                }
                minerUDoc.PagesBlocks = normalizedPages;

                // Also normalize PreprocBlocks
                if (minerUDoc.PreprocBlocks.Count > 0)
                {
                    var normalizedPreproc = new Dictionary<int, List<MinerUBlock>>();
                    foreach (var kvp in minerUDoc.PreprocBlocks)
                    {
                        int newKey = kvp.Key + 1;
                        foreach (var block in kvp.Value)
                        {
                            if (block.Page == kvp.Key)
                                block.Page = newKey;
                        }
                        normalizedPreproc[newKey] = kvp.Value;
                    }
                    minerUDoc.PreprocBlocks = normalizedPreproc;
                }

                // Also normalize PageTypes (page_type enum values)
                if (minerUDoc.PageTypes.Count > 0)
                {
                    var normalizedPageTypes = new Dictionary<int, PageType>();
                    foreach (var kvp in minerUDoc.PageTypes)
                    {
                        normalizedPageTypes[kvp.Key + 1] = kvp.Value;
                    }
                    minerUDoc.PageTypes = normalizedPageTypes;
                }
            }

            // 4. Build InferenceBlocks from PagesBlocks
            minerUDoc.InferenceBlocks = minerUDoc.GetAllBlocks();

            return minerUDoc.InferenceBlocks.Count > 0 || minerUDoc.TreeRoot is not null
                ? minerUDoc
                : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Parses a MinerU result zip file and extracts MinerUDocument.
    /// Searches for *_middle.json in the extracted files.
    /// </summary>
    public static StructureDocument? TryParseMinerUZip(string zipPath)
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
            System.Diagnostics.Debug.WriteLine($"[MinerUJson] Failed to parse MinerU zip: {ex.Message}");
            throw new MinerUServiceException($"Failed to parse MinerU output: {ex.Message}", ex);
        }
        finally
        {
            // Clean up temporary extraction directory
            try { Directory.Delete(tempDir, true); }
            catch { /* Best-effort cleanup, ignore errors */ }
        }
    }

    /// <summary>
    /// Parses an already-extracted MinerU output directory and builds a MinerUDocument.
    /// Searches only for *_middle.json in the directory.
    /// </summary>
    /// <param name="extractedDir">Path to the extracted directory containing MinerU output files.</param>
    /// <returns>A MinerUDocument if parsing succeeds, otherwise null.</returns>
    public static StructureDocument? TryParseMinerUFromExtractedDir(string extractedDir)
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
            var minerUDoc = TryParseMinerUMiddleJson(jsonFile);
            if (minerUDoc is not null)
                return minerUDoc;
        }

        return null;
    }

    #endregion

    #region MinerU Mapping Helpers

    /// <summary>
    /// Maps a MinerU block JsonElement to a MinerUBlock.
    /// </summary>
    static MinerUBlock MapMinerUBlockToMinerUBlock(JsonElement elem, double pageWidth, double pageHeight)
    {
        var block = new MinerUBlock();

        // Parse block_id (UUID string) - the primary alignment key for matching blocks
        block.BlockId = GetStringProperty(elem, "block_id") ?? string.Empty;

        // Note: ID is assigned by the caller using a global counter to ensure uniqueness.
        // We no longer use JSON "index" or "id" fields as they can cause collisions.

        // Parse block_ids array (source preproc_block IDs referenced by this para_block)
        if (elem.TryGetProperty("block_ids", out var blockIdsElem) && blockIdsElem.ValueKind == JsonValueKind.Array)
        {
            foreach (var id in blockIdsElem.EnumerateArray())
            {
                if (id.ValueKind == JsonValueKind.String)
                {
                    var sourceId = id.GetString();
                    if (!string.IsNullOrEmpty(sourceId))
                    {
                        block.SourceBlockIds.Add(sourceId);
                    }
                }
            }
        }

        if (elem.TryGetProperty("page", out var pageElem))
            block.Page = GetIntValue(pageElem, block.Page);

        // First try direct "content" field
        block.Content = GetStringProperty(elem, "content") ?? string.Empty;

        // If content is empty, try to extract from lines/spans (handles image_body, image_footnote etc.)
        if (string.IsNullOrEmpty(block.Content))
        {
            block.Content = ExtractMinerUContent(elem);
        }

        // Determine type from source_label or type field
        var minerUType = GetStringProperty(elem, "source_label")
                        ?? GetStringProperty(elem, "type")
                        ?? GetStringProperty(elem, "category")
                        ?? string.Empty;

        block.SourceLabel = minerUType;
        block.Type = MapMinerUTypeToBlockType(minerUType);

        // Parse bbox
        if (elem.TryGetProperty("bbox", out var bboxElem) || elem.TryGetProperty("box", out bboxElem))
        {
            var (bbox, isNormalized) = ParseMinerUBbox(bboxElem, pageWidth, pageHeight);
            block.Bbox = bbox;
            block.IsBboxNormalized = isNormalized;
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

    static IEnumerable<MinerUBlock> MapMinerUPageSectionToBlocks(JsonElement sectionElem, int pageNum, double pageWidth, double pageHeight)
    {
        var results = new List<MinerUBlock>();

        if (sectionElem.ValueKind != JsonValueKind.Object)
            return results;

        if (sectionElem.TryGetProperty("blocks", out var blocksElem) && blocksElem.ValueKind == JsonValueKind.Array)
        {
            // Extract parent's block_ids so sub-blocks can inherit them for proper matching
            var parentBlockIds = new List<string>();
            if (sectionElem.TryGetProperty("block_ids", out var parentBlockIdsElem) && parentBlockIdsElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var id in parentBlockIdsElem.EnumerateArray())
                {
                    if (id.ValueKind == JsonValueKind.String)
                    {
                        var sid = id.GetString();
                        if (!string.IsNullOrEmpty(sid))
                            parentBlockIds.Add(sid);
                    }
                }
            }
            
            var blocksCount = blocksElem.GetArrayLength();
            
            for (int i = 0; i < blocksCount; i++)
            {
                var blockElem = blocksElem[i];
                var block = MapMinerUBlockToMinerUBlock(blockElem, pageWidth, pageHeight);
                if (block.Page <= 0)
                    block.Page = pageNum;
                
                // If sub-block has no own block_ids, determine which parent block_id to assign
                if (block.SourceBlockIds.Count == 0 && parentBlockIds.Count > 0)
                {
                    // If parent has exactly the same number of block_ids as sub-blocks,
                    // assign by index for precise 1:1 matching (e.g., image_body -> first block_id,
                    // image_footnote -> second block_id). This ensures preproc sub-blocks are
                    // matched to the correct para sub-blocks.
                    if (parentBlockIds.Count == blocksCount)
                    {
                        block.SourceBlockIds.Add(parentBlockIds[i]);
                    }
                    else
                    {
                        // Fallback: if counts don't match, inherit all parent block_ids
                        // to maintain backward compatibility with unusual JSON structures
                        block.SourceBlockIds = new System.Collections.Generic.List<string>(parentBlockIds);
                    }
                }
                
                results.Add(block);
            }
        }

        // Also support a direct content-bearing block object (no sub-blocks).
        if (sectionElem.TryGetProperty("lines", out var linesElem) && linesElem.ValueKind == JsonValueKind.Array)
        {
            // Parse block_id from the parent section element (e.g., preproc_block's own block_id)
            var blockId = GetStringProperty(sectionElem, "block_id") ?? string.Empty;
            
            // Parse block_ids array from the parent section element
            var sourceBlockIds = new List<string>();
            if (sectionElem.TryGetProperty("block_ids", out var blockIdsElem) && blockIdsElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var id in blockIdsElem.EnumerateArray())
                {
                    if (id.ValueKind == JsonValueKind.String)
                    {
                        var sid = id.GetString();
                        if (!string.IsNullOrEmpty(sid))
                            sourceBlockIds.Add(sid);
                    }
                }
            }
            
            var block = new MinerUBlock
            {
                Id = results.Count + 1,
                BlockId = blockId,
                Page = pageNum,
                Type = MapMinerUTypeToBlockType(GetStringProperty(sectionElem, "type") ?? string.Empty),
                SourceLabel = GetStringProperty(sectionElem, "type") ?? string.Empty,
                Content = ExtractMinerUContent(sectionElem),
            };
            // Set source block IDs if any
            if (sourceBlockIds.Count > 0)
            {
                block.SourceBlockIds = sourceBlockIds;
            }
            var (bbox, isNormalized) = ParseMinerUBbox(sectionElem.TryGetProperty("bbox", out var bboxElem) ? bboxElem : default, pageWidth, pageHeight);
            block.Bbox = bbox;
            block.IsBboxNormalized = isNormalized;
            // Note: ID is assigned by the caller using a global counter
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
                        // Try text content first
                        var content = GetStringProperty(spanElem, "content");
                        if (!string.IsNullOrEmpty(content))
                        {
                            parts.Add(content);
                        }
                        // For image spans, extract image_path instead
                        else
                        {
                            var imagePath = GetStringProperty(spanElem, "image_path");
                            if (!string.IsNullOrEmpty(imagePath))
                            {
                                parts.Add(imagePath);
                            }
                        }
                    }
                }
            }

            return string.Join("\n", parts);
        }

        return string.Empty;
    }

    /// <summary>
    /// Maps MinerU source_label/type to MinerUBlock.Type.
    /// </summary>
    static string MapMinerUTypeToBlockType(string minerUType)
    {
        if (string.IsNullOrEmpty(minerUType))
            return "text";

        var type = minerUType.ToLowerInvariant().Trim();

        return type switch
        {
            "paragraph_title" or "title" or "section_title" => "title",
            "paragraph" or "text" or "plain_text" or "body_text" => "text",
            "figure" or "image" or "picture" or "photo" or "image_body" => "image",
            "table" or "tabular" => "table",
            "figure_footnote" or "table_footnote" or "caption" or "image_caption" or "table_caption" or "image_footnote" => "caption",
            "header" or "footnote" or "footer" => "text",
            "equation" or "formula" => "text",
            "list" or "list_item" => "text",
            _ => "text"  // Default to text for unknown types
        };
    }

    /// <summary>
    /// Parses a bbox array [x1, y1, x2, y2] from MinerU, with optional normalization.
    /// Returns the parsed Rect and whether the coordinates were normalized to 0-1.
    /// </summary>
    static (Rect bbox, bool isNormalized) ParseMinerUBbox(JsonElement elem, double pageWidth = 0, double pageHeight = 0)
    {
        if (elem.ValueKind != JsonValueKind.Array || elem.GetArrayLength() < 4)
            return (new Rect(0, 0, 0, 0), false);

        var x1 = GetDoubleValue(elem[0]);
        var y1 = GetDoubleValue(elem[1]);
        var x2 = GetDoubleValue(elem[2]);
        var y2 = GetDoubleValue(elem[3]);

        bool normalized = false;

        // If page dimensions provided and coordinates look like absolute pixels (> 1000),
        // normalize to 0-1 range
        if (pageWidth > 0 && pageHeight > 0 && (x2 > 1000 || y2 > 1000))
        {
            x1 /= pageWidth;
            y1 /= pageHeight;
            x2 /= pageWidth;
            y2 /= pageHeight;
            normalized = true;
        }

        // Ensure valid bounds
        var width = Math.Max(0, x2 - x1);
        var height = Math.Max(0, y2 - y1);

        return (new Rect((double)x1, (double)y1, (double)width, (double)height), normalized);
    }

    /// <summary>
    /// Recursively maps a MinerU tree node JsonElement to a AnalysisTreeNode.
    /// </summary>
    static AnalysisTreeNode MapMinerUTreeNode(JsonElement elem, Dictionary<int, (double width, double height)> pageSizeMap)
    {
        var node = new AnalysisTreeNode();

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
                    var (bbox, _) = ParseMinerUBbox(bboxElem, pw, ph);
                    entry.Bbox = bbox;
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
    /// Parses a page_type string from MinerU into a PageType enum value.
    /// Supports 15 page types: cover, half_title, toc, body, chapter_start,
    /// image_dominant, table_dominant, blank, back_cover, copyright, colophon,
    /// acknowledgment, appendix, glossary, reference.
    /// </summary>
    static PageType ParsePageType(string pageTypeStr)
    {
        var type = pageTypeStr.ToLowerInvariant().Trim();
        return type switch
        {
            "cover" => PageType.cover,
            "half_title" => PageType.half_title,
            "toc" => PageType.toc,
            "body" => PageType.body,
            "chapter_start" => PageType.chapter_start,
            "image_dominant" => PageType.image_dominant,
            "table_dominant" => PageType.table_dominant,
            "blank" => PageType.blank,
            "back_cover" => PageType.back_cover,
            "copyright" => PageType.copyright,
            "colophon" => PageType.colophon,
            "acknowledgment" => PageType.acknowledgment,
            "appendix" => PageType.appendix,
            "glossary" => PageType.glossary,
            "reference" => PageType.reference,
            _ => PageType.unknown
        };
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

    /// <summary>
    /// Finds the matching para_block or discarded_block for a preproc_block based on bbox overlap.
    /// Returns the block with the largest overlap ratio (prefer para_blocks over discarded_blocks),
    /// or null if no significant overlap found (>= MinerUConstants.MinimumOverlapRatio threshold).
    /// Uses > for comparison to prefer earlier (first) matches when ratios are equal.
    /// </summary>
    static MinerUBlock? FindMatchingBlock(MinerUBlock preprocBlock, List<MinerUBlock> paraBlocks, List<MinerUBlock> discardedBlocks)
    {
        MinerUBlock? best = null;
        double bestRatio = MinerUConstants.MinimumOverlapRatio;

        // First, search para_blocks (higher priority - adopted blocks)
        foreach (var b in paraBlocks)
        {
            var ratio = CalculateOverlapRatio(preprocBlock, b);
            if (ratio > bestRatio)
            {
                bestRatio = ratio;
                best = b;
            }
        }

        // Then search discarded_blocks (lower priority - only if no para match found)
        if (best is null)
        {
            foreach (var b in discardedBlocks)
            {
                var ratio = CalculateOverlapRatio(preprocBlock, b);
                if (ratio > bestRatio)
                {
                    bestRatio = ratio;
                    best = b;
                }
            }
        }

        return best;
    }

    /// <summary>
    /// Calculates the overlap ratio between two blocks (overlap area / preprocBlock area).
    /// </summary>
    static double CalculateOverlapRatio(MinerUBlock a, MinerUBlock b)
    {
        var aX1 = a.Bbox.X;
        var aY1 = a.Bbox.Y;
        var aX2 = a.Bbox.Right;
        var aY2 = a.Bbox.Bottom;
        
        var bX1 = b.Bbox.X;
        var bY1 = b.Bbox.Y;
        var bX2 = b.Bbox.Right;
        var bY2 = b.Bbox.Bottom;
        
        // Calculate overlap rectangle
        var overlapX1 = Math.Max(aX1, bX1);
        var overlapY1 = Math.Max(aY1, bY1);
        var overlapX2 = Math.Min(aX2, bX2);
        var overlapY2 = Math.Min(aY2, bY2);
        
        if (overlapX1 >= overlapX2 || overlapY1 >= overlapY2)
            return 0.0;
        
        var overlapArea = (overlapX2 - overlapX1) * (overlapY2 - overlapY1);
        var aArea = (aX2 - aX1) * (aY2 - aY1);
        
        return aArea > 0 ? overlapArea / aArea : 0.0;
    }

    #endregion
}
