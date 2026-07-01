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
    /// Whether MinerU AI parsing is enabled (reads from CalySettings).
    /// </summary>
    public bool MinerUEnabled => CalySettings.Default.MinerUEnabled;

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
    /// </summary>
    [ObservableProperty]
    private bool _showMinerUColumn = true;

    /// <summary>
    /// Whether the Popo column is visible (user toggle).
    /// </summary>
    [ObservableProperty]
    private bool _showPopoColumn = true;


    #endregion

    #region MinerU Service Factory

    /// <summary>
    /// Gets or creates a cached MinerUService instance for the current configuration.
    /// This ensures HttpClient reuse across parse operations.
    /// </summary>
    private MinerUService GetMinerUService()
    {
        var settings = CalySettings.Default;
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
    private void TogglePopoColumn()
    {
        ShowPopoColumn = !ShowPopoColumn;
    }

    /// <summary>
    /// Parses the current document using MinerU AI service.
    /// First checks for cached results, then uploads the PDF, waits for parsing,
    /// downloads the result, and loads the PopoDocument.
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

        // Try to load from cache first (avoids unnecessary network requests)
        var cachedResult = service.TryLoadFromCache(LocalPath);
        if (cachedResult?.PopoDocument is not null)
        {
            LoadParseResult(cachedResult);
            MinerUStatus = MinerUParseStatus.Completed;
            MinerUProgress = 100;
            MinerUStatusText = "Loaded from cache";
            IsMinerUParsing = false;
            return;
        }

        // Clean up old extracted directories before starting a new parse
        var docId = Path.GetFileNameWithoutExtension(LocalPath);
        service.ClearOldExtractedDirs(docId);

        _minerUCts = new CancellationTokenSource();
        IsMinerUParsing = true;
        ShowMinerUColumn = true; // Auto-open MinerU column to show progress in-place
        MinerUStatus = MinerUParseStatus.Submitting;
        MinerUProgress = 0;
        MinerUStatusText = MinerUParseStatus.Submitting.ToDisplayName();

        try
        {
            var settings = CalySettings.Default;

            // Health check
            if (!await service.HealthCheckAsync(_minerUCts.Token))
            {
                MinerUStatus = MinerUParseStatus.Failed;
                MinerUStatusText = "MinerU service unavailable";
                MinerUProgress = 0;
                return;
            }

            // Full parse flow: submit → poll → download → parse → load
            var result = await service.ParseAsync(
                LocalPath,
                settings.MinerUBackend,
                OnMinerUProgress,
                _minerUCts.Token);

            // Load result into Popo properties
            LoadParseResult(result);

            // Save result to project for persistence
            if (result.PopoDocument is not null && ProjectPath is not null)
            {
                try
                {
                    PopoJsonService.SavePopoDocumentToProject(result.PopoDocument, ProjectPath);
                }
                catch
                {
                    // Non-critical: ignore save errors
                }
            }

            MinerUStatus = MinerUParseStatus.Completed;
            MinerUProgress = 100;
            MinerUStatusText = "Parse completed";
        }
        catch (OperationCanceledException)
        {
            MinerUStatus = MinerUParseStatus.Idle;
            MinerUProgress = 0;
            MinerUStatusText = "Parse cancelled";
        }
        catch (MinerUServiceException ex)
        {
            MinerUStatus = MinerUParseStatus.Failed;
            MinerUProgress = 0;
            MinerUStatusText = $"Parse failed: {ex.Message}";
        }
        catch (Exception ex)
        {
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
    /// Loads a parse result into the ViewModel properties.
    /// Populates PopoDocument, page blocks, MinerU blocks, and flat Popo blocks.
    /// </summary>
    private void LoadParseResult(MinerUParseResult result)
    {
        if (result.PopoDocument is null)
            return;

        PopoDocument = result.PopoDocument;
        PopoAnalysisViewModel = new PopoAnalysisViewModel(result.PopoDocument);

        // Assign blocks to each page view model
        foreach (var page in Pages)
        {
            page.PopoBlocks = result.PopoDocument.GetBlocksForPage(page.PageNumber);
        }

        // Build block collections in memory first, then replace
        if (result.PopoDocument.PagesBlocks is not null)
        {
            var allBlocks = result.PopoDocument.GetAllBlocks();

            var newMinerUBlocks = allBlocks.Select(block => new MinerUBlockViewModel(
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
                    Bbox = new double[] { block.Bbox.X, block.Bbox.Y, block.Bbox.Right, block.Bbox.Bottom }
                })).ToList();

            var newPopoBlocks = allBlocks.Select(block => new BlockViewModel(block)).ToList();

            // Replace collections in bulk
            MinerUBlocks.Clear();
            foreach (var b in newMinerUBlocks)
                MinerUBlocks.Add(b);

            PopoBlocksFlat.Clear();
            foreach (var b in newPopoBlocks)
                PopoBlocksFlat.Add(b);
        }

        // Auto-open the Popo analysis pane
        IsPopoPaneOpen = true;
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

    #region Progress Callback

    /// <summary>
    /// Called by MinerUService during parsing to report progress updates.
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

    #endregion
}