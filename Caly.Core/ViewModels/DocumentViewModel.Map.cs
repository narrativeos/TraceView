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
    /// Path to the generated GeoLibre JSON file for this document.
    /// </summary>
    [ObservableProperty]
    private string? _geoLibreFilePath;

    /// <summary>
    /// Whether GeoLibre JSON file exists and is available.
    /// </summary>
    public bool HasGeoLibreFile => !string.IsNullOrEmpty(GeoLibreFilePath) && File.Exists(GeoLibreFilePath);

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

    /// <summary>
    /// Human-readable status for the GeoLibre Export button tooltip.
    /// </summary>
    public string ExportGeoLibreButtonTooltip
    {
        get
        {
            if (HasGeoLibreFile)
                return "GeoLibre JSON 已导出 · 点击重新导出";
            if (_geoLibreCts is not null)
                return "导出 GeoLibre JSON 中...";
            return "导出 GeoLibre JSON";
        }
    }

    private CancellationTokenSource? _mapCts;
    private CancellationTokenSource? _geoLibreCts;

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

    [RelayCommand]
    private async Task ExportGeoLibreJsonAsync()
    {
        if (SemanticResults is null || !HasSemanticResults)
        {
            MapStatus = MapProcessStatus.Failed;
            MapStatusText = "No semantic data available. Run NLP analysis first.";
            return;
        }

        // If already processing, cancel
        if (_geoLibreCts is not null)
        {
            _geoLibreCts?.Cancel();
            MapStatusText = "Cancelling...";
            return;
        }

        // Check if cached GeoLibre file exists in the semantic output directory
        var outputDir = GetSemanticOutputDir();
        var cachedGeoLibrePath = Path.Combine(outputDir, "locations.geolibre.json");
        
        string? jsonContent = null;
        
        if (File.Exists(cachedGeoLibrePath))
        {
            // Use cached file directly - no need to regenerate
            MapStatus = MapProcessStatus.Completed;
            MapStatusText = "使用缓存文件...";
            jsonContent = await File.ReadAllTextAsync(cachedGeoLibrePath);
        }
        else
        {
            // Need to generate
            _geoLibreCts = new CancellationTokenSource();
            MapStatus = MapProcessStatus.Processing;
            MapStatusText = "Generating GeoLibre JSON...";
            ShowMapColumn = true;

            try
            {
                var geoJsonService = new GeoJsonService();

                // Generate GeoLibre JSON string (without writing to file)
                jsonContent = await geoJsonService.GenerateGeoLibreJsonStringAsync(
                    SemanticResults,
                    _geoLibreCts.Token);

                if (jsonContent is not null)
                {
                    // Also save to cache for next time
                    Directory.CreateDirectory(outputDir);
                    await File.WriteAllTextAsync(cachedGeoLibrePath, jsonContent, _geoLibreCts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                MapStatus = MapProcessStatus.Idle;
                MapStatusText = "Export cancelled";
                _geoLibreCts?.Dispose();
                _geoLibreCts = null;
                return;
            }
            catch (Exception ex)
            {
                MapStatus = MapProcessStatus.Failed;
                MapStatusText = $"Export error: {ex.Message}";
                _geoLibreCts?.Dispose();
                _geoLibreCts = null;
                return;
            }
            finally
            {
                _geoLibreCts?.Dispose();
                _geoLibreCts = null;
            }
        }

        if (jsonContent is null)
        {
            MapStatus = MapProcessStatus.Failed;
            MapStatusText = "No location entities found in semantic data.";
            return;
        }

        MapStatusText = "请选择保存位置...";

        // Show "Save As" dialog
        var fileName = FileName?.Replace(".pdf", "") ?? "document";
        var bytes = System.Text.Encoding.UTF8.GetBytes(jsonContent);
        
        var savedFile = await _filesService.SaveFileAsync(bytes, $"{fileName}.geolibre.json");

        if (savedFile is not null)
        {
            // Get the file path from the storage file
            var savedPath = savedFile.Path.LocalPath;
            // Do NOT update GeoLibreFilePath - keep it pointing to the cache path
            MapStatus = MapProcessStatus.Completed;
            if (File.Exists(cachedGeoLibrePath))
            {
                MapStatusText = $"GeoLibre JSON 已保存到: {System.IO.Path.GetFileName(savedPath)} (使用缓存)";
            }
            else
            {
                MapStatusText = $"GeoLibre JSON 已保存到: {System.IO.Path.GetFileName(savedPath)}";
            }
            ShowMapColumn = true;
        }
        else
        {
            // User cancelled the save dialog
            MapStatus = MapProcessStatus.Idle;
            MapStatusText = "Export cancelled";
        }
    }

    [RelayCommand]
    private void OpenGeoLibreFile()
    {
        // Prefer GeoLibre JSON file if available, fallback to GeoJSON
        string? filePath = null;
        if (!string.IsNullOrEmpty(GeoLibreFilePath) && File.Exists(GeoLibreFilePath))
        {
            filePath = GeoLibreFilePath;
        }
        else if (!string.IsNullOrEmpty(GeoJsonFilePath) && File.Exists(GeoJsonFilePath))
        {
            filePath = GeoJsonFilePath;
        }

        if (string.IsNullOrEmpty(filePath))
            return;

        // Get GeoLibre base URL from settings
        var geoLibreBaseUrl = _settingsService.GetSettings().GeoLibreBaseUrl;
        var fileUri = new Uri(filePath).AbsoluteUri;
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
    /// Parses the GeoJSON file to populate the geocoding locations table.
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
                ShowMapColumn = true;
                
                // Parse the GeoJSON file to populate the locations table
                var content = File.ReadAllText(geoJsonPath);
                var locations = new ObservableCollection<GeocodingLocationViewModel>();
                
                // Parse features from GeoJSON (simple string-based parsing to avoid AOT issues)
                // Format: "features": [ { "type":"Feature", "properties":{...}, "geometry":{...} } ]
                var featuresStart = content.IndexOf("\"features\"");
                if (featuresStart >= 0)
                {
                    // Find the array after "features":
                    var arrayStart = content.IndexOf('[', featuresStart);
                    if (arrayStart >= 0)
                    {
                        // Extract each feature object by counting braces
                        int pos = arrayStart + 1;
                        while (pos < content.Length)
                        {
                            // Skip whitespace and commas
                            while (pos < content.Length && (char.IsWhiteSpace(content[pos]) || content[pos] == ',' || content[pos] == '\n' || content[pos] == '\r' || content[pos] == ' '))
                                pos++;
                            
                            if (content[pos] == ']')
                                break;
                            
                            if (content[pos] == '{')
                            {
                                // Find matching closing brace
                                int depth = 0;
                                int featureStart = pos;
                                while (pos < content.Length)
                                {
                                    if (content[pos] == '{') depth++;
                                    if (content[pos] == '}') depth--;
                                    pos++;
                                    if (depth == 0) break;
                                }
                                
                                string feature = content.Substring(featureStart, pos - featureStart);
                                
                                // Extract name and display_name from "properties" object
                                var propertiesStart = feature.IndexOf("\"properties\"");
                                string? name = null;
                                string? displayName = null;
                                string? type = null;
                                
                                if (propertiesStart >= 0)
                                {
                                    var propObjStart = feature.IndexOf('{', propertiesStart);
                                    if (propObjStart >= 0)
                                    {
                                        int propDepth = 0;
                                        int propObjEnd = propObjStart;
                                        while (propObjEnd < feature.Length)
                                        {
                                            if (feature[propObjEnd] == '{') propDepth++;
                                            if (feature[propObjEnd] == '}') propDepth--;
                                            propObjEnd++;
                                            if (propDepth == 0) break;
                                        }
                                        string propertiesObj = feature.Substring(propObjStart, propObjEnd - propObjStart);
                                        name = ExtractJsonStringValue(propertiesObj, "name");
                                        displayName = ExtractJsonStringValue(propertiesObj, "display_name");
                                        type = ExtractJsonStringValue(propertiesObj, "type") ?? ExtractJsonStringValue(propertiesObj, "class");
                                    }
                                }
                                
                                // Extract coordinates from "geometry": { "type": "Point", "coordinates": [lon, lat] }
                                // GeoJSON coordinates are [longitude, latitude]
                                double? lat = null;
                                double? lon = null;
                                var geometryIdx = feature.IndexOf("\"geometry\"");
                                if (geometryIdx >= 0)
                                {
                                    var geomObjStart = feature.IndexOf('{', geometryIdx);
                                    if (geomObjStart >= 0)
                                    {
                                        int geomDepth = 0;
                                        int geomObjEnd = geomObjStart;
                                        while (geomObjEnd < feature.Length)
                                        {
                                            if (feature[geomObjEnd] == '{') geomDepth++;
                                            if (feature[geomObjEnd] == '}') geomDepth--;
                                            geomObjEnd++;
                                            if (geomDepth == 0) break;
                                        }
                                        string geometryObj = feature.Substring(geomObjStart, geomObjEnd - geomObjStart);
                                        // Extract "coordinates": [lon, lat]
                                        var coordsIdx = geometryObj.IndexOf("\"coordinates\"");
                                        if (coordsIdx >= 0)
                                        {
                                            var arrayIdx = geometryObj.IndexOf('[', coordsIdx);
                                            if (arrayIdx >= 0)
                                            {
                                                var closeBracket = geometryObj.IndexOf(']', arrayIdx);
                                                if (closeBracket > arrayIdx)
                                                {
                                                    string coordsStr = geometryObj.Substring(arrayIdx + 1, closeBracket - arrayIdx - 1);
                                                    var parts = coordsStr.Split(',').Select(s => s.Trim()).ToArray();
                                                    if (parts.Length >= 2 && double.TryParse(parts[0], out var lonVal) && double.TryParse(parts[1], out var latVal))
                                                    {
                                                        lon = lonVal;  // GeoJSON: [longitude, latitude]
                                                        lat = latVal;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                                
                                if (!string.IsNullOrEmpty(name))
                                {
                                    var vm = new GeocodingLocationViewModel(name);
                                    vm.DisplayName = displayName ?? name;
                                    vm.PlaceType = type ?? "";
                                    
                                    if (lat.HasValue && lon.HasValue)
                                    {
                                        vm.Status = GeocodingStatus.Success;
                                        vm.Latitude = lat.Value;
                                        vm.Longitude = lon.Value;
                                    }
                                    
                                    locations.Add(vm);
                                }
                            }
                        }
                    }
                }
                
                GeocodingLocations = locations;
                MapStatusText = locations.Count > 0 
                    ? $"Loaded {locations.Count} locations from cache" 
                    : "GeoJSON loaded (no locations)";
                
                System.Diagnostics.Debug.WriteLine($"[Map] TryLoadMapData: loaded {locations.Count} locations from {geoJsonPath}");
            }

            // Also check for existing GeoLibre JSON file
            var geoLibrePath = Path.Combine(outputDir, "locations.geolibre.json");
            if (File.Exists(geoLibrePath))
            {
                GeoLibreFilePath = geoLibrePath;
                System.Diagnostics.Debug.WriteLine($"[Map] TryLoadMapData: found GeoLibre file {geoLibrePath}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Map] TryLoadMapData ERROR: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Extract a string value for a given key from a JSON string (simple parsing).
    /// </summary>
    static string? ExtractJsonStringValue(string json, string key)
    {
        var searchKey = "\"" + key + "\"";
        int idx = json.IndexOf(searchKey);
        if (idx < 0) return null;
        
        // Find the colon after the key
        idx = json.IndexOf(':', idx + searchKey.Length);
        if (idx < 0) return null;
        
        // Skip whitespace
        idx++;
        while (idx < json.Length && char.IsWhiteSpace(json[idx])) idx++;
        
        if (idx >= json.Length) return null;
        
        // Check for null
        if (json.Substring(idx, 4) == "null") return null;
        
        // Expect a quoted string
        if (json[idx] != '"') return null;
        
        // Find the closing quote (handle escaped quotes)
        int start = idx + 1;
        int end = start;
        while (end < json.Length)
        {
            if (json[end] == '\\' && end + 1 < json.Length)
            {
                end += 2; // skip escaped character
                continue;
            }
            if (json[end] == '"')
                break;
            end++;
        }
        
        return end > start ? json.Substring(start, end - start).Replace("\\\"", "\"") : null;
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