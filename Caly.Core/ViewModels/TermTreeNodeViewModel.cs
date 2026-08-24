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
using CommunityToolkit.Mvvm.ComponentModel;

namespace Caly.Core.ViewModels;

/// <summary>
/// ViewModel for a single node in the term hierarchy tree.
/// Supports expand/collapse for TreeView binding.
/// </summary>
public partial class TermTreeNodeViewModel : ObservableObject
{
    public string Term { get; }
    public string Category { get; }
    public double Score { get; }
    public string? Definition { get; }
    public int Depth { get; }
    
    // ── P2: Relation metadata for UI display ──
    
    /// <summary>Relation type from parent (e.g., "HYPERNYM", "IS_A"). Null for root nodes.</summary>
    public string? ParentRelationType { get; }
    
    /// <summary>Confidence of the relation from parent (0.0-1.0). Null for root nodes.</summary>
    public double? ParentRelationConfidence { get; }
    
    /// <summary>Evidence text for the relation from parent. Null for root nodes.</summary>
    public string? ParentRelationEvidence { get; }

    [ObservableProperty]
    private bool _isExpanded;

    public ObservableCollection<TermTreeNodeViewModel> Children { get; } = new();

    public bool HasChildren => Children.Count > 0;

    public string ScoreDisplay => $"{Score:F2}";
    
    /// <summary>Display text for relation type (e.g., "上下级", "定义").</summary>
    public string? RelationTypeDisplay => ParentRelationType switch
    {
        "HYPERNYM" => "上下级",
        "IS_A" => "定义",
        "HYPONYM" => "包含",
        "SYNONYM" => "同义",
        null => null,
        _ => ParentRelationType
    };
    
    /// <summary>Display text for relation confidence (e.g., "置信度: 0.65").</summary>
    public string? RelationConfidenceDisplay => ParentRelationConfidence is double conf 
        ? $"置信度: {conf:F2}" 
        : null;
    
    /// <summary>Whether this node has relation info to display (non-root node).</summary>
    public bool HasRelationInfo => ParentRelationType != null;
    
    /// <summary>Short evidence display (truncated if too long).</summary>
    public string? RelationEvidenceDisplay => ParentRelationEvidence != null 
        ? (ParentRelationEvidence.Length > 50 
            ? ParentRelationEvidence.Substring(0, 50) + "..." 
            : ParentRelationEvidence)
        : null;

    public string CategoryColorHex => Category switch
    {
        "MATERIAL" => "#4CAF50",
        "STANDARD" => "#FF9800",
        "PRODUCT" => "#2196F3",
        "PERSON" => "#E91E63",
        "ORGANIZATION" => "#9C27B0",
        "LOCATION" => "#00BCD4",
        "DATE" => "#607D8B",
        "CONCEPT" => "#795548",
        _ => "#757575",
    };

    public string IndentDisplay => new string(' ', Depth * 2);

    public TermTreeNodeViewModel(string term, string category, double score, string? definition, int depth,
        string? parentRelationType = null, double? parentRelationConfidence = null, string? parentRelationEvidence = null)
    {
        Term = term;
        Category = category;
        Score = score;
        Definition = definition;
        Depth = depth;
        ParentRelationType = parentRelationType;
        ParentRelationConfidence = parentRelationConfidence;
        ParentRelationEvidence = parentRelationEvidence;
        _isExpanded = depth <= 1; // Auto-expand top 2 levels
    }
}
