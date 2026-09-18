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

using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;

namespace Caly.Core.Models;

/// <summary>
/// Complete Popo document model that holds normalized blocks, inference results,
/// tree structure, and aggregation relationships.
/// </summary>
public partial class StructureDocument : ObservableObject
{
    /// <summary>
    /// Document ID.
    /// </summary>
    private string _docId = string.Empty;
    public string DocId
    {
        get => _docId;
        set => SetProperty(ref _docId, value);
    }

    /// <summary>
    /// Model name (mineru, monkeyocr, etc.).
    /// </summary>
    private string _modelName = string.Empty;
    public string ModelName
    {
        get => _modelName;
        set => SetProperty(ref _modelName, value);
    }

    /// <summary>
    /// Path to the artifacts directory (images, etc.) relative to the project root.
    /// Stored in popo_result.json so image paths can be resolved without directory scanning.
    /// Example: "mineru/978-xxx/hybrid_auto"
    /// </summary>
    private string? _artifactsDir;
    public string? ArtifactsDir
    {
        get => _artifactsDir;
        set => SetProperty(ref _artifactsDir, value);
    }

    /// <summary>
    /// Normalized blocks grouped by page number (para+discarded blocks).
    /// </summary>
    private Dictionary<int, List<MinerUBlock>> _pagesBlocks = new();
    public Dictionary<int, List<MinerUBlock>> PagesBlocks
    {
        get => _pagesBlocks;
        set => SetProperty(ref _pagesBlocks, value);
    }

    /// <summary>
    /// Preproc blocks (raw detection blocks) grouped by page number.
    /// These are the blocks shown on the PDF overlay, and are connected to
    /// para/discarded blocks via the connection lines.
    /// </summary>
    private Dictionary<int, List<MinerUBlock>> _preprocBlocks = new();
    public Dictionary<int, List<MinerUBlock>> PreprocBlocks
    {
        get => _preprocBlocks;
        set => SetProperty(ref _preprocBlocks, value);
    }

    /// <summary>
    /// Inference blocks (flat list with contd/level/image fields).
    /// </summary>
    private List<MinerUBlock> _inferenceBlocks = new();
    public List<MinerUBlock> InferenceBlocks
    {
        get => _inferenceBlocks;
        set => SetProperty(ref _inferenceBlocks, value);
    }

    /// <summary>
    /// Final tree structure root node.
    /// </summary>
    private AnalysisTreeNode? _treeRoot;
    public AnalysisTreeNode? TreeRoot
    {
        get => _treeRoot;
        set => SetProperty(ref _treeRoot, value);
    }

    /// <summary>
    /// Aggregation map: tree node -> source blocks mapping.
    /// </summary>
    private Dictionary<int, List<MinerUBlock>> _aggregationMap = new();
    public Dictionary<int, List<MinerUBlock>> AggregationMap
    {
        get => _aggregationMap;
        set => SetProperty(ref _aggregationMap, value);
    }

    /// <summary>
    /// List of aggregation relationships for visualization.
    /// </summary>
    private List<AnalysisAggregation> _aggregations = new();
    public List<AnalysisAggregation> Aggregations
    {
        get => _aggregations;
        set => SetProperty(ref _aggregations, value);
    }

    /// <summary>
    /// Page type classification from MinerU's page-level analysis.
    /// Maps page number (1-based) to the PageType enum value.
    /// Populated when parsing MinerU middle.json with "page_type" field.
    /// </summary>
    private Dictionary<int, PageType> _pageTypes = new();
    public Dictionary<int, PageType> PageTypes
    {
        get => _pageTypes;
        set => SetProperty(ref _pageTypes, value);
    }

    /// <summary>
    /// Secondary page type classification from MinerU's page-level analysis (MinerU v2.0+).
    /// Maps page number (1-based) to the PageType enum value for mixed-type pages.
    /// For example, a page may be primarily "toc" (table of contents) with a secondary "copyright" type.
    /// This field is optional — only present when a page contains multiple types of content.
    /// </summary>
    private Dictionary<int, PageType> _pageTypeSecondaries = new();
    public Dictionary<int, PageType> PageTypeSecondaries
    {
        get => _pageTypeSecondaries;
        set => SetProperty(ref _pageTypeSecondaries, value);
    }

    /// <summary>
    /// Gets the secondary page type for a given page number, or null if not set.
    /// </summary>
    public PageType? GetPageTypeSecondary(int pageNumber)
    {
        return PageTypeSecondaries.TryGetValue(pageNumber, out var type) ? type : (PageType?)null;
    }

    /// <summary>
    /// Table of contents entries, seeded from popo_result.json (result.toc.entries)
    /// and supplemented with heading nodes from the analysis tree
    /// (PopoJsonService.SupplementTocFromTree) so the list is complete even when
    /// the API returned only a partial toc. Each entry maps to a section in the
    /// document with title, level, and verified page.
    /// </summary>
    private List<TocEntry> _tocEntries = new();
    public List<TocEntry> TocEntries
    {
        get => _tocEntries;
        set => SetProperty(ref _tocEntries, value);
    }

    /// <summary>
    /// Pre-parsed bibliographic reference entries from popo_result.json (result.reference.entries).
    /// Each entry represents a cited work with extracted metadata (authors, title, year, etc.)
    /// and the page number where it appears in the document.
    /// </summary>
    private List<ReferenceEntry> _referenceEntries = new();
    public List<ReferenceEntry> ReferenceEntries
    {
        get => _referenceEntries;
        set => SetProperty(ref _referenceEntries, value);
    }

    /// <summary>
    /// Pre-parsed formula/equation entries extracted from the document.
    /// Each entry represents a formula with its raw content, formula code, page location,
    /// and placeholder for NLP analysis. Extracted from MinerU blocks of type "equation"
    /// and text blocks containing display formulas.
    /// </summary>
    private List<FormulaEntry> _formulaEntries = new();
    public List<FormulaEntry> FormulaEntries
    {
        get => _formulaEntries;
        set => SetProperty(ref _formulaEntries, value);
    }

    /// <summary>
    /// Book copyright metadata JSON string from popo_result.json (result.metadata).
    /// Contains publisher, ISBN, authors, edition, and other bibliographic information.
    /// Serialized from the metadata dict in the Popo API response.
    /// </summary>
    private string? _bookMetadataJson;
    public string? BookMetadataJson
    {
        get => _bookMetadataJson;
        set => SetProperty(ref _bookMetadataJson, value);
    }

    /// <summary>
    /// Gets all blocks across all pages as a flat list.
    /// </summary>
    public List<MinerUBlock> GetAllBlocks()
    {
        var allBlocks = new List<MinerUBlock>();
        foreach (var pageBlocks in PagesBlocks.Values)
        {
            allBlocks.AddRange(pageBlocks);
        }
        return allBlocks;
    }

    /// <summary>
    /// Gets blocks for a specific page (para+discarded).
    /// </summary>
    public List<MinerUBlock> GetBlocksForPage(int pageNumber)
    {
        return PagesBlocks.TryGetValue(pageNumber, out var blocks) ? blocks : new List<MinerUBlock>();
    }

    /// <summary>
    /// Gets preproc blocks (raw detection blocks) for a specific page.
    /// </summary>
    public List<MinerUBlock> GetPreprocBlocksForPage(int pageNumber)
    {
        return PreprocBlocks.TryGetValue(pageNumber, out var blocks) ? blocks : new List<MinerUBlock>();
    }

    /// <summary>
    /// Finds a block by its ID.
    /// </summary>
    public MinerUBlock? FindBlockById(int id)
    {
        foreach (var block in InferenceBlocks)
        {
            if (block.Id == id)
                return block;
        }
        return null;
    }

    /// <summary>
    /// Builds aggregation map from tree structure.
    /// </summary>
    public void BuildAggregationMap()
    {
        AggregationMap.Clear();
        if (TreeRoot is null)
            return;

        BuildAggregationMapRecursive(TreeRoot);
    }

    private void BuildAggregationMapRecursive(AnalysisTreeNode node)
    {
        if (node.BlockIds.Count > 0)
        {
            var blocks = new List<MinerUBlock>();
            foreach (var blockId in node.BlockIds)
            {
                var block = FindBlockById(blockId);
                if (block is not null)
                    blocks.Add(block);
            }

            // Use the first block ID as key for this node
            AggregationMap[node.BlockIds[0]] = blocks;
        }

        foreach (var child in node.Children)
        {
            BuildAggregationMapRecursive(child);
        }
    }
}