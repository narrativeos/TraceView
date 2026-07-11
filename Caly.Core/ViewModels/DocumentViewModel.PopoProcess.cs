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

using Avalonia.Threading;
using Caly.Core.Models;
using Caly.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using PopoProcessStatus = Caly.Core.Services.PopoProcessStatus;

namespace Caly.Core.ViewModels;

public sealed partial class DocumentViewModel
{
    #region Popo Process Properties

    [ObservableProperty]
    private PopoProcessStatus _popoStatus = PopoProcessStatus.Idle;

    partial void OnPopoStatusChanged(PopoProcessStatus value)
    {
        OnPropertyChanged(nameof(ShowPopoBlocksList));
    }

    [ObservableProperty]
    private int _popoProgress;

    [ObservableProperty]
    private string _popoStatusText = "Ready";

    [ObservableProperty]
    private bool _isPopoProcessing;

    partial void OnIsPopoProcessingChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowPopoBlocksList));
    }

    /// <summary>
    /// Whether the Popo blocks list should be shown (not processing and no error).
    /// </summary>
    public bool ShowPopoBlocksList => !IsPopoProcessing && PopoStatus != PopoProcessStatus.Failed;

    /// <summary>
    /// Whether Popo processing is enabled (reads from settings service).
    /// </summary>
    public bool PopoEnabled => !string.IsNullOrEmpty(_settingsService.GetSettings().PopoBaseUrl);

    /// <summary>
    /// Whether Popo-processed results already exist in the project's popo/ directory.
    /// Checks for popo_result.json directly in the popo/ directory.
    /// </summary>
    public bool HasPopoResult
    {
        get
        {
            if (ProjectPath is null)
                return false;

            var popoDir = Path.Combine(ProjectPath, "popo");
            if (!Directory.Exists(popoDir))
                return false;

            // Check for popo_result.json directly in the popo directory
            var resultJson = Path.Combine(popoDir, "popo_result.json");
            if (File.Exists(resultJson))
                return true;

            // Fallback: Check for extract subdirectory with JSON files
            var extractDir = Path.Combine(popoDir, "extract");
            if (Directory.Exists(extractDir))
            {
                var jsonFiles = Directory.GetFiles(extractDir, "*.json", SearchOption.AllDirectories);
                if (jsonFiles.Length > 0)
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Whether there is a MinerU ZIP available for Popo processing.
    /// Returns false if Popo results already exist (no need to re-process).
    /// </summary>
    public bool HasMinerUZip
    {
        get
        {
            if (ProjectPath is null)
                return false;

            // Don't show button if Popo result already exists (not MinerU result)
            if (HasPopoResult)
                return false;

            var docId = LocalPath is not null ? Path.GetFileNameWithoutExtension(LocalPath) : null;
            return MinerUJsonService.FindMinerUZipInProject(ProjectPath, docId) is not null;
        }
    }

    private CancellationTokenSource? _popoCts;

    /// <summary>
    /// Cached PopoService instance per configuration key.
    /// </summary>
    private static readonly ConditionalWeakTable<string, PopoService> _analysisServiceCache = new();

    #endregion

    #region Popo Service Factory

    private PopoService GetPopoService()
    {
        var settings = _settingsService.GetSettings();
        var cacheDir = ProjectPath is not null
            ? Path.Combine(ProjectPath, "popo")
            : null;
        var cacheKey = $"{settings.PopoBaseUrl}|{cacheDir}";
        return _analysisServiceCache.GetValue(cacheKey, _ => new PopoService(settings.PopoBaseUrl, cacheDir));
    }

    #endregion

    #region Popo Commands

    /// <summary>
    /// Processes the current document with Popo service.
    /// Submits the MinerU ZIP, waits for processing, downloads the result, and loads the MinerUDocument.
    /// </summary>
    [RelayCommand]
    private async Task ProcessWithPopoAsync()
    {
        if (LocalPath is null)
        {
            PopoStatus = PopoProcessStatus.Failed;
            PopoStatusText = "No document open";
            PopoProgress = 0;
            return;
        }

        // If already processing, cancel
        if (IsPopoProcessing)
        {
            _popoCts?.Cancel();
            PopoStatusText = "Cancelling...";
            return;
        }

        var service = GetPopoService();
        var docId = Path.GetFileNameWithoutExtension(LocalPath);

        // Show the Popo column immediately with progress state (before health check / MinU ZIP check)
        // This matches the AI Parse pattern where the column is shown before any async work.
        _popoCts = new CancellationTokenSource();
        IsPopoProcessing = true;
        ShowAnalysisColumn = true;
        PopoStatus = PopoProcessStatus.Submitting;
        PopoProgress = 0;
        PopoStatusText = PopoProcessStatus.Submitting.ToDisplayName();

        // Clear Popo blocks to avoid showing stale data while processing
        PopoBlocks.Clear();

        // Yield to ensure the UI renders the progress state before any potentially
        // synchronous early-return (e.g., missing ZIP, health check failure).
        await Task.Yield();

        try
        {
            // Find the MinerU ZIP in the project
            var zipPath = MinerUJsonService.FindMinerUZipInProject(ProjectPath ?? string.Empty, docId);
            if (zipPath is null)
            {
                PopoStatus = PopoProcessStatus.Failed;
                var expectedPath = ProjectPath is not null
                    ? Path.Combine(ProjectPath, "mineru", $"{docId}.zip")
                    : "mineru/ directory";
                PopoStatusText = $"No MinerU ZIP found at {expectedPath}. Run AI Parse first to generate it, then try Process with Popo again.";
                PopoProgress = 0;
                return;
            }

            // Health check
            if (!await service.HealthCheckAsync())
            {
                PopoStatus = PopoProcessStatus.Failed;
                var baseUrl = _settingsService.GetSettings().PopoBaseUrl;
                PopoStatusText = $"Popo service unavailable at {baseUrl}. Ensure the Popo server is running.";
                PopoProgress = 0;
                return;
            }


            var settings = _settingsService.GetSettings();
            var result = await service.ProcessAsync(
                zipPath,
                docId,
                settings.PopoModel,
                OnPopoProgress,
                _popoCts.Token);

            if (result.StructureDocument is not null)
            {
                // Build hierarchical tree for Column 3 (matches popo_result.json tree structure)
                if (result.StructureDocument.TreeRoot is not null)
                {
                    PopoTreeRoot = new TreeNodeViewModel(result.StructureDocument.TreeRoot, result.ArtifactsDirectory, result.StructureDocument);
                }

                // Populate flat blocks for backward compatibility
                var allBlocks = result.StructureDocument.GetAllBlocks();
                PopoBlocks.Clear();
                foreach (var block in allBlocks)
                {
                    PopoBlocks.Add(new BlockViewModel(block));
                }

                // Note: popo_result.json is already saved by PopoService.DownloadAndParseResultAsync
                // in the project's popo/ directory. TryLoadPopoData loads from there.
            }

            PopoStatus = PopoProcessStatus.Completed;
            PopoProgress = 100;
            PopoStatusText = "Popo completed";
        }
        catch (OperationCanceledException)
        {
            PopoStatus = PopoProcessStatus.Idle;
            PopoProgress = 0;
            PopoStatusText = "Popo cancelled";
        }
        catch (PopoServiceException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PopoProcess] PopoServiceException: {ex}");
            PopoStatus = PopoProcessStatus.Failed;
            PopoProgress = 0;
            PopoStatusText = $"Popo failed: {ex.Message}";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PopoProcess] Unexpected error: {ex}");
            PopoStatus = PopoProcessStatus.Failed;
            PopoProgress = 0;
            PopoStatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsPopoProcessing = false;
            // Keep the column visible so the user can see the final status (error or completion)
            // The column will remain open until the user manually toggles it off
            _popoCts?.Dispose();
            _popoCts = null;
        }
    }

    [RelayCommand]
    private void CancelPopoProcess()
    {
        _popoCts?.Cancel();
    }

    #endregion

    #region Progress Callback

    private void OnPopoProgress(PopoProcessStatus status, int progress)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            PopoStatus = status;
            PopoProgress = progress;
            PopoStatusText = status.ToDisplayName();
        });
    }

    #endregion
}