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
using CommunityToolkit.Mvvm.ComponentModel;

namespace Caly.Core.ViewModels;

/// <summary>
/// ViewModel wrapping a <see cref="TermResultFile"/> for UI binding.
/// Provides the root nodes of the term hierarchy tree and summary statistics.
/// </summary>
public partial class TermBlockViewModel : ObservableObject
{
    private readonly TermResultFile _result;

    /// <summary>Root nodes of the term hierarchy tree.</summary>
    public ObservableCollection<TermTreeNodeViewModel> RootNodes { get; } = new();

    /// <summary>Summary statistics for display.</summary>
    public TermStatistics? Statistics => _result.Statistics;

    public int TermCount => _result.Terms.Count;
    public int DefinedCount => _result.Terms.Count(t => !string.IsNullOrEmpty(t.Definition));
    public int HypernymCount => _result.Statistics?.HypernymRelations ?? 0;
    public int SynonymCount => _result.Statistics?.SynonymRelations ?? 0;
    public int RelationCount => _result.Relations.Count;
    public int MaxDepth => _result.Statistics?.MaxDepth ?? 0;

    public string Summary
    {
        get
        {
            var parts = new List<string>();
            parts.Add($"{TermCount} terms");
            if (DefinedCount > 0) parts.Add($"{DefinedCount} defined");
            if (HypernymCount > 0) parts.Add($"{HypernymCount} HYPERNYM");
            if (SynonymCount > 0) parts.Add($"{SynonymCount} SYNONYM");
            if (RelationCount > 0) parts.Add($"{RelationCount} relations");
            if (MaxDepth > 0) parts.Add($"max depth {MaxDepth}");
            return string.Join(" · ", parts);
        }
    }

    public TermBlockViewModel(TermResultFile result)
    {
        _result = result;
        if (result.TermTree.Count > 0)
        {
            BuildTreeNodes(result.TermTree);
        }
        else
        {
            // Fallback: build flat tree from flat term list (no hierarchy available)
            BuildFlatTree(result.Terms);
        }
    }

    private void BuildTreeNodes(List<TermTreeNodeOutput> nodes)
    {
        foreach (var node in nodes)
        {
            RootNodes.Add(ConvertNode(node));
        }
    }

    private void BuildFlatTree(List<TermEntry> terms)
    {
        foreach (var term in terms.OrderByDescending(t => t.Score))
        {
            RootNodes.Add(new TermTreeNodeViewModel(
                term.Text,
                term.Category,
                term.Score,
                term.Definition,
                0));
        }
    }

    private static TermTreeNodeViewModel ConvertNode(TermTreeNodeOutput node)
    {
        var vm = new TermTreeNodeViewModel(
            node.Term,
            node.Category,
            node.Score,
            node.Definition,
            node.Depth,
            // P2: Pass relation metadata for UI display
            node.ParentRelationType,
            node.ParentRelationConfidence,
            node.ParentRelationEvidence);

        foreach (var child in node.Children)
        {
            vm.Children.Add(ConvertNode(child));
        }

        return vm;
    }
}
