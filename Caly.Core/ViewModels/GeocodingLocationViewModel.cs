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
using System.Collections.ObjectModel;
using Avalonia.Media;
using Caly.Core.Models;
using Caly.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;


namespace Caly.Core.ViewModels;

/// <summary>
/// Status of a single location geocoding operation.
/// </summary>
public enum GeocodingStatus
{
    Pending,
    Processing,
    Success,
    Failed,
}

/// <summary>
/// ViewModel for a single geocoding result (sub-item in expandable row).
/// </summary>
public partial class GeocodingResultViewModel : ObservableObject
{
    public GeocodingResult Result { get; }

    [ObservableProperty]
    private int _index;

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => OnPropertyChanged(nameof(SelectedColor));

    public IBrush SelectedColor => IsSelected
        ? new SolidColorBrush(Color.Parse("#4CAF50"))
        : new SolidColorBrush(Color.Parse("#999999"));

    public GeocodingResultViewModel(int index, GeocodingResult result)
    {
        _index = index;
        Result = result;
    }
}

/// <summary>
/// ViewModel for a single location in the geocoding table.
/// Supports multiple results with expand/collapse.
/// </summary>
public partial class GeocodingLocationViewModel : ObservableObject
{
    /// <summary>
    /// Display/execution order index (0-based).
    /// </summary>
    public int Index { get; set; }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private GeocodingStatus _status = GeocodingStatus.Pending;

    partial void OnStatusChanged(GeocodingStatus value)
    {
        OnPropertyChanged(nameof(StatusColor));
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(HasCoordinates));
        OnPropertyChanged(nameof(CoordinateText));
        OnPropertyChanged(nameof(ResultCount));
    }

    [ObservableProperty]
    private double _latitude;

    partial void OnLatitudeChanged(double value)
    {
        OnPropertyChanged(nameof(HasCoordinates));
        OnPropertyChanged(nameof(CoordinateText));
    }

    [ObservableProperty]
    private double _longitude;

    partial void OnLongitudeChanged(double value)
    {
        OnPropertyChanged(nameof(HasCoordinates));
        OnPropertyChanged(nameof(CoordinateText));
    }

    [ObservableProperty]
    private string _displayName = "";

    [ObservableProperty]
    private string _placeType = "";

    [ObservableProperty]
    private double _importance;

    [ObservableProperty]
    private string _error = "";

    [ObservableProperty]
    private string _syntacticRole = "";

    [ObservableProperty]
    private string _matchType = "";

    [ObservableProperty]
    private string _matchedNameTag = "";

    [ObservableProperty]
    private string _matchedName = "";

    [ObservableProperty]
    private bool _matchByHomophone;

    [ObservableProperty]
    private GeocodingFailureType _failureType = GeocodingFailureType.Unknown;

    // Expand/collapse for multiple results
    [ObservableProperty]
    private bool _isExpanded;

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(ExpandIcon));

    public string ExpandIcon => IsExpanded ? "▼" : "▶";

    /// <summary>
    /// All geocoding results for this location (sorted by confidence).
    /// </summary>
    public ObservableCollection<GeocodingResultViewModel> Results { get; }

    /// <summary>
    /// Number of results.
    /// </summary>
    public int ResultCount => Results.Count;

    public GeocodingLocationViewModel(string name)
    {
        _name = name;
        Results = new();
        Results.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ResultCount));
            OnPropertyChanged(nameof(HasMultipleResults));
        };
    }

    /// <summary>
    /// Whether there are multiple results (show expand indicator).
    /// </summary>
    public bool HasMultipleResults => Results.Count > 1;

    [ObservableProperty]
    private int _selectedResultIndex = 0;

    partial void OnSelectedResultIndexChanged(int value) => UpdateFromSelectedResult();

    partial void OnFailureTypeChanged(GeocodingFailureType value)
    {
        OnPropertyChanged(nameof(FailureTypeLabel));
        OnPropertyChanged(nameof(FailureTypeColor));
    }

    /// <summary>
    /// Human-readable label for the failure type.
    /// </summary>
    public string FailureTypeLabel => FailureType switch
    {
        GeocodingFailureType.Transient => "网络错误",
        GeocodingFailureType.Ambiguous => "名称歧义",
        GeocodingFailureType.NotFound => "未找到",
        _ => "未知"
    };

    /// <summary>
    /// Color for the failure type badge.
    /// </summary>
    public IBrush FailureTypeColor => FailureType switch
    {
        GeocodingFailureType.Transient => new SolidColorBrush(Color.Parse("#FF9800")),  // Orange for retryable
        GeocodingFailureType.Ambiguous => new SolidColorBrush(Color.Parse("#2196F3")),   // Blue for disambiguation
        GeocodingFailureType.NotFound => new SolidColorBrush(Color.Parse("#9E9E9E")),    // Gray for permanent
        _ => new SolidColorBrush(Color.Parse("#9E9E9E"))
    };

    /// <summary>
    /// Chinese display name for the syntactic role. Unknown/empty displays as "-".
    /// </summary>
    public string SyntacticRoleDisplay => (SyntacticRole == "" || SyntacticRole == "Unknown")
        ? "-"
        : LocationSyntacticRole.ToDisplay(SyntacticRole);

    /// <summary>
    /// Color for the syntactic role text, per-role coloring for readability on dark background.
    /// </summary>
    public IBrush SyntacticRoleColor => SyntacticRole switch
    {
        "Subject" => new SolidColorBrush(Color.Parse("#CE93D8")),    // 主语 - 亮紫色
        "Attributive" => new SolidColorBrush(Color.Parse("#81C784")), // 定语 - 亮绿色
        "Adverbial" => new SolidColorBrush(Color.Parse("#4FC3F7")),   // 状语 - 亮蓝色
        "Object" => new SolidColorBrush(Color.Parse("#FFB74D")),      // 宾语 - 亮橙色
        "Predicative" => new SolidColorBrush(Color.Parse("#F06292")), // 表语 - 亮粉色
        _ => new SolidColorBrush(Color.Parse("#666666"))              // 未知 - 灰色
    };

    /// <summary>
    /// Narrative description of the spatial function.
    /// </summary>
    public string SyntacticRoleDescription => LocationSyntacticRole.ToNarrativeDescription(SyntacticRole);

    /// <summary>
    /// Color for the status text based on current status.
    /// </summary>
    public IBrush StatusColor => Status switch
    {
        GeocodingStatus.Pending => new SolidColorBrush(Color.Parse("#999999")),
        GeocodingStatus.Processing => new SolidColorBrush(Color.Parse("#2196F3")),
        GeocodingStatus.Success => new SolidColorBrush(Color.Parse("#4CAF50")),
        GeocodingStatus.Failed => new SolidColorBrush(Color.Parse("#F44336")),
        _ => new SolidColorBrush(Color.Parse("#999999"))
    };

    /// <summary>
    /// Human-readable status label.
    /// </summary>
    public string StatusLabel => Status switch
    {
        GeocodingStatus.Pending => "等待中",
        GeocodingStatus.Processing => "处理中",
        GeocodingStatus.Success => "成功",
        GeocodingStatus.Failed => "失败",
        _ => "未知"
    };

    /// <summary>
    /// Whether coordinates are available (from selected result).
    /// </summary>
    public bool HasCoordinates => Status == GeocodingStatus.Success && Latitude != 0 && Longitude != 0;

    /// <summary>
    /// Formatted coordinate string for display.
    /// </summary>
    public string CoordinateText => HasCoordinates 
        ? $"({Latitude:F4}, {Longitude:F4})" 
        : "-";

    /// <summary>
    /// Result count text for display.
    /// </summary>
    public string ResultCountText => ResultCount > 1 ? $"{ResultCount} 个结果" : "";


    /// <summary>
    /// Mark this location as successfully geocoded with multiple results.
    /// </summary>
    public void MarkSuccess(List<GeocodingResult> results)
    {
        Status = GeocodingStatus.Success;
        Error = "";

        // Clear existing results
        Results.Clear();

        // Add all results
        for (int i = 0; i < results.Count; i++)
        {
            var vm = new GeocodingResultViewModel(i, results[i]);
            Results.Add(vm);
        }

        // Auto-select the first (best) result
        SelectedResultIndex = 0;
        UpdateFromSelectedResult();

        // Auto-expand if multiple results
        if (results.Count > 1)
        {
            IsExpanded = false; // Default collapsed, user can expand
        }
    }

    /// <summary>
    /// Update display fields from the selected result.
    /// </summary>
    private void UpdateFromSelectedResult()
    {
        if (SelectedResultIndex >= 0 && SelectedResultIndex < Results.Count)
        {
            var result = Results[SelectedResultIndex].Result;
            Latitude = result.Latitude;
            Longitude = result.Longitude;
            DisplayName = result.DisplayName;
            PlaceType = string.IsNullOrEmpty(result.Type) ? result.Class : result.Type;
            Importance = result.Importance;
            MatchType = result.MatchType;
            MatchedNameTag = result.MatchedNameTag;
            MatchedName = result.MatchedName;
            MatchByHomophone = result.MatchByHomophone;
        }
    }

    /// <summary>
    /// Mark this location as failed.
    /// </summary>
    public void MarkFailed(string error, GeocodingFailureType failureType = GeocodingFailureType.Unknown)
    {
        Status = GeocodingStatus.Failed;
        Error = error;
        FailureType = failureType;
    }

    /// <summary>
    /// Mark this location as currently processing.
    /// </summary>
    public void MarkProcessing()
    {
        Status = GeocodingStatus.Processing;
    }

    [RelayCommand]
    private void ToggleExpanded()
    {
        IsExpanded = !IsExpanded;
    }
}
