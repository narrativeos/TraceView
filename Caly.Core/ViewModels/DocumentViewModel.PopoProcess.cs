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

    [ObservableProperty]
    private int _popoProgress;

    [ObservableProperty]
    private string _popoStatusText = "Ready";

    [ObservableProperty]
    private bool _isPopoProcessing;

    /// <summary>
    /// Whether Popo processing is enabled (reads from CalySettings).
    /// </summary>
    public bool PopoEnabled => !string.IsNullOrEmpty(CalySettings.Default.PopoBaseUrl);

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

            // Don't show button if Popo result already exists
            if (HasPopoDocument)
                return false;

            var docId = LocalPath is not null ? Path.GetFileNameWithoutExtension(LocalPath) : null;
            return PopoJsonService.FindMinerUZipInProject(ProjectPath, docId) is not null;
        }
    }

    private CancellationTokenSource? _popoCts;

    /// <summary>
    /// Cached PopoService instance per configuration key.
    /// </summary>
    private static readonly ConditionalWeakTable<string, PopoService> _popoServiceCache = new();

    #endregion

    #region Popo Service Factory

    private PopoService GetPopoService()
    {
        var settings = CalySettings.Default;
        var cacheDir = ProjectPath is not null
            ? Path.Combine(ProjectPath, "popo")
            : null;
        var cacheKey = $"{settings.PopoBaseUrl}|{cacheDir}";
        return _popoServiceCache.GetValue(cacheKey, _ => new PopoService(settings.PopoBaseUrl, cacheDir));
    }

    #endregion

    #region Popo Commands

    /// <summary>
    /// Processes the current document with Popo service.
    /// Submits the MinerU ZIP, waits for processing, downloads the result, and loads the PopoDocument.
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

        // Find the MinerU ZIP in the project
        var zipPath = PopoJsonService.FindMinerUZipInProject(ProjectPath ?? string.Empty, docId);
        if (zipPath is null)
        {
            PopoStatus = PopoProcessStatus.Failed;
            PopoStatusText = "No MinerU ZIP found. Run AI Parse first.";
            PopoProgress = 0;
            return;
        }

        // Health check
        if (!await service.HealthCheckAsync())
        {
            PopoStatus = PopoProcessStatus.Failed;
            PopoStatusText = "Popo service unavailable";
            PopoProgress = 0;
            return;
        }

        _popoCts = new CancellationTokenSource();
        IsPopoProcessing = true;
        ShowPopoColumn = true;
        PopoStatus = PopoProcessStatus.Submitting;
        PopoProgress = 0;
        PopoStatusText = PopoProcessStatus.Submitting.ToDisplayName();

        try
        {
            var result = await service.ProcessAsync(
                zipPath,
                docId,
                OnPopoProgress,
                _popoCts.Token);

            // Load result
            if (result.PopoDocument is not null)
            {
                // Update PopoDocument with the processed result
                PopoDocument = result.PopoDocument;
                PopoAnalysisViewModel = new PopoAnalysisViewModel(result.PopoDocument);

                // Assign blocks to each page
                foreach (var page in Pages)
                {
                    page.PopoBlocks = result.PopoDocument.GetBlocksForPage(page.PageNumber);
                }

                // Update flat block collection
                PopoBlocksFlat.Clear();
                foreach (var block in result.PopoDocument.GetAllBlocks())
                {
                    PopoBlocksFlat.Add(new BlockViewModel(block));
                }

                // Save to project
                if (ProjectPath is not null)
                {
                    try
                    {
                        PopoJsonService.SavePopoDocumentToProject(result.PopoDocument, ProjectPath);
                    }
                    catch
                    {
                        // Non-critical
                    }
                }
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
            PopoStatus = PopoProcessStatus.Failed;
            PopoProgress = 0;
            PopoStatusText = $"Popo failed: {ex.Message}";
        }
        catch (Exception ex)
        {
            PopoStatus = PopoProcessStatus.Failed;
            PopoProgress = 0;
            PopoStatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsPopoProcessing = false;
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