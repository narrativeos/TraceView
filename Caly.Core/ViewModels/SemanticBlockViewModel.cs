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

using Caly.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Caly.Core.ViewModels;

/// <summary>
/// View model wrapping SemanticBlockResult with expand/collapse functionality.
/// Similar to TreeNodeViewModel's expand approach.
/// </summary>
public partial class SemanticBlockViewModel : ObservableObject
{
    private readonly SemanticBlockResult _result;

    [ObservableProperty]
    private bool _isDetailsExpanded;

    public SemanticBlockViewModel(SemanticBlockResult result)
    {
        _result = result;
    }

    /// <summary>
    /// Toggle details expand/collapse.
    /// </summary>
    [RelayCommand]
    private void ToggleDetailsExpand()
    {
        IsDetailsExpanded = !IsDetailsExpanded;
    }

    #region Forwarded properties from SemanticBlockResult

    public string Type => _result.Type;
    public string Title => _result.Title;
    public string Content => _result.Content;
    public string TypeColorHex => _result.TypeColorHex;
    public string ContentPreview => _result.ContentPreview;
    public string EntitySummary => _result.EntitySummary;
    public bool HasTitleOrType => _result.HasTitleOrType;
    public bool HasContent => _result.HasContent;
    public bool HasEntities => _result.HasEntities;
    public bool HasExpandableDetails => _result.HasExpandableDetails;

    public System.Collections.Generic.List<Models.SemanticToken> Tokens => _result.Tokens;
    public System.Collections.Generic.List<Models.SemanticEntity> Entities => _result.Entities;
    public System.Collections.Generic.List<Models.SemanticRelation> Relations => _result.Relations;

    public System.Collections.Generic.List<Models.SemanticEntity> LocationEntities => _result.LocationEntities;
    public System.Collections.Generic.List<Models.SemanticEntity> DateEntities => _result.DateEntities;
    public System.Collections.Generic.List<Models.SemanticEntity> PersonEntities => _result.PersonEntities;
    public System.Collections.Generic.List<Models.SemanticEntity> NumberEntities => _result.NumberEntities;
    public System.Collections.Generic.List<Models.SemanticEntity> FacilityEntities => _result.FacilityEntities;
    public System.Collections.Generic.List<Models.SemanticEntity> OrganizationEntities => _result.OrganizationEntities;
    public System.Collections.Generic.List<Models.SemanticEntity> OtherEntities => _result.OtherEntities;

    public System.Collections.Generic.List<Models.SemanticDepToken> DepTokens => _result.DepTokens;
    public System.Collections.Generic.List<Models.SemanticDepEdge> DepEdges => _result.DepEdges;
    public bool HasDepTree => _result.HasDepTree;

    #endregion
}
