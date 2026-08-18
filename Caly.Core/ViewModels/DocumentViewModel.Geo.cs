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
using Caly.Core.Services.Interfaces;
using Caly.Core.Utilities;
using Caly.Core.Views;
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
    /// <summary>
    /// Gets the best available document title for story maps.
    /// Priority: Popo tree root title > PDF filename > fallback default.
    /// </summary>
    string GetBestDocumentTitle()
    {
        // 1. Popo tree root node title (most meaningful, from document content)
        if (!string.IsNullOrEmpty(StructureDocument?.TreeRoot?.Title)
            && StructureDocument!.TreeRoot!.Title != "Default Title"
            && StructureDocument.TreeRoot.Title.Trim().Length > 1)
        {
            return StructureDocument.TreeRoot.Title.Trim();
        }

        // 2. PDF filename (without extension)
        if (!string.IsNullOrEmpty(FileName))
        {
            var name = FileName.Replace(".pdf", "");
            if (name.Trim().Length > 0)
                return name.Trim();
        }

        // 3. Fallback default
        return "Document Story";
    }

    #region Geo Properties

    /// <summary>
    /// Whether the Geo column is visible (user toggle).
    /// </summary>
    [ObservableProperty]
    private bool _showGeoColumn = false;

    /// <summary>
    /// Path to the generated GeoJSON file for this document.
    /// </summary>
    [ObservableProperty]
    private string? _geoJsonFilePath;

    /// <summary>
    /// Whether GeoJSON file exists and is available.
    /// </summary>
    public bool HasGeoJsonFile => !string.IsNullOrEmpty(GeoJsonFilePath) && File.Exists(GeoJsonFilePath);

    partial void OnGeoJsonFilePathChanged(string? value)
    {
        OnPropertyChanged(nameof(HasGeoJsonFile));
        OnPropertyChanged(nameof(CanEnrich));
    }

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
    /// Geo processing status.
    /// </summary>
    [ObservableProperty]
    private GeoProcessStatus _geoStatus = GeoProcessStatus.Idle;

    partial void OnGeoStatusChanged(GeoProcessStatus value)
    {
        OnPropertyChanged(nameof(IsGeoCompleted));
        OnPropertyChanged(nameof(IsGeoFailed));
        OnPropertyChanged(nameof(ShowGeoPlaceholder));
        OnPropertyChanged(nameof(GeoButtonTooltip));
    }

    [ObservableProperty]
    private string _geoStatusText = "Ready";

    [ObservableProperty]
    private bool _isGeoProcessing;

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
        OnPropertyChanged(nameof(ShowGeoPlaceholder));
    }

    private void OnGeocodingLocationsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(GeocodingTotalCount));
        OnPropertyChanged(nameof(GeocodingSuccessCount));
        OnPropertyChanged(nameof(GeocodingFailedCount));
        OnPropertyChanged(nameof(GeocodingProgressText));
        OnPropertyChanged(nameof(ShowGeoPlaceholder));
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
        get => GeocodingLocations.Count;
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
    /// Whether geo processing has been completed successfully.
    /// </summary>
    public bool IsGeoCompleted => GeoStatus == GeoProcessStatus.Completed;

    /// <summary>
    /// Whether geo processing has failed.
    /// </summary>
    public bool IsGeoFailed => GeoStatus == GeoProcessStatus.Failed;

    /// <summary>
    /// Whether to show the empty placeholder (no locations, not processing, not failed).
    /// </summary>
    public bool ShowGeoPlaceholder
    {
        get
        {
            if (GeocodingTotalCount > 0)
                return false;
            if (GeoStatus == GeoProcessStatus.Processing)
                return false;
            if (GeoStatus == GeoProcessStatus.Failed)
                return false;
            if (GeoStatus == GeoProcessStatus.Completed)
                return false;
            return true;
        }
    }

    /// <summary>
    /// Human-readable status for the Geo button tooltip.
    /// </summary>
    public string GeoButtonTooltip
    {
        get
        {
            return GeoStatus switch
            {
                GeoProcessStatus.Completed => "地理数据已生成 · 点击重新生成",
                GeoProcessStatus.Failed => "地理数据生成失败 · 点击重试",
                GeoProcessStatus.Processing => $"地理数据生成中...",
                _ => "生成地理数据"
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

    private CancellationTokenSource? _geoCts;
    private CancellationTokenSource? _geoLibreCts;

    /// <summary>
    /// Opens the GeoLibre file in the GeoLibre release app (/Applications/GeoLibre Desktop.app).
    /// </summary>
    public void OpenInGeoLibreRelease(string jsonContent)
    {
        var outputDir = GetSemanticOutputDir();
        var filePath = Path.Combine(outputDir, "locations.geolibre.json");

        try
        {
            Directory.CreateDirectory(outputDir);
            File.WriteAllText(filePath, jsonContent);
            GeoLibreFilePath = filePath;

            if (OperatingSystem.IsMacOS())
            {
                var releasePath = "/Applications/GeoLibre Desktop.app/Contents/MacOS/geolibre-desktop";
                System.Diagnostics.Debug.WriteLine($"[GeoLibre] Opening with release app: {releasePath} {filePath}");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = releasePath,
                    Arguments = filePath,
                    UseShellExecute = false
                });
            }
            else if (OperatingSystem.IsWindows())
            {
                System.Diagnostics.Debug.WriteLine($"[GeoLibre] Opening on Windows: geolibre {filePath}");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "geolibre",
                    Arguments = filePath,
                    UseShellExecute = true
                });
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[GeoLibre] Opening on Linux/other: geolibre {filePath}");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "geolibre",
                    Arguments = filePath,
                    UseShellExecute = false
                });
            }

            GeoStatus = GeoProcessStatus.Completed;
            GeoStatusText = $"已在 GeoLibre 正式版中打开";
            ShowGeoColumn = true;
        }
        catch (Exception ex)
        {
            GeoStatus = GeoProcessStatus.Failed;
            GeoStatusText = $"Failed to open in GeoLibre: {ex.Message}";
        }
    }

#if DEBUG
    /// <summary>
    /// Opens the GeoLibre file in the GeoLibre debug build. Only available in Debug builds.
    /// </summary>
    public void OpenInGeoLibreDebug(string jsonContent)
    {
        var outputDir = GetSemanticOutputDir();
        var filePath = Path.Combine(outputDir, "locations.geolibre.json");

        try
        {
            Directory.CreateDirectory(outputDir);
            File.WriteAllText(filePath, jsonContent);
            GeoLibreFilePath = filePath;

            if (OperatingSystem.IsMacOS())
            {
                var debugPath = "/Users/futuremeng/github/narrativeos/GeoLibre/apps/geolibre-desktop/src-tauri/target/debug/geolibre-desktop";
                System.Diagnostics.Debug.WriteLine($"[GeoLibre] Opening with debug build: {debugPath} {filePath}");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = debugPath,
                    Arguments = filePath,
                    UseShellExecute = false
                });
            }
            else
            {
                GeoStatus = GeoProcessStatus.Failed;
                GeoStatusText = "调试版仅支持 macOS。";
                return;
            }

            GeoStatus = GeoProcessStatus.Completed;
            GeoStatusText = $"已在 GeoLibre 调试版中打开";
            ShowGeoColumn = true;
        }
        catch (Exception ex)
        {
            GeoStatus = GeoProcessStatus.Failed;
            GeoStatusText = $"Failed to open in GeoLibre Debug: {ex.Message}";
        }
    }
#endif

    [RelayCommand]
    private async Task GenerateGeoJsonAsync()
    {
        if (SemanticResults is null || !HasSemanticResults)
        {
            GeoStatus = GeoProcessStatus.Failed;
            GeoStatusText = "No semantic data available. Run NLP analysis first.";
            return;
        }

        // If already processing, cancel
        if (IsGeoProcessing)
        {
            _geoCts?.Cancel();
            GeoStatusText = "Cancelling...";
            return;
        }

        _geoCts = new CancellationTokenSource();
        IsGeoProcessing = true;
        GeoStatus = GeoProcessStatus.Processing;
        GeoStatusText = "提取地点清单...";
        // Ensure Geo column is visible
        ShowGeoColumn = true;

        // Declare vmLookup outside try block so it's accessible in catch blocks
        var vmLookup = new Dictionary<string, GeocodingLocationViewModel>();

        try
        {
            var outputDir = GetSemanticOutputDir();
            var geoJsonService = new GeoJsonService();

            // Step 1: Collect all unique location entities from semantic results
            // Apply LocationValidator to filter out clearly invalid locations
            var locationByName = new Dictionary<string, SemanticEntity>();
            var ambiguousNames = new HashSet<string>();
            var filteredLocations = new List<(string Name, string Reason)>();
            foreach (var block in SemanticResults.Blocks)
            {
                foreach (var entity in block.LocationEntities)
                {
                    if (locationByName.ContainsKey(entity.Text))
                        continue;

                    // Phase 1: Validate location name before including it
                    var (result, reason) = LocationValidator.Validate(entity.Text);
                    switch (result)
                    {
                        case LocationValidationResult.Invalid:
                            filteredLocations.Add((entity.Text, reason));
                            System.Diagnostics.Debug.WriteLine(
                                $"[Geo] Filtered INVALID location: '{entity.Text}' — {reason}");
                            continue;
                        case LocationValidationResult.Ambiguous:
                            // Include but mark as ambiguous
                            locationByName[entity.Text] = entity;
                            ambiguousNames.Add(entity.Text);
                            System.Diagnostics.Debug.WriteLine(
                                $"[Geo] Marked AMBIGUOUS location: '{entity.Text}' — {reason}");
                            break;
                        default:
                            locationByName[entity.Text] = entity;
                            break;
                    }
                }
            }

            if (filteredLocations.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Geo] Filtered {filteredLocations.Count} invalid location(s): " +
                    string.Join(", ", filteredLocations.Select(f => $"'{f.Name}'({f.Reason})")));
            }

            // Step 2: Create location ViewModels with syntactic role info from semantic analysis
            var locationVms = new ObservableCollection<GeocodingLocationViewModel>();
            int index = 0;
            foreach (var kvp in locationByName)
            {
                var vm = new GeocodingLocationViewModel(kvp.Key);
                vm.Index = index++;
                vm.SyntacticRole = kvp.Value.SyntacticRole;
                vm.IsAmbiguous = ambiguousNames.Contains(kvp.Key);
                vm.LocationType = LocationValidator.ClassifyType(kvp.Key);
                locationVms.Add(vm);
            }

            // Build a name -> ViewModel lookup for callbacks
            foreach (var vm in locationVms)
            {
                vmLookup[vm.Name] = vm;
            }

            // Display locations with syntactic roles immediately (before calling geo API)
            GeocodingLocations = locationVms;
            GeoStatusText = $"已提取 {locationByName.Count} 个地点，开始获取坐标...";

            // Capture the UI display order so geocoding processes in the same order
            var uiLocationOrder = locationVms.Select(vm => vm.Name).ToList();

            // Callbacks are now called from background threads (parallel processing),
            // so they must be dispatched to the UI thread for thread-safe ViewModel updates.
            Action<string> onProcessing = (name) =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (vmLookup.TryGetValue(name, out var vm))
                    {
                        vm.MarkProcessing();
                    }
                });
            };

            GeocodingService.GeocodingSuccessCallback onSuccess = (name, results) =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (vmLookup.TryGetValue(name, out var vm))
                    {
                        vm.MarkSuccess(results);
                    }
                    var done = vmLookup.Values.Count(v => v.Status == GeocodingStatus.Success || v.Status == GeocodingStatus.Failed);
                    GeoStatusText = $"{done}/{vmLookup.Count} ({name})";
                });
            };

            GeocodingService.GeocodingFailedCallback onFailed = (name, error, failureType) =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (vmLookup.TryGetValue(name, out var vm))
                    {
                        vm.MarkFailed(error, failureType);
                    }
                    var done = vmLookup.Values.Count(v => v.Status == GeocodingStatus.Success || v.Status == GeocodingStatus.Failed);
                    GeoStatusText = $"{done}/{vmLookup.Count} ({name})";
                });
            };

            // Generate GeoJSON file (pass UI order to ensure sequential processing matches UI)
            var filePath = await geoJsonService.GenerateGeoJsonAsync(
                SemanticResults,
                outputDir,
                onProcessing,
                onSuccess,
                onFailed,
                uiLocationOrder,
                _geoCts.Token);

            // Mark any locations still in "Processing" state as failed (timeout or interrupted)
            foreach (var vm in vmLookup.Values)
            {
                if (vm.Status == GeocodingStatus.Processing)
                {
                    vm.MarkFailed("Timeout or processing interrupted", GeocodingFailureType.Transient);
                }
            }

            if (filePath is not null)
            {
                GeoJsonFilePath = filePath;
                GeoStatus = GeoProcessStatus.Completed;
                GeoStatusText = GeocodingProgressText;
                ShowGeoColumn = true;
            }
            else
            {
                GeoStatus = GeoProcessStatus.Failed;
                var totalLocs = locationByName.Count;
                if (totalLocs > 0)
                {
                    GeoStatusText = $"Found {totalLocs} location(s) but geocoding failed for all. Check network or try again.";
                }
                else
                {
                    GeoStatusText = "No location entities found in semantic data.";
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Mark any locations still in "Processing" state as failed
            foreach (var vm in vmLookup.Values)
            {
                if (vm.Status == GeocodingStatus.Processing)
                {
                    vm.MarkFailed("Cancelled", GeocodingFailureType.Transient);
                }
            }
            GeoStatus = GeoProcessStatus.Idle;
            GeoStatusText = "Generation cancelled";
        }
        catch (Exception ex)
        {
            // Mark any locations still in "Processing" state as failed
            foreach (var vm in vmLookup.Values)
            {
                if (vm.Status == GeocodingStatus.Processing)
                {
                    vm.MarkFailed($"Error: {ex.Message}", GeocodingFailureType.Transient);
                }
            }
            GeoStatus = GeoProcessStatus.Failed;
            GeoStatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsGeoProcessing = false;
            _geoCts?.Dispose();
            _geoCts = null;
        }
    }

    [RelayCommand]
    private void CancelGeoProcess()
    {
        _geoCts?.Cancel();
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
            GeoStatusText = $"Failed to open: {ex.Message}";
        }
    }

    /// <summary>
    /// Gets the existing GeoLibre JSON file content if it exists.
    /// </summary>
    public string? GetExistingGeoLibreContent()
    {
        if (!string.IsNullOrEmpty(GeoLibreFilePath) && File.Exists(GeoLibreFilePath))
        {
            return File.ReadAllText(GeoLibreFilePath);
        }
        return null;
    }

    /// <summary>
    /// Generate GeoLibre JSON content without saving. Used by the export flow
    /// so the caller can decide what to do with the content.
    /// </summary>
    public async Task<string?> GenerateGeoLibreContentAsync()
    {
        if (SemanticResults is null || !HasSemanticResults)
        {
            GeoStatus = GeoProcessStatus.Failed;
            GeoStatusText = "No semantic data available. Run NLP analysis first.";
            return null;
        }

        // If already processing, cancel
        if (_geoLibreCts is not null)
        {
            _geoLibreCts?.Cancel();
            GeoStatusText = "Cancelling...";
            return null;
        }

        _geoLibreCts = new CancellationTokenSource();
        GeoStatus = GeoProcessStatus.Processing;
        GeoStatusText = "Generating GeoLibre JSON...";
        ShowGeoColumn = true;

        string? jsonContent = null;

        try
        {
            var geoJsonService = new GeoJsonService();
            var nlpService = new SemanticAnalysisService();

            // Get the cached GeoJSON file path (if it exists)
            var outputDir = GetSemanticOutputDir();
            var geoJsonPath = Path.Combine(outputDir, "locations.geojson");

            // Generate GeoLibre JSON string, prioritizing cache
            jsonContent = await geoJsonService.GenerateGeoLibreJsonStringAsync(
                SemanticResults,
                geoJsonPath: HasGeoJsonFile ? GeoJsonFilePath : null,
                onProgress: (status) => {
                    Dispatcher.UIThread.InvokeAsync(() => {
                        GeoStatusText = status;
                    });
                },
                cancellationToken: _geoLibreCts.Token,
                popoTreeRoot: StructureDocument?.TreeRoot,
                documentName: this.GetBestDocumentTitle(),
                nlpService: nlpService);
            nlpService.Dispose();
        }
        catch (OperationCanceledException)
        {
            GeoStatus = GeoProcessStatus.Idle;
            GeoStatusText = "Export cancelled";
            _geoLibreCts?.Dispose();
            _geoLibreCts = null;
            return null;
        }
        catch (Exception ex)
        {
            GeoStatus = GeoProcessStatus.Failed;
            GeoStatusText = $"Export error: {ex.Message}";
            _geoLibreCts?.Dispose();
            _geoLibreCts = null;
            return null;
        }
        finally
        {
            _geoLibreCts?.Dispose();
            _geoLibreCts = null;
        }

        if (jsonContent is null)
        {
            GeoStatus = GeoProcessStatus.Failed;
            GeoStatusText = "No location entities found in semantic data.";
            return null;
        }

        GeoStatus = GeoProcessStatus.Completed;
        GeoStatusText = "GeoLibre JSON 已生成，请选择处理方式...";
        return jsonContent;
    }

    /// <summary>
    /// Save GeoLibre JSON content to a file via the save dialog.
    /// </summary>
    public async Task SaveGeoLibreFileAsync(string jsonContent)
    {
        GeoStatusText = "请选择保存位置...";

        var fileName = FileName?.Replace(".pdf", "") ?? "document";
        var bytes = System.Text.Encoding.UTF8.GetBytes(jsonContent);

        var savedFile = await _filesService.SaveFileAsync(bytes, $"{fileName}.geolibre.json");

        if (savedFile is not null)
        {
            var savedPath = savedFile.Path.LocalPath;
            GeoLibreFilePath = savedPath;
            GeoStatus = GeoProcessStatus.Completed;
            GeoStatusText = $"GeoLibre JSON 已保存到: {System.IO.Path.GetFileName(savedPath)}";
            ShowGeoColumn = true;
        }
        else
        {
            GeoStatus = GeoProcessStatus.Idle;
            GeoStatusText = "Export cancelled";
        }
    }

    /// <summary>
    /// Open GeoLibre JSON content in the GeoLibre desktop app by first saving to a file.
    /// On macOS, uses 'open -a' to specify the app (debug or release).
    /// </summary>
    public void OpenInGeoLibreApp(string jsonContent)
    {
        // Save to project's semantic output directory first
        var outputDir = GetSemanticOutputDir();
        var filePath = Path.Combine(outputDir, "locations.geolibre.json");

        try
        {
            Directory.CreateDirectory(outputDir);
            File.WriteAllText(filePath, jsonContent);
            GeoLibreFilePath = filePath;

            // Open with GeoLibre desktop app using platform-specific command
            OpenGeoLibreWithSystemCommand(filePath);

            GeoStatus = GeoProcessStatus.Completed;
            GeoStatusText = $"已在 GeoLibre 中打开";
            ShowGeoColumn = true;
        }
        catch (Exception ex)
        {
            GeoStatus = GeoProcessStatus.Failed;
            GeoStatusText = $"Failed to open in GeoLibre: {ex.Message}";
        }
    }

    /// <summary>
    /// Checks if the GeoLibre debug build is running by looking for the process.
    /// Returns true if running, false otherwise.
    /// </summary>
    bool IsGeoLibreDebugRunning()
    {
        if (!OperatingSystem.IsMacOS())
            return false;

        foreach (var process in System.Diagnostics.Process.GetProcesses())
        {
            try
            {
                if (process.ProcessName == "geolibre-desktop")
                    return true;
            }
            catch
            {
                // Process may have exited, skip it
            }
        }
        return false;
    }

    /// <summary>
    /// Opens the file with the GeoLibre desktop app using platform-specific commands.
    /// Uses runtime OS detection (OperatingSystem.IsMacOS etc.) instead of compile-time conditionals.
    /// </summary>
    void OpenGeoLibreWithSystemCommand(string filePath)
    {
        if (OperatingSystem.IsMacOS())
        {
            if (IsGeoLibreDebugRunning())
            {
                var debugPath = "/Users/futuremeng/github/narrativeos/GeoLibre/apps/geolibre-desktop/src-tauri/target/debug/geolibre-desktop";
                System.Diagnostics.Debug.WriteLine($"[GeoLibre] Opening with debug build: {debugPath} {filePath}");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = debugPath,
                    Arguments = filePath,
                    UseShellExecute = false
                });
            }
            else
            {
                // Debug build not running, show error message instead of falling back to 'open'
                System.Diagnostics.Debug.WriteLine($"[GeoLibre] Debug build not running, cannot open.");
                GeoStatus = GeoProcessStatus.Failed;
                GeoStatusText = "GeoLibre 未运行，请先启动 GeoLibre 应用后再试。";
                return;
            }
        }
        else if (OperatingSystem.IsWindows())
        {
            System.Diagnostics.Debug.WriteLine($"[GeoLibre] Opening on Windows: geolibre {filePath}");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "geolibre",
                Arguments = filePath,
                UseShellExecute = true
            });
        }
        else if (OperatingSystem.IsLinux())
        {
            System.Diagnostics.Debug.WriteLine($"[GeoLibre] Opening on Linux: geolibre {filePath}");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "geolibre",
                Arguments = filePath,
                UseShellExecute = false
            });
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[GeoLibre] Opening on unknown OS: geolibre {filePath}");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "geolibre",
                Arguments = filePath,
                UseShellExecute = true
            });
        }
    }

    [RelayCommand]
    private async Task ExportGeoLibreJsonAsync()
    {
        // This is kept for backward compatibility (direct command invocation).
        // The primary flow now goes through DocumentsTabsControl.OnExportGeoLibreClick
        // which shows the choice dialog first.
        var jsonContent = await GenerateGeoLibreContentAsync();
        if (jsonContent is null)
            return;

        // Default behavior: save file (backward compatible)
        await SaveGeoLibreFileAsync(jsonContent);
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
            GeoStatusText = $"Failed to open: {ex.Message}";
        }
    }

    #endregion

    #region Geo Data Loading

    /// <summary>
    /// Tries to load existing GeoJSON file path when document is opened.
    /// Parses the GeoJSON file to populate the geocoding locations table.
    /// </summary>
    internal void TryLoadGeoData()
    {
        try
        {
            var outputDir = GetSemanticOutputDir();
            var geoJsonPath = Path.Combine(outputDir, "locations.geojson");

            if (File.Exists(geoJsonPath))
            {
                GeoJsonFilePath = geoJsonPath;
                GeoStatus = GeoProcessStatus.Completed;
                ShowGeoColumn = true;

                // Parse the GeoJSON file using System.Text.Json (fast, AOT-compatible)
                var content = File.ReadAllText(geoJsonPath);
                var locations = new ObservableCollection<GeocodingLocationViewModel>();

                using (var doc = System.Text.Json.JsonDocument.Parse(content))
                {
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("features", out System.Text.Json.JsonElement features) ||
                        features.ValueKind != System.Text.Json.JsonValueKind.Array)
                        return;

                    int loadIndex = 0;
                    foreach (var feature in features.EnumerateArray())
                    {
                        if (feature.ValueKind != System.Text.Json.JsonValueKind.Object)
                            continue;

                        string? name = null;
                        string? displayName = null;
                        string? type = null;
                        string? syntacticRole = null;
                        string? matchType = null;
                        string? matchedName = null;
                        string? matchedNameTag = null;
                        bool matchByHomophone = false;
                        double? lat = null;
                        double? lon = null;

                        // Parse properties
                        System.Text.Json.JsonElement props = default;
                        if (feature.TryGetProperty("properties", out System.Text.Json.JsonElement p) &&
                            p.ValueKind == System.Text.Json.JsonValueKind.Object)
                        {
                            props = p;
                            if (props.TryGetProperty("name", out System.Text.Json.JsonElement nameEl) && nameEl.ValueKind == System.Text.Json.JsonValueKind.String)
                                name = nameEl.GetString();
                            if (props.TryGetProperty("display_name", out System.Text.Json.JsonElement dnEl) && dnEl.ValueKind == System.Text.Json.JsonValueKind.String)
                                displayName = dnEl.GetString();
                            if (props.TryGetProperty("type", out System.Text.Json.JsonElement tEl) && tEl.ValueKind == System.Text.Json.JsonValueKind.String)
                                type = tEl.GetString();
                            if (type == null && props.TryGetProperty("class", out System.Text.Json.JsonElement cEl) && cEl.ValueKind == System.Text.Json.JsonValueKind.String)
                                type = cEl.GetString();
                            if (props.TryGetProperty("syntactic_role", out System.Text.Json.JsonElement srEl) && srEl.ValueKind == System.Text.Json.JsonValueKind.String)
                                syntacticRole = srEl.GetString();
                            if (props.TryGetProperty("match_type", out System.Text.Json.JsonElement mtEl) && mtEl.ValueKind == System.Text.Json.JsonValueKind.String)
                                matchType = mtEl.GetString();
                            if (props.TryGetProperty("matched_name", out System.Text.Json.JsonElement mnEl) && mnEl.ValueKind == System.Text.Json.JsonValueKind.String)
                                matchedName = mnEl.GetString();
                            if (props.TryGetProperty("matched_name_tag", out System.Text.Json.JsonElement mntEl) && mntEl.ValueKind == System.Text.Json.JsonValueKind.String)
                                matchedNameTag = mntEl.GetString();
                            if (props.TryGetProperty("match_by_homophone", out System.Text.Json.JsonElement mbhEl))
                            {
                                if (mbhEl.ValueKind == System.Text.Json.JsonValueKind.True)
                                    matchByHomophone = true;
                                else if (mbhEl.ValueKind == System.Text.Json.JsonValueKind.False)
                                    matchByHomophone = false;
                            }
                        }

                        // Parse geometry coordinates [lon, lat]
                        if (feature.TryGetProperty("geometry", out System.Text.Json.JsonElement geom) &&
                            geom.ValueKind == System.Text.Json.JsonValueKind.Object &&
                            geom.TryGetProperty("coordinates", out System.Text.Json.JsonElement coords) &&
                            coords.ValueKind == System.Text.Json.JsonValueKind.Array &&
                            coords.GetArrayLength() >= 2)
                        {
                            lon = coords[0].GetDouble();
                            lat = coords[1].GetDouble();
                        }

                        if (string.IsNullOrEmpty(name))
                            continue;

                        var vm = new GeocodingLocationViewModel(name);
                        vm.Index = loadIndex++;
                        vm.DisplayName = displayName ?? name;
                        vm.PlaceType = type ?? "";
                        vm.SyntacticRole = syntacticRole ?? "";
                        vm.MatchType = matchType ?? "";
                        vm.MatchedName = matchedName ?? "";
                        vm.MatchedNameTag = matchedNameTag ?? "";
                        vm.MatchByHomophone = matchByHomophone;

                        if (lat.HasValue && lon.HasValue)
                        {
                            vm.Status = GeocodingStatus.Success;
                            vm.Latitude = lat.Value;
                            vm.Longitude = lon.Value;
                        }

                        // Parse all_results array
                        if (props.ValueKind == System.Text.Json.JsonValueKind.Object &&
                            props.TryGetProperty("all_results", out System.Text.Json.JsonElement allResults) &&
                            allResults.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            foreach (var r in allResults.EnumerateArray())
                            {
                                if (r.ValueKind != System.Text.Json.JsonValueKind.Object)
                                    continue;

                                var rName = (r.TryGetProperty("name", out System.Text.Json.JsonElement rn) && rn.ValueKind == System.Text.Json.JsonValueKind.String) ? rn.GetString() : name;
                                var rLat = r.TryGetProperty("latitude", out System.Text.Json.JsonElement rl) && rl.ValueKind == System.Text.Json.JsonValueKind.Number ? (double?)rl.GetDouble() : null;
                                var rLon = r.TryGetProperty("longitude", out System.Text.Json.JsonElement rlo) && rlo.ValueKind == System.Text.Json.JsonValueKind.Number ? (double?)rlo.GetDouble() : null;
                                var rDisplayName = (r.TryGetProperty("display_name", out System.Text.Json.JsonElement rdn) && rdn.ValueKind == System.Text.Json.JsonValueKind.String) ? rdn.GetString() : rName;
                                var rIsCached = false;
                                if (r.TryGetProperty("is_cached", out System.Text.Json.JsonElement ric))
                                {
                                    if (ric.ValueKind == System.Text.Json.JsonValueKind.True) rIsCached = true;
                                }
                                var rPlaceId = (r.TryGetProperty("place_id", out System.Text.Json.JsonElement rpi) && rpi.ValueKind == System.Text.Json.JsonValueKind.String) ? rpi.GetString() : "";
                                var rOsmType = (r.TryGetProperty("osm_type", out System.Text.Json.JsonElement rot) && rot.ValueKind == System.Text.Json.JsonValueKind.String) ? rot.GetString() : "";
                                var rOsmId = r.TryGetProperty("osm_id", out System.Text.Json.JsonElement roid) && roid.ValueKind == System.Text.Json.JsonValueKind.Number ? roid.GetInt64() : 0L;
                                var rCategory = (r.TryGetProperty("category", out System.Text.Json.JsonElement rc) && rc.ValueKind == System.Text.Json.JsonValueKind.String) ? rc.GetString() : "";
                                var rType = (r.TryGetProperty("type", out System.Text.Json.JsonElement rt) && rt.ValueKind == System.Text.Json.JsonValueKind.String) ? rt.GetString() : "";
                                var rAddresstype = (r.TryGetProperty("addresstype", out System.Text.Json.JsonElement rat) && rat.ValueKind == System.Text.Json.JsonValueKind.String) ? rat.GetString() : "";
                                var rPlaceRank = r.TryGetProperty("place_rank", out System.Text.Json.JsonElement rpr) && rpr.ValueKind == System.Text.Json.JsonValueKind.Number ? rpr.GetInt32() : 0;
                                var rAddressRank = r.TryGetProperty("address_rank", out System.Text.Json.JsonElement rar) && rar.ValueKind == System.Text.Json.JsonValueKind.Number ? rar.GetInt32() : 0;
                                var rImportance = r.TryGetProperty("importance", out System.Text.Json.JsonElement ri) && ri.ValueKind == System.Text.Json.JsonValueKind.Number ? ri.GetDouble() : 0.0;
                                var rConfidence = r.TryGetProperty("confidence", out System.Text.Json.JsonElement rcf) && rcf.ValueKind == System.Text.Json.JsonValueKind.Number ? rcf.GetDouble() : 0.0;
                                var rMatchType = (r.TryGetProperty("match_type", out System.Text.Json.JsonElement rmt) && rmt.ValueKind == System.Text.Json.JsonValueKind.String) ? rmt.GetString() : "";
                                var rMatchedNameTag = (r.TryGetProperty("matched_name_tag", out System.Text.Json.JsonElement rmnt) && rmnt.ValueKind == System.Text.Json.JsonValueKind.String) ? rmnt.GetString() : "";
                                var rMatchedName = (r.TryGetProperty("matched_name", out System.Text.Json.JsonElement rmn) && rmn.ValueKind == System.Text.Json.JsonValueKind.String) ? rmn.GetString() : "";
                                var rMatchByHomophone = false;
                                if (r.TryGetProperty("match_by_homophone", out System.Text.Json.JsonElement rmh))
                                {
                                    if (rmh.ValueKind == System.Text.Json.JsonValueKind.True) rMatchByHomophone = true;
                                }

                                var result = new GeocodingResult
                                {
                                    Name = rName ?? "",
                                    Latitude = rLat ?? 0,
                                    Longitude = rLon ?? 0,
                                    DisplayName = rDisplayName ?? "",
                                    IsCached = rIsCached,
                                    PlaceId = rPlaceId ?? "",
                                    OsmType = rOsmType ?? "",
                                    OsmId = rOsmId,
                                    Category = rCategory ?? "",
                                    Type = rType ?? "",
                                    Addresstype = rAddresstype ?? "",
                                    PlaceRank = rPlaceRank,
                                    AddressRank = rAddressRank,
                                    Importance = rImportance,
                                    Confidence = rConfidence,
                                    MatchType = rMatchType ?? "",
                                    MatchedNameTag = rMatchedNameTag ?? "",
                                    MatchedName = rMatchedName ?? "",
                                    MatchByHomophone = rMatchByHomophone
                                };

                                if (rLat.HasValue)
                                {
                                    result.BoundingBoxMinLat = r.TryGetProperty("bbox_min_lat", out System.Text.Json.JsonElement bml) && bml.ValueKind == System.Text.Json.JsonValueKind.Number ? (double?)bml.GetDouble() : null;
                                    result.BoundingBoxMaxLat = r.TryGetProperty("bbox_max_lat", out System.Text.Json.JsonElement bmal) && bmal.ValueKind == System.Text.Json.JsonValueKind.Number ? (double?)bmal.GetDouble() : null;
                                    result.BoundingBoxMinLon = r.TryGetProperty("bbox_min_lon", out System.Text.Json.JsonElement bmlo) && bmlo.ValueKind == System.Text.Json.JsonValueKind.Number ? (double?)bmlo.GetDouble() : null;
                                    result.BoundingBoxMaxLon = r.TryGetProperty("bbox_max_lon", out System.Text.Json.JsonElement bma) && bma.ValueKind == System.Text.Json.JsonValueKind.Number ? (double?)bma.GetDouble() : null;
                                }

                                vm.Results.Add(new GeocodingResultViewModel(vm.Results.Count, result));
                            }

                            if (vm.Results.Count > 0)
                                vm.SelectedResultIndex = 0;
                        }

                        locations.Add(vm);
                    }
                }

                GeocodingLocations = locations;
                GeoStatusText = locations.Count > 0
                    ? $"Loaded {locations.Count} locations from cache"
                    : "GeoJSON loaded (no locations)";

                System.Diagnostics.Debug.WriteLine($"[Geo] TryLoadGeoData: loaded {locations.Count} locations from {geoJsonPath}");
            }

            // Also check for existing GeoLibre JSON file
            var geoLibrePath = Path.Combine(outputDir, "locations.geolibre.json");
            if (File.Exists(geoLibrePath))
            {
                GeoLibreFilePath = geoLibrePath;
                System.Diagnostics.Debug.WriteLine($"[Geo] TryLoadGeoData: found GeoLibre file {geoLibrePath}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Geo] TryLoadGeoData ERROR: {ex.Message}");
        }
    }

    #endregion

    /// <summary>
    /// Extract a boolean value for a given key from a JSON string (simple parsing).
    /// </summary>
    static void ExtractJsonBool(string json, string key, out bool result)
    {
        result = false;
        var searchKey = "\"" + key + "\"";
        int idx = json.IndexOf(searchKey);
        if (idx < 0) return;
        
        // Find the colon after the key
        idx = json.IndexOf(':', idx + searchKey.Length);
        if (idx < 0) return;
        
        // Skip whitespace
        idx++;
        while (idx < json.Length && char.IsWhiteSpace(json[idx])) idx++;
        
        if (idx >= json.Length) return;
        
        // Check for true/false
        if (json.Substring(idx, 4) == "true")
        {
            result = true;
        }
        else if (json.Substring(idx, 5) == "false")
        {
            result = false;
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

    /// <summary>
    /// Extract a double value for a given key from a JSON string (simple parsing).
    /// </summary>
    static double? ExtractJsonDoubleValue(string json, string key)
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
        
        // Read number until comma, }, ], or whitespace
        int start = idx;
        int end = idx;
        while (end < json.Length && json[end] != ',' && json[end] != '}' && json[end] != ']' && !char.IsWhiteSpace(json[end]))
            end++;
        
        if (end > start && double.TryParse(json.Substring(start, end - start), out var value))
            return value;
        
        return null;
    }

    /// <summary>
    /// Extract a long value for a given key from a JSON string (simple parsing).
    /// </summary>
    static long? ExtractJsonLongValue(string json, string key)
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
        
        // Read number until comma, }, ], or whitespace
        int start = idx;
        int end = idx;
        while (end < json.Length && json[end] != ',' && json[end] != '}' && json[end] != ']' && !char.IsWhiteSpace(json[end]))
            end++;
        
        if (end > start && long.TryParse(json.Substring(start, end - start), out var value))
            return value;
        
        return null;
    }

    /// <summary>
    /// Extract a boolean value for a given key from a JSON string (returns default false if not found).
    /// </summary>
    static bool ExtractJsonBoolValue(string json, string key)
    {
        ExtractJsonBool(json, key, out var result);
        return result;
    }
}

/// <summary>
/// Geo processing status.
/// </summary>
public enum GeoProcessStatus
{
    Idle,
    Processing,
    Completed,
    Failed,
}