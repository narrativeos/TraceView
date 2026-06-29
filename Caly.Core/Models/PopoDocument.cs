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
public partial class PopoDocument : ObservableObject
{
    /// <summary>
    /// Document ID.
    /// </summary>
    [ObservableProperty]
    private string _docId = string.Empty;

    /// <summary>
    /// Model name (mineru, monkeyocr, etc.).
    /// </summary>
    [ObservableProperty]
    private string _modelName = string.Empty;

    /// <summary>
    /// Normalized blocks grouped by page number.
    /// </summary>
    [ObservableProperty]
    private Dictionary<int, List<PopoBlock>> _pagesBlocks = new();

    /// <summary>
    /// Inference blocks (flat list with contd/level/image fields).
    /// </summary>
    [ObservableProperty]
    private List<PopoBlock> _inferenceBlocks = new();

    /// <summary>
    /// Final tree structure root node.
    /// </summary>
    [ObservableProperty]
    private PopoTreeNode? _treeRoot;

    /// <summary>
    /// Aggregation map: tree node -> source blocks mapping.
    /// </summary>
    [ObservableProperty]
    private Dictionary<int, List<PopoBlock>> _aggregationMap = new();

    /// <summary>
    /// List of aggregation relationships for visualization.
    /// </summary>
    [ObservableProperty]
    private List<PopoAggregation> _aggregations = new();

    /// <summary>
    /// Gets all blocks across all pages as a flat list.
    /// </summary>
    public List<PopoBlock> GetAllBlocks()
    {
        var allBlocks = new List<PopoBlock>();
        foreach (var pageBlocks in PagesBlocks.Values)
        {
            allBlocks.AddRange(pageBlocks);
        }
        return allBlocks;
    }

    /// <summary>
    /// Gets blocks for a specific page.
    /// </summary>
    public List<PopoBlock> GetBlocksForPage(int pageNumber)
    {
        return PagesBlocks.TryGetValue(pageNumber, out var blocks) ? blocks : new List<PopoBlock>();
    }

    /// <summary>
    /// Finds a block by its ID.
    /// </summary>
    public PopoBlock? FindBlockById(int id)
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

    private void BuildAggregationMapRecursive(PopoTreeNode node)
    {
        if (node.BlockIds.Count > 0)
        {
            var blocks = new List<PopoBlock>();
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