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

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Caly.Core.Models;
using Caly.Core.ViewModels;

namespace Caly.Tests;

/// <summary>
/// Tests for <see cref="DocumentViewModel.LinkSemanticResultsToPopoNodes"/> — the
/// matching that attaches each SemanticBlockResult to its Popo tree node by
/// overlapping SourceBlockIds (MinerU block UUIDs). This is what sets
/// HasSemanticResult, which ConnectionLinesControl requires to draw the
/// Popo -> Semantic connection lines.
/// </summary>
public class SemanticPopoLinkTests
{
    private static TreeNodeViewModel MakeNode(string title, params string[] sourceBlockIds)
    {
        var node = new AnalysisTreeNode
        {
            Type = "text",
            Title = title,
            Content = title,
            SourceBlockIds = sourceBlockIds.ToList()
        };
        return new TreeNodeViewModel(node);
    }

    private static SemanticBlockResult MakeBlock(params string[] sourceBlockIds)
        => new() { SourceBlockIds = sourceBlockIds.ToList() };

    [Fact]
    public void LinkSemanticResults_MatchesBySourceBlockIds()
    {
        var nodeA = MakeNode("A", "uuid-1");
        var nodeB = MakeNode("B", "uuid-2");
        var root = new ObservableCollection<TreeNodeViewModel> { nodeA, nodeB };

        var blockA = MakeBlock("uuid-1");
        var blockB = MakeBlock("uuid-2");
        var blocks = new List<SemanticBlockResult> { blockA, blockB };

        DocumentViewModel.LinkSemanticResultsToPopoNodes(root, blocks);

        Assert.True(nodeA.HasSemanticResult);
        Assert.Same(blockA, nodeA.SemanticResult);
        Assert.True(nodeB.HasSemanticResult);
        Assert.Same(blockB, nodeB.SemanticResult);
    }

    [Fact]
    public void LinkSemanticResults_NoMatch_LeavesNoResult()
    {
        var nodeA = MakeNode("A", "uuid-1");
        var root = new ObservableCollection<TreeNodeViewModel> { nodeA };

        var block = MakeBlock("uuid-999");
        DocumentViewModel.LinkSemanticResultsToPopoNodes(root, new List<SemanticBlockResult> { block });

        Assert.False(nodeA.HasSemanticResult);
    }

    [Fact]
    public void LinkSemanticResults_NestedNode_Matches()
    {
        var parent = MakeNode("Parent"); // no SourceBlockIds
        var child = MakeNode("Child", "uuid-3");
        parent.Children.Add(child);
        var root = new ObservableCollection<TreeNodeViewModel> { parent };

        var block = MakeBlock("uuid-3");
        DocumentViewModel.LinkSemanticResultsToPopoNodes(root, new List<SemanticBlockResult> { block });

        Assert.False(parent.HasSemanticResult);
        Assert.True(child.HasSemanticResult);
        Assert.Same(block, child.SemanticResult);
    }

    [Fact]
    public void LinkSemanticResults_ClearsStaleResults()
    {
        var nodeA = MakeNode("A", "uuid-1");
        var root = new ObservableCollection<TreeNodeViewModel> { nodeA };

        // First link: matches.
        var blockA = MakeBlock("uuid-1");
        DocumentViewModel.LinkSemanticResultsToPopoNodes(root, new List<SemanticBlockResult> { blockA });
        Assert.True(nodeA.HasSemanticResult);

        // Second link: no matching block -> previous result must be cleared.
        var blockOther = MakeBlock("uuid-999");
        DocumentViewModel.LinkSemanticResultsToPopoNodes(root, new List<SemanticBlockResult> { blockOther });
        Assert.False(nodeA.HasSemanticResult);
    }

    [Fact]
    public void LinkSemanticResults_NullBlocks_ClearsAll()
    {
        var nodeA = MakeNode("A", "uuid-1");
        var root = new ObservableCollection<TreeNodeViewModel> { nodeA };

        var blockA = MakeBlock("uuid-1");
        DocumentViewModel.LinkSemanticResultsToPopoNodes(root, new List<SemanticBlockResult> { blockA });
        Assert.True(nodeA.HasSemanticResult);

        DocumentViewModel.LinkSemanticResultsToPopoNodes(root, null);
        Assert.False(nodeA.HasSemanticResult);
    }
}
