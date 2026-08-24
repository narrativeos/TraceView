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
using System.Linq;

namespace Caly.Core.Services;

/// <summary>
/// Represents a node in the term hierarchy tree.
/// </summary>
public class TermTreeNode
{
    /// <summary>The term text (normalized).</summary>
    public string Term { get; set; } = string.Empty;

    /// <summary>Original entity category.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Importance score (0.0–1.0).</summary>
    public double Score { get; set; }

    /// <summary>Definition text, if available.</summary>
    public string? Definition { get; set; }

    /// <summary>Depth in the tree (0 = root).</summary>
    public int Depth { get; set; }

    /// <summary>Child terms (hyponyms).</summary>
    public List<TermTreeNode> Children { get; set; } = new();

    /// <summary>Whether this node has been visited during cycle detection.</summary>
    internal bool Visited { get; set; }

    /// <summary>Whether this node is in the current recursion stack.</summary>
    internal bool InStack { get; set; }
    
    // ── P2: Relation metadata for UI display ──
    
    /// <summary>Relation type from parent (e.g., "HYPERNYM", "IS_A"). Null for root nodes.</summary>
    public string? ParentRelationType { get; set; }
    
    /// <summary>Confidence of the relation from parent (0.0-1.0). Null for root nodes.</summary>
    public double? ParentRelationConfidence { get; set; }
    
    /// <summary>Evidence text for the relation from parent. Null for root nodes.</summary>
    public string? ParentRelationEvidence { get; set; }
}

/// <summary>
/// Builds a hierarchical term tree from flat term relations.
/// Constructs a multi-tree using HYPERNYM/HYPONYM edges with cycle detection.
/// </summary>
public class TermHierarchyBuilder
{
    /// <summary>
    /// Builds a list of root-level term tree nodes from term relations and scores.
    /// </summary>
    /// <param name="terms">All term texts with their scores.</param>
    /// <param name="hypernymRelations">HYPERNYM relations (term → hypernym, confidence, evidence, relationType).</param>
    /// <param name="definitions">Term definitions keyed by term text.</param>
    /// <param name="categories">Entity categories keyed by term text.</param>
    /// <returns>List of root nodes (terms with no hypernym).</returns>
    public List<TermTreeNode> Build(
        IReadOnlyDictionary<string, double> terms,
        IReadOnlyList<(string Term, string Hypernym, double Confidence, string? Evidence, string RelationType)> hypernymRelations,
        IReadOnlyDictionary<string, string> definitions,
        IReadOnlyDictionary<string, string> categories)
    {
        // ── Phase 1: Build parent → children mapping ──
        // Key = hypernym (parent), Value = list of terms (children)
        var childrenMap = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var relationConfidences = new Dictionary<(string, string), double>();
        var relationEvidence = new Dictionary<(string, string), string?>();
        var relationTypes = new Dictionary<(string, string), string>();

        foreach (var (term, hypernym, confidence, evidence, relationType) in hypernymRelations)
        {
            if (!childrenMap.ContainsKey(hypernym))
                childrenMap[hypernym] = new List<string>();
            if (!childrenMap[hypernym].Contains(term))
            {
                childrenMap[hypernym].Add(term);
                relationConfidences[(term, hypernym)] = confidence;
                relationEvidence[(term, hypernym)] = evidence;
                relationTypes[(term, hypernym)] = relationType;
            }
        }

        // ── Phase 2: Identify root nodes (terms not appearing as children of any other term) ──
        var allChildTerms = new HashSet<string>(StringComparer.Ordinal);
        foreach (var kvp in childrenMap)
            foreach (var child in kvp.Value)
                allChildTerms.Add(child);

        // Root = terms that are NOT children AND have children themselves, OR are standalone
        var rootTerms = new HashSet<string>(StringComparer.Ordinal);
        foreach (var term in terms.Keys)
        {
            if (!allChildTerms.Contains(term) || childrenMap.ContainsKey(term))
                rootTerms.Add(term);
        }

        // ── Phase 3: Recursively build tree with cycle detection ──
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var nodeCache = new Dictionary<string, TermTreeNode>(StringComparer.Ordinal);
        var roots = new List<TermTreeNode>();

        foreach (var rootTerm in rootTerms)
        {
            if (visited.Contains(rootTerm)) continue;

            var node = BuildSubtree(rootTerm, childrenMap, terms, definitions, categories,
                relationConfidences, relationEvidence, relationTypes,
                visited, nodeCache, 0, new HashSet<string>(StringComparer.Ordinal));
            if (node != null)
                roots.Add(node);
        }

        // ── Phase 4: Sort by score descending ──
        roots.Sort((a, b) => b.Score.CompareTo(a.Score));
        SortChildrenRecursive(roots);

        return roots;
    }

    private TermTreeNode? BuildSubtree(
        string term,
        Dictionary<string, List<string>> childrenMap,
        IReadOnlyDictionary<string, double> scores,
        IReadOnlyDictionary<string, string> definitions,
        IReadOnlyDictionary<string, string> categories,
        Dictionary<(string, string), double> relationConfidences,
        Dictionary<(string, string), string?> relationEvidence,
        Dictionary<(string, string), string> relationTypes,
        HashSet<string> visited,
        Dictionary<string, TermTreeNode> nodeCache,
        int depth,
        HashSet<string> pathStack,
        string? parentRelationType = null,
        double? parentRelationConfidence = null,
        string? parentRelationEvidence = null)
    {
        // Cycle detection: if already in current path, skip
        if (pathStack.Contains(term))
            return null;

        // If already cached, return deep copy at correct depth
        if (nodeCache.TryGetValue(term, out var cached))
        {
            // Update depth if we found a shorter path
            if (depth < cached.Depth)
                cached.Depth = depth;
            return cached;
        }

        pathStack.Add(term);
        visited.Add(term);

        var node = new TermTreeNode
        {
            Term = term,
            Category = categories.TryGetValue(term, out var cat) ? cat : "CONCEPT",
            Score = scores.TryGetValue(term, out var s) ? s : 0.0,
            Definition = definitions.TryGetValue(term, out var def) ? def : null,
            Depth = depth,
            // P2: Pass relation metadata from parent
            ParentRelationType = parentRelationType,
            ParentRelationConfidence = parentRelationConfidence,
            ParentRelationEvidence = parentRelationEvidence
        };

        nodeCache[term] = node;

        // Build children
        if (childrenMap.TryGetValue(term, out var children))
        {
            foreach (var child in children.OrderByDescending(c =>
                relationConfidences.TryGetValue((c, term), out var conf) ? conf : 0.0))
            {
                // P2: Get relation metadata for this child
                var childRelationType = relationTypes.TryGetValue((child, term), out var rt) ? rt : "HYPERNYM";
                var childRelationConfidence = relationConfidences.TryGetValue((child, term), out var childConf) ? childConf : (double?)null;
                var childRelationEvidence = relationEvidence.TryGetValue((child, term), out var childEv) ? childEv : null;
                
                var childNode = BuildSubtree(child, childrenMap, scores, definitions, categories,
                    relationConfidences, relationEvidence, relationTypes,
                    visited, nodeCache, depth + 1, pathStack,
                    childRelationType, childRelationConfidence, childRelationEvidence);
                if (childNode != null)
                    node.Children.Add(childNode);
            }
        }

        pathStack.Remove(term);
        return node;
    }

    private static void SortChildrenRecursive(List<TermTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            node.Children.Sort((a, b) => b.Score.CompareTo(a.Score));
            SortChildrenRecursive(node.Children);
        }
    }
}
