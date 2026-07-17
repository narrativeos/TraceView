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

using Avalonia.Collections;
using Avalonia.Threading;
using Caly.Core.Models;
using Caly.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Caly.Core.ViewModels;

public sealed partial class DocumentViewModel
{
    #region Map Properties

    /// <summary>
    /// Whether the Map column is visible (user toggle).
    /// </summary>
    [ObservableProperty]
    private bool _showMapColumn = false;

    /// <summary>
    /// Path to the generated GeoJSON file for this document.
    /// </summary>
    [ObservableProperty]
    private string? _geoJsonFilePath;

    /// <summary>
    /// Whether GeoJSON file exists and is available.
    /// </summary>
    public bool HasGeoJsonFile => !string.IsNullOrEmpty(GeoJsonFilePath) && File.Exists(GeoJsonFilePath);

    /// <summary>
    /// Map processing status.
    /// </summary>
    [ObservableProperty]
    private MapProcessStatus _mapStatus = MapProcessStatus.Idle;

    [ObservableProperty]
    private string _mapStatusText = "Ready";

    [ObservableProperty]
    private bool _isMapProcessing;

    /// <summary>
    /// Collection of locations being geocoded (for table display).
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<GeocodingLocationViewModel> _geocodingLocations = new();

    partial void OnGeocodingLocationsChanged(ObservableCollection<GeocodingLocationViewModel> value)
    {
        // Subscribe to collection changes to notify dependent properties
        if (_geocodingLocations != null)
            _geocodingLocations.CollectionChanged -= OnGeocodingLocationsCollectionChanged;
        if (_geocodingLocations != null)
            _geocodingLocations.CollectionChanged += OnGeocodingLocationsCollectionChanged;
        
        OnPropertyChanged(nameof(GeocodingTotalCount));
        OnPropertyChanged(nameof(GeocodingSuccessCount));
        OnPropertyChanged(nameof(GeocodingFailedCount));
        OnPropertyChanged(nameof(GeocodingProgressText));
    }

    private void OnGeocodingLocationsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(GeocodingTotalCount));
        OnPropertyChanged(nameof(GeocodingSuccessCount));
        OnPropertyChanged(nameof(GeocodingFailedCount));
        OnPropertyChanged(nameof(GeocodingProgressText));
    }

    /// <summary>
    /// Number of locations that have been successfully geocoded.
    /// </summary>
    public int GeocodingSuccessCount => GeocodingLocations.Count(l => l.Status == GeocodingStatus.Success);

    /// <summary>
    /// Number of locations that have failed.
    /// </summary>
    public int GeocodingFailedCount => GeocodingLocations.Count(l => l.Status == GeocodingStatus.Failed);

    /// <summary>
    /// Total number of locations.
    /// </summary>
    public int GeocodingTotalCount
    {
        get => _geocodingLocations.Count;
    }

    /// <summary>
    /// Progress summary text like "3/10 completed".
    /// </summary>
    public string GeocodingProgressText
    {
        get
        {
            var done = GeocodingSuccessCount + GeocodingFailedCount;
            var total = GeocodingTotalCount;
            if (total == 0)
                return "Ready";
            if (done < total)
                return $"处理中: {done}/{total}";
            return $"已完成: {GeocodingSuccessCount}/{total} 成功";
        }
    }

    /// <summary>
    /// Whether map GeoJSON generation has been completed successfully.
    /// </summary>
    public bool IsMapCompleted => MapStatus == MapProcessStatus.Completed;

    /// <summary>
    /// Whether map GeoJSON generation has failed.
    /// </summary>
    public bool IsMapFailed => MapStatus == MapProcessStatus.Failed;

    /// <summary>
    /// Human-readable status for the Map button tooltip.
    /// </summary>
    public string MapButtonTooltip
    {
        get
        {
            return MapStatus switch
            {
                MapProcessStatus.Completed => "地图已生成 · 点击重新生成",
                MapProcessStatus.Failed => "地图生成失败 · 点击重试",
                MapProcessStatus.Processing => $"地图生成中...",
                _ => "生成地理地图"
            };
        }
    }

    private CancellationTokenSource? _mapCts;

    #endregion

    #region Map Commands

    [RelayCommand]
    private void ToggleMapColumn()
    {
        ShowMapColumn = !ShowMapColumn;
    }

    [RelayCommand]
    private async Task GenerateMapGeoJsonAsync()
    {
        if (SemanticResults is null || !HasSemanticResults)
        {
            MapStatus = MapProcessStatus.Failed;
            MapStatusText = "No semantic data available. Run NLP analysis first.";
            return;
        }

        // If already processing, cancel
        if (IsMapProcessing)
        {
            _mapCts?.Cancel();
            MapStatusText = "Cancelling...";
            return;
        }

        _mapCts = new CancellationTokenSource();
        IsMapProcessing = true;
        MapStatus = MapProcessStatus.Processing;
        MapStatusText = "Generating GeoJSON...";
        // Ensure Map column is visible
        ShowMapColumn = true;

        try
        {
            var outputDir = GetSemanticOutputDir();
            var geoJsonService = new GeoJsonService();

            // Collect all unique location names
            var locationNames = new HashSet<string>();
            foreach (var block in SemanticResults.Blocks)
            {
                foreach (var entity in block.LocationEntities)
                {
                    locationNames.Add(entity.Text);
                }
            }

            // Create location ViewModels in UI thread
            var locationVms = new ObservableCollection<GeocodingLocationViewModel>();
            foreach (var name in locationNames)
            {
                locationVms.Add(new GeocodingLocationViewModel(name));
            }

            // Build a name -> ViewModel lookup for callbacks
            var vmLookup = new Dictionary<string, GeocodingLocationViewModel>();
            foreach (var vm in locationVms)
            {
                vmLookup[vm.Name] = vm;
            }

            // Set the collection immediately (before await) to trigger UI binding
            GeocodingLocations = locationVms;

            // Callback for when a location starts processing
            Action<string> onProcessing = (name) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (vmLookup.TryGetValue(name, out var vm))
                    {
                        vm.MarkProcessing();
                    }
                    MapStatusText = $"Geocoding: {name} ({vmLookup.Values.Count(v => v.Status != GeocodingStatus.Pending)}/{vmLookup.Count})";
                });
            };

            // Callback for when a location succeeds
            Action<GeocodingResult> onSuccess = (result) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (vmLookup.TryGetValue(result.Name, out var vm))
                    {
                        vm.MarkSuccess(result);
                    }
                    var done = vmLookup.Values.Count(v => v.Status == GeocodingStatus.Success || v.Status == GeocodingStatus.Failed);
                    MapStatusText = $"Geocoding: {result.Name} ({done}/{vmLookup.Count})";
                });
            };

            // Callback for when a location fails
            Action<string, string> onFailed = (name, error) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (vmLookup.TryGetValue(name, out var vm))
                    {
                        vm.MarkFailed(error);
                    }
                    var done = vmLookup.Values.Count(v => v.Status == GeocodingStatus.Success || v.Status == GeocodingStatus.Failed);
                    MapStatusText = $"Geocoding: {name} ({done}/{vmLookup.Count})";
                });
            };

            // Generate GeoJSON file
            var filePath = await geoJsonService.GenerateGeoJsonAsync(
                SemanticResults,
                outputDir,
                onProcessing,
                onSuccess,
                onFailed,
                _mapCts.Token);

            if (filePath is not null)
            {
                GeoJsonFilePath = filePath;
                MapStatus = MapProcessStatus.Completed;
                MapStatusText = GeocodingProgressText;
                ShowMapColumn = true;
            }
            else
            {
                MapStatus = MapProcessStatus.Failed;
                var totalLocs = locationNames.Count;
                if (totalLocs > 0)
                {
                    MapStatusText = $"Found {totalLocs} location(s) but geocoding failed for all. Check network or try again.";
                }
                else
                {
                    MapStatusText = "No location entities found in semantic data.";
                }
            }
        }
        catch (OperationCanceledException)
        {
            MapStatus = MapProcessStatus.Idle;
            MapStatusText = "Generation cancelled";
        }
        catch (Exception ex)
        {
            MapStatus = MapProcessStatus.Failed;
            MapStatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsMapProcessing = false;
            _mapCts?.Dispose();
            _mapCts = null;
        }
    }

    [RelayCommand]
    private void CancelMapProcess()
    {
        _mapCts?.Cancel();
    }

    [RelayCommand]
    private void OpenInGeoLibre()
    {
        if (string.IsNullOrEmpty(GeoJsonFilePath) || !File.Exists(GeoJsonFilePath))
            return;

        // Get GeoLibre base URL from settings
        var geoLibreBaseUrl = _settingsService.GetSettings().GeoLibreBaseUrl;
        var fileUri = new Uri(GeoJsonFilePath).AbsoluteUri;
        var url = $"{geoLibreBaseUrl.TrimEnd('/')}?url={Uri.EscapeDataString(fileUri)}";

        // Open in default browser
        try
        {
#if WINDOWS
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
#elif MACOS
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "open",
                Arguments = url,
                UseShellExecute = false
            });
#elif LINUX
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "xdg-open",
                Arguments = url,
                UseShellExecute = false
            });
#else
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
#endif
        }
        catch (Exception ex)
        {
            MapStatusText = $"Failed to open: {ex.Message}";
        }
    }

    #endregion

    #region Map Data Loading

    /// <summary>
    /// Tries to load existing GeoJSON file path when document is opened.
    /// </summary>
    internal void TryLoadMapData()
    {
        try
        {
            var outputDir = GetSemanticOutputDir();
            var geoJsonPath = Path.Combine(outputDir, "locations.geojson");

            if (File.Exists(geoJsonPath))
            {
                GeoJsonFilePath = geoJsonPath;
                MapStatus = MapProcessStatus.Completed;
                MapStatusText = $"Loaded from cache: {Path.GetFileName(geoJsonPath)}";
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Map] TryLoadMapData ERROR: {ex.Message}");
        }
    }

    #endregion
}

/// <summary>
/// Map generation processing status.
/// </summary>
public enum MapProcessStatus
{
    Idle,
    Processing,
    Completed,
    Failed,
}