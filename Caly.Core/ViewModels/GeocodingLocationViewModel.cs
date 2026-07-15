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

using Avalonia.Media;
using Caly.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;

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
/// ViewModel for a single location in the geocoding table.
/// </summary>
public partial class GeocodingLocationViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private GeocodingStatus _status = GeocodingStatus.Pending;

    partial void OnStatusChanged(GeocodingStatus value)
    {
        // Notify dependent computed properties
        OnPropertyChanged(nameof(StatusColor));
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(HasCoordinates));
        OnPropertyChanged(nameof(CoordinateText));
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
    /// Whether coordinates are available.
    /// </summary>
    public bool HasCoordinates => Status == GeocodingStatus.Success && Latitude != 0 && Longitude != 0;

    /// <summary>
    /// Formatted coordinate string for display.
    /// </summary>
    public string CoordinateText => HasCoordinates 
        ? $"({Latitude:F4}, {Longitude:F4})" 
        : "-";

    public GeocodingLocationViewModel(string name)
    {
        _name = name;
    }

    /// <summary>
    /// Mark this location as successfully geocoded with full result.
    /// </summary>
    public void MarkSuccess(GeocodingResult result)
    {
        Status = GeocodingStatus.Success;
        Latitude = result.Latitude;
        Longitude = result.Longitude;
        DisplayName = result.DisplayName;
        PlaceType = string.IsNullOrEmpty(result.Type) ? result.Class : result.Type;
        Importance = result.Importance;
        Error = "";
    }

    /// <summary>
    /// Mark this location as failed.
    /// </summary>
    public void MarkFailed(string error)
    {
        Status = GeocodingStatus.Failed;
        Error = error;
    }

    /// <summary>
    /// Mark this location as currently processing.
    /// </summary>
    public void MarkProcessing()
    {
        Status = GeocodingStatus.Processing;
    }
}