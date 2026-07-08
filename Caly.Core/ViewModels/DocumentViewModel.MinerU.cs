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
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Caly.Core.ViewModels;

public sealed partial class DocumentViewModel
{
    #region MinerU Properties

    [ObservableProperty]
    private MinerUParseStatus _minerUStatus = MinerUParseStatus.Idle;

    [ObservableProperty]
    private int _minerUProgress;

    [ObservableProperty]
    private string _minerUStatusText = "Ready";

    [ObservableProperty]
    private bool _isMinerUParsing;

    /// <summary>
    /// Current page being processed (from MinerU progress API).
    /// </summary>
    [ObservableProperty]
    private int _minerUCurrentPage;

    /// <summary>
    /// Total pages in the document (from MinerU progress API).
    /// </summary>
    [ObservableProperty]
    private int _minerUTotalPages;

    /// <summary>
    /// Current processing stage name (e.g., "Text Recognition").
    /// </summary>
    [ObservableProperty]
    private string _minerUCurrentStage = string.Empty;

    /// <summary>
    /// Formatted detail string for progress display (e.g., "Text Recognition | Page 59/114").
    /// </summary>
    [ObservableProperty]
    private string _minerUProgressDetail = string.Empty;

    /// <summary>
    /// Whether MinerU AI parsing is enabled (reads from settings service).
    /// </summary>
    public bool MinerUEnabled => _settingsService.GetSettings().MinerUEnabled;

    private CancellationTokenSource? _minerUCts;

    /// <summary>
    /// Cached MinerUService instance per configuration key.
    /// Reuses HttpClient connections across parse operations.
    /// </summary>
    private static readonly ConditionalWeakTable<string, MinerUService> _minerUServiceCache = new();

    /// <summary>
    /// Raw MinerU blocks for the middle column of the three-column layout.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<MinerUBlockViewModel> _minerUBlocks = new();

    /// <summary>
    /// Raw MinerU middle JSON data (for reference).
    /// </summary>
    [ObservableProperty]
    private MinerUMiddleJson? _minerUMiddleJson;

    /// <summary>
    /// Gets whether MinerU raw blocks are available for display.
    /// </summary>
    public bool HasMinerUBlocks => MinerUBlocks.Count > 0;

    /// <summary>
    /// Whether the MinerU column is visible (user toggle).
    /// Defaults to false; shown when MinerU data is loaded.
    /// </summary>
    [ObservableProperty]
    private bool _showMinerUColumn = false;

    /// <summary>
    /// Whether the Popo column is visible (user toggle).
    /// Defaults to false; shown when Popo data is loaded.
    /// </summary>
    [ObservableProperty]
    private bool _showAnalysisColumn = false;

    /// <summary>
    /// Currently selected MinerU block ID for cross-highlighting with the PDF overlay.
    /// When set, the BlockOverlayControl highlights all blocks whose IDs are in the
    /// RelatedBlockIds of the selected block.
    /// </summary>
    [ObservableProperty]
    private int? _selectedMinerUBlockId;

    // Cached reference to the currently selected block to avoid O(n) searches on each property access.
    private MinerUBlockViewModel? _cachedSelectedBlock;

    /// <summary>
    /// Dictionary for O(1) block lookup by ID. Rebuilt whenever MinerUBlocks collection changes.
    /// </summary>
    private System.Collections.Generic.Dictionary<int, MinerUBlockViewModel> _blockIdMap = new();

    /// <summary>
    /// Gets the RelatedBlockIds of the currently selected MinerU block.
    /// Used to bind to BlockOverlayControl.RelatedHighlightBlockIds for cross-highlighting.
    /// Returns a HashSet-backed IReadOnlySet for O(1) Contains performance in the render loop.
    /// </summary>
    public System.Collections.Generic.IReadOnlySet<int>? SelectedMinerUBlockRelatedIds
    {
        get
        {
            if (_cachedSelectedBlock is null)
                return null;
            var related = _cachedSelectedBlock.RelatedBlockIds;
            return related.Count > 0 ? (System.Collections.Generic.IReadOnlySet<int>)new System.Collections.Generic.HashSet<int>(related) : null;
        }
    }

    /// <summary>
    /// Gets the DestinationType of the currently selected MinerU block.
    /// Used to determine highlight color: "para" = green, "discarded" = red.
    /// </summary>
    public string? SelectedMinerUBlockDestinationType
    {
        get
        {
            _cachedSelectedBlock ??= FindSelectedBlock();
            return _cachedSelectedBlock?.DestinationType;
        }
    }

    private MinerUBlockViewModel? FindSelectedBlock()
    {
        if (SelectedMinerUBlockId is not int id)
            return null;
        return _blockIdMap.TryGetValue(id, out var block) ? block : null;
    }

    /// <summary>
    /// Clears selection cache and rebuilds the block ID map after MinerUBlocks collection is replaced.
    /// Call this after MinerUBlocks.Clear() or when the collection is repopulated.
    /// </summary>
    internal void ResetMinerUSelectionAndCache()
    {
        _cachedSelectedBlock = null;
        SelectedMinerUBlockId = null;
        RebuildBlockIdMap();
        OnPropertyChanged(nameof(SelectedMinerUBlockRelatedIds));
        OnPropertyChanged(nameof(SelectedMinerUBlockDestinationType));
    }

    /// <summary>
    /// Rebuilds the block ID map after MinerUBlocks collection is populated.
    /// Uses a loop instead of ToDictionary to handle duplicate IDs gracefully (last-one-wins).
    /// Duplicate IDs can occur when the same block ID appears in both para_blocks and discarded_blocks.
    /// </summary>
    internal void RebuildBlockIdMap()
    {
        var dict = new System.Collections.Generic.Dictionary<int, MinerUBlockViewModel>(MinerUBlocks.Count);
        foreach (var block in MinerUBlocks)
        {
            dict[block.Id] = block; // Last-one-wins for duplicate IDs
        }
        _blockIdMap = dict;
    }


    #endregion

    #region MinerU Service Factory

    /// <summary>
    /// Gets or creates a cached MinerUService instance for the current configuration.
    /// This ensures HttpClient reuse across parse operations.
    /// </summary>
    private MinerUService GetMinerUService()
    {
        var settings = _settingsService.GetSettings();
        var minerUDir = ProjectPath is not null
            ? Path.Combine(ProjectPath, "mineru")
            : null;
        // Cache key includes both URL and cache directory for proper isolation
        var cacheKey = $"{settings.MinerUBaseUrl}|{minerUDir}";
        return _minerUServiceCache.GetValue(cacheKey, _ => new MinerUService(settings.MinerUBaseUrl, minerUDir));
    }

    #endregion

    #region MinerU Commands

    [RelayCommand]
    private void ToggleMinerUColumn()
    {
        ShowMinerUColumn = !ShowMinerUColumn;
    }

    [RelayCommand]
    private void ToggleAnalysisColumn()
    {
        ShowAnalysisColumn = !ShowAnalysisColumn;
    }

    /// <summary>
    /// Selects a MinerU block for cross-highlighting with the PDF overlay.
    /// When a block is selected, its RelatedBlockIds are highlighted on the PDF overlay.
    /// Clicking the same block again deselects it (toggles off).
    /// </summary>
    [RelayCommand]
    private void SelectMinerUBlock(int blockId)
    {
        // Toggle: if the same block is clicked again, deselect
        if (SelectedMinerUBlockId == blockId)
        {
            SelectedMinerUBlockId = null;
            _cachedSelectedBlock = null;
        }
        else
        {
            SelectedMinerUBlockId = blockId;
            // Use dictionary for O(1) lookup instead of LINQ FirstOrDefault
            _cachedSelectedBlock = _blockIdMap.TryGetValue(blockId, out var block) ? block : null;
        }
        // Notify UI that computed properties have changed
        OnPropertyChanged(nameof(SelectedMinerUBlockRelatedIds));
        OnPropertyChanged(nameof(SelectedMinerUBlockDestinationType));
    }

    /// <summary>
    /// Parses the current document using MinerU AI service.
    /// Flow: 1) Check cache → 2) Check pending task ID → 3) Submit new task.
    /// If called while already parsing, cancels the current operation.
    /// </summary>
    [RelayCommand]
    private async Task ParseWithMinerUAsync()
    {
        if (LocalPath is null)
        {
            MinerUStatus = MinerUParseStatus.Failed;
            MinerUStatusText = "No document open";
            MinerUProgress = 0;
            return;
        }

        // If already parsing, cancel
        if (IsMinerUParsing)
        {
            _minerUCts?.Cancel();
            MinerUStatusText = "Cancelling...";
            return;
        }

        var service = GetMinerUService();

        // Step 1: Try to load from cache first (avoids unnecessary network requests)
        var cachedResult = service.TryLoadFromCache(LocalPath);
        if (cachedResult?.StructureDocument is not null)
        {
            LoadParseResult(cachedResult);
            MinerUStatus = MinerUParseStatus.Completed;
            MinerUProgress = 100;
            MinerUStatusText = "Loaded from cache";
            IsMinerUParsing = false;
            return;
        }

        // Step 2: Check for a pending task ID from a previous session
        var pendingTaskId = service.LoadTaskId(LocalPath, ProjectPath);
        if (pendingTaskId is not null)
        {
            _minerUCts = new CancellationTokenSource();
            IsMinerUParsing = true;
            ShowMinerUColumn = true;
            MinerUStatus = MinerUParseStatus.Processing;
            MinerUProgress = 50;
            MinerUStatusText = "Resuming previous task...";

            try
            {
                // Health check before resuming
                if (!await service.HealthCheckAsync(_minerUCts.Token))
                {
                    // Service unavailable, clear stale task ID and let user retry
                    service.ClearTaskId(LocalPath, ProjectPath);
                    MinerUStatus = MinerUParseStatus.Failed;
                    MinerUStatusText = "MinerU service unavailable";
                    MinerUProgress = 0;
                    return;
                }

                var result = await service.ResumeTaskAsync(
                    pendingTaskId,
                    LocalPath,
                    OnMinerUProgress,
                    _minerUCts.Token);

                // Load result
                LoadParseResult(result);
                await SaveParseResultAsync(result);

                // Clear the task ID on success
                service.ClearTaskId(LocalPath, ProjectPath);

                MinerUStatus = MinerUParseStatus.Completed;
                MinerUProgress = 100;
                MinerUStatusText = "Parse completed (resumed)";
                return;
            }
            catch (OperationCanceledException)
            {
                MinerUStatus = MinerUParseStatus.Idle;
                MinerUProgress = 0;
                MinerUStatusText = "Parse cancelled";
                return;
            }
            catch (MinerUServiceException ex)
            {
                // Task failed on server side - clear task ID so user can retry
                service.ClearTaskId(LocalPath, ProjectPath);
                MinerUStatus = MinerUParseStatus.Failed;
                MinerUProgress = 0;
                MinerUStatusText = $"Resume failed: {ex.Message}";

                // Fall through to submit a new task below
            }
            catch (Exception ex)
            {
                service.ClearTaskId(LocalPath, ProjectPath);
                MinerUStatus = MinerUParseStatus.Failed;
                MinerUProgress = 0;
                MinerUStatusText = $"Error: {ex.Message}";
                return;
            }
            finally
            {
                IsMinerUParsing = false;
                _minerUCts?.Dispose();
                _minerUCts = null;
            }

            // If resume failed, fall through to submit a new task
        }

        // Step 3: Submit a new parse task
        var docId = Path.GetFileNameWithoutExtension(LocalPath);
        service.ClearOldExtractedDirs(docId);

        _minerUCts = new CancellationTokenSource();
        IsMinerUParsing = true;
        ShowMinerUColumn = true;
        MinerUStatus = MinerUParseStatus.Submitting;
        MinerUProgress = 0;
        MinerUStatusText = MinerUParseStatus.Submitting.ToDisplayName();

        try
        {
            var settings = _settingsService.GetSettings();

            // Health check
            if (!await service.HealthCheckAsync(_minerUCts.Token))
            {
                MinerUStatus = MinerUParseStatus.Failed;
                MinerUStatusText = "MinerU service unavailable";
                MinerUProgress = 0;
                return;
            }

            // Submit the task and immediately persist the task ID
            var taskId = await service.SubmitTaskAsync(
                LocalPath,
                settings.MinerUBackend,
                _minerUCts.Token);

            // Save task ID so it can be recovered if the app closes
            service.SaveTaskId(LocalPath, taskId, ProjectPath);

            // Poll until complete
            MinerUStatus = MinerUParseStatus.Queued;
            MinerUProgress = 15;
            MinerUStatusText = MinerUParseStatus.Queued.ToDisplayName();

            await service.PollUntilCompleteAsync(taskId, OnMinerUProgressDetailed, _minerUCts.Token);

            // Download result
            MinerUStatus = MinerUParseStatus.Downloading;
            MinerUProgress = 70;
            MinerUStatusText = MinerUParseStatus.Downloading.ToDisplayName();

            var zipPath = await service.DownloadResultAsync(taskId, LocalPath, _minerUCts.Token);

            // Build result
            MinerUStatus = MinerUParseStatus.Caching;
            MinerUProgress = 80;
            MinerUStatusText = MinerUParseStatus.Caching.ToDisplayName();

            var result = await service.BuildParseResultFromZipAsync(zipPath, LocalPath, OnMinerUProgress, _minerUCts.Token);

            System.Diagnostics.Debug.WriteLine($"[MinerU ViewModel DEBUG] Parse completed: StructureDocument={result.StructureDocument != null}, ZipPath={result.ZipPath}, ArtifactsDir={result.ArtifactsDirectory}");

            // Load result into Popo properties
            LoadParseResult(result);

            // Save result to project for persistence
            await SaveParseResultAsync(result);

            // Clear task ID on success
            service.ClearTaskId(LocalPath, ProjectPath);

            MinerUStatus = MinerUParseStatus.Completed;
            MinerUProgress = 100;
            MinerUStatusText = "Parse completed";
        }
        catch (OperationCanceledException)
        {
            // On cancel, keep the task ID so user can resume later
            MinerUStatus = MinerUParseStatus.Idle;
            MinerUProgress = 0;
            MinerUStatusText = "Parse cancelled";
        }
        catch (MinerUServiceException ex)
        {
            // On failure, clear task ID so user can retry
            service.ClearTaskId(LocalPath, ProjectPath);
            MinerUStatus = MinerUParseStatus.Failed;
            MinerUProgress = 0;
            MinerUStatusText = $"Parse failed: {ex.Message}";
        }
        catch (Exception ex)
        {
            service.ClearTaskId(LocalPath, ProjectPath);
            MinerUStatus = MinerUParseStatus.Failed;
            MinerUProgress = 0;
            MinerUStatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsMinerUParsing = false;
            _minerUCts?.Dispose();
            _minerUCts = null;
        }
    }

    /// <summary>
    /// Saves the MinerU ZIP to the project folder (for Popo upload).
    /// Does NOT generate any Popo-related files — MinerU has no dependency on Popo.
    /// </summary>
    private async Task SaveParseResultAsync(MinerUParseResult result)
    {
        // Save the MinerU ZIP to the project folder for Popo upload
        if (!string.IsNullOrEmpty(result.ZipPath) && ProjectPath is not null)
        {
            try
            {
                var docId = Path.GetFileNameWithoutExtension(LocalPath!);
                MinerUJsonService.SaveMinerUZipToProject(result.ZipPath, ProjectPath, docId);
            }
            catch
            {
                // Non-critical: ignore ZIP save errors
            }
        }

        // Notify that HasMinerUZip may have changed (Popo result now exists or ZIP was saved)
        OnPropertyChanged(nameof(HasMinerUZip));
    }

    /// <summary>
    /// Loads a parse result into the ViewModel properties.
    /// Populates MinerUDocument, page blocks, MinerU blocks, and flat Popo blocks.
    /// </summary>
    private void LoadParseResult(MinerUParseResult result)
    {
        System.Diagnostics.Debug.WriteLine($"[MinerU ViewModel DEBUG] LoadParseResult called, StructureDocument={result.StructureDocument != null}");

        if (result.StructureDocument is null)
        {
            System.Diagnostics.Debug.WriteLine($"[MinerU ViewModel DEBUG] LoadParseResult: StructureDocument is NULL, returning early!");
            return;
        }

        LoadStructureDocument(result.StructureDocument, result.ArtifactsDirectory);
    }

    /// <summary>
    /// Loads a StructureDocument into the ViewModel properties.
    /// Shared by both MinerU (AI Parse) and Popo processing flows.
    /// Populates page blocks, MinerU middle blocks, and auto-opens the analysis pane.
    /// Does NOT populate PopoBlocks — that is done separately by Popo processing.
    /// </summary>
    private void LoadStructureDocument(StructureDocument minerUDoc, string? artifactsDirectory = null)
    {
        System.Diagnostics.Debug.WriteLine($"[MinerU ViewModel DEBUG] StructureDocument has {minerUDoc.GetAllBlocks().Count} blocks, TreeRoot={minerUDoc.TreeRoot != null}");

        StructureDocument = minerUDoc;
        AnalysisViewModel = new AnalysisViewModel(minerUDoc, artifactsDirectory);

        System.Diagnostics.Debug.WriteLine($"[MinerU ViewModel DEBUG] StructureDocument and AnalysisViewModel set");

        // Assign blocks to each page view model
        foreach (var page in Pages)
        {
            page.MinerUBlocks = minerUDoc.GetBlocksForPage(page.PageNumber);
        }

        System.Diagnostics.Debug.WriteLine($"[MinerU ViewModel DEBUG] Blocks assigned to {Pages.Count} page view models");


        // Build block collections in memory first, then replace
        if (minerUDoc.PagesBlocks is not null)
        {
            var allBlocks = minerUDoc.GetAllBlocks();

            // MinerUBlocks: MinerUBlockViewModel for middle column (raw MinerU data)
            var newMinerUViewModels = allBlocks.Select(block => ToMinerUBlockViewModel(block)).ToList();

            // Clear selection cache before replacing collection (avoids stale reference)
            _cachedSelectedBlock = null;
            SelectedMinerUBlockId = null;

            // Replace collections in bulk
            MinerUBlocks.Clear();
            foreach (var b in newMinerUViewModels)
                MinerUBlocks.Add(b);

            // Rebuild the block ID map for O(1) lookups
            RebuildBlockIdMap();

            System.Diagnostics.Debug.WriteLine($"[MinerU ViewModel DEBUG] Collections populated: MinerUBlocks={MinerUBlocks.Count}");
        }

        // Auto-open the Popo analysis pane
        IsPopoPaneOpen = true;

        System.Diagnostics.Debug.WriteLine($"[MinerU ViewModel DEBUG] LoadStructureDocument completed: HasStructureDocument={HasStructureDocument}, MinerUBlocks={MinerUBlocks.Count}");
    }

    /// <summary>
    /// Cancels the current MinerU parse operation.
    /// </summary>
    [RelayCommand]
    private void CancelMinerUParse()
    {
        _minerUCts?.Cancel();
    }

    #endregion

    #region Auto-Load from Cache

    /// <summary>
    /// Tries to load MinerU data from the project's mineru/ directory.
    /// Called silently when opening an existing project.
    /// </summary>
    internal void TryLoadMinerUData()
    {
        if (ProjectPath is null)
            return;

        var minerUDir = Path.Combine(ProjectPath, "mineru");
        if (!Directory.Exists(minerUDir))
            return;

        // Look for *_middle.json files
        var middleJsonFiles = Directory.GetFiles(minerUDir, "*_middle.json", SearchOption.AllDirectories);
        if (middleJsonFiles.Length == 0)
            return;

        var minerUDoc = MinerUJsonService.TryParseMinerUMiddleJson(middleJsonFiles[0]);
        if (minerUDoc is null)
            return;

        // Set StructureDocument first — subscribes the Pages.CollectionChanged handler
        // so any pages added later will automatically get blocks assigned.
        StructureDocument = minerUDoc;
        AnalysisViewModel = new AnalysisViewModel(minerUDoc);

        // Assign blocks to existing pages (the CollectionChanged handler only covers future additions)
        foreach (var page in Pages)
        {
            if (page.MinerUBlocks is null)
            {
                page.MinerUBlocks = minerUDoc.GetBlocksForPage(page.PageNumber);
            }
        }

        // Populate MinerUBlocks (middle column)
        // Clear selection cache before replacing collection (avoids stale reference)
        _cachedSelectedBlock = null;
        SelectedMinerUBlockId = null;

        MinerUBlocks.Clear();
        foreach (var block in minerUDoc.GetAllBlocks())
        {
            MinerUBlocks.Add(ToMinerUBlockViewModel(block));
        }

        // Rebuild the block ID map for O(1) lookups
        RebuildBlockIdMap();

        MinerUStatus = MinerUParseStatus.Completed;
        MinerUProgress = 100;
        MinerUStatusText = $"Loaded ({MinerUBlocks.Count} blocks)";

        // Show the MinerU column when data is loaded
        ShowMinerUColumn = true;
    }

    /// <summary>
    /// Converts a MinerUBlock to a MinerUBlockViewModel for UI binding.
    /// Extracted to avoid code duplication across LoadStructureDocument and TryLoadMinerUData.
    /// </summary>
    private MinerUBlockViewModel ToMinerUBlockViewModel(MinerUBlock block)
    {
        return new MinerUBlockViewModel(
            new MinerUMiddlePageBlock
            {
                Id = block.Id,
                Page = block.Page,
                Type = block.Type,
                Content = block.Content,
                SourceLabel = block.SourceLabel,
                Contd = block.Contd,
                Level = block.Level,
                Image = block.Image,
                Bbox = new double[] { block.Bbox.X, block.Bbox.Y, block.Bbox.Right, block.Bbox.Bottom },
                BlockSource = block.BlockSource,
                DestinationType = block.DestinationType,
                RelatedBlockIds = new System.Collections.Generic.List<int>(block.RelatedBlockIds)
            });
    }

    #endregion

    #region Progress Callback

    /// <summary>
    /// Called by MinerUService during parsing to report progress updates (simple version).
    /// Ensures all property updates happen on the UI thread.
    /// </summary>
    private void OnMinerUProgress(MinerUParseStatus status, int progress)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            MinerUStatus = status;
            MinerUProgress = progress;
            MinerUStatusText = status.ToDisplayName();
        });
    }

    /// <summary>
    /// Called by MinerUService during parsing to report detailed progress updates.
    /// Includes page info and stage from the MinerU progress API.
    /// </summary>
    private void OnMinerUProgressDetailed(MinerUParseStatus status, int progress, MinerUTaskProgress? progressInfo)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            MinerUStatus = status;
            MinerUProgress = progress;

            if (progressInfo != null)
            {
                MinerUCurrentPage = progressInfo.CurrentPage;
                MinerUTotalPages = progressInfo.TotalPages;
                MinerUCurrentStage = progressInfo.Stage ?? string.Empty;
                MinerUProgressDetail = progressInfo.ToDisplayString();

                // Build a more descriptive status text
                if (!string.IsNullOrEmpty(progressInfo.Stage))
                {
                    MinerUStatusText = $"{status.ToDisplayName()} {progress}% - {progressInfo.ToDisplayString()}";
                }
                else
                {
                    MinerUStatusText = $"{status.ToDisplayName()} {progress}%";
                }
            }
            else
            {
                MinerUCurrentPage = 0;
                MinerUTotalPages = 0;
                MinerUCurrentStage = string.Empty;
                MinerUProgressDetail = string.Empty;
                MinerUStatusText = status.ToDisplayName();
            }
        });
    }

    #endregion
}