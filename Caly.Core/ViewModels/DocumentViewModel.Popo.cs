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
using Caly.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;

namespace Caly.Core.ViewModels;

public sealed partial class DocumentViewModel
{
    [ObservableProperty]
    private MinerUDocument? _minerUDocument;

    partial void OnMinerUDocumentChanged(MinerUDocument? value)
    {
        OnPropertyChanged(nameof(HasMinerUDocument));
        OnPropertyChanged(nameof(HasMinerUZip));

        // Defensively ensure newly added pages get blocks assigned
        if (value is not null)
        {
            Pages.CollectionChanged += OnPagesCollectionChangedForBlocks;
        }
        else
        {
            Pages.CollectionChanged -= OnPagesCollectionChangedForBlocks;
        }
    }

    /// <summary>
    /// When new pages are added to the collection, assign their MinerU blocks
    /// if a MinerUDocument is already loaded. Handles lazy/late page creation.
    /// </summary>
    private void OnPagesCollectionChangedForBlocks(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || e.NewItems is null)
            return;

        var doc = MinerUDocument;
        if (doc is null)
            return;

        foreach (var item in e.NewItems)
        {
            if (item is PageViewModel page && page.MinerUBlocks is null)
            {
                page.MinerUBlocks = doc.GetBlocksForPage(page.PageNumber);
            }
        }
    }

    /// <summary>
    /// Gets whether a MinerUDocument has been loaded (for UI visibility binding).
    /// </summary>
    public bool HasMinerUDocument => MinerUDocument is not null;

    [ObservableProperty]
    private AnalysisViewModel? _analysisViewModel;

    [ObservableProperty]
    private bool _isPopoPaneOpen;

    /// <summary>
    /// Flat list of Popo-processed blocks for the right column of the three-column layout.
    /// Only populated when Popo processing completes (not during MinerU parse).
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<BlockViewModel> _popoBlocks = new();

    /// <summary>
    /// Gets whether Popo blocks are available for display.
    /// </summary>
    public bool HasPopoBlocks => PopoBlocks.Count > 0;

    [RelayCommand]
    private void TogglePopoPane()
    {
        IsPopoPaneOpen = !IsPopoPaneOpen;
    }

    /// <summary>
    /// Attempts to load Popo analysis data for the currently opened document.
    /// Checks in order: 1) project popo/popo.json, 2) project popo/extract/,
    /// 3) project popo/result_* subdirectories, 4) standard outputs/ directory.
    /// Called silently after the document is successfully opened.
    /// </summary>
    internal void TryLoadPopoData()
    {
        if (ProjectPath is null && LocalPath is null)
            return;

        MinerUDocument? minerUDoc = null;

        // Phase 1: Check project's popo/ directory
        if (ProjectPath is not null)
        {
            var popoDir = Path.Combine(ProjectPath, "popo");
            if (Directory.Exists(popoDir))
            {
                // 1a: Try popo.json first
                var popoJsonPath = Path.Combine(popoDir, "popo.json");
                if (File.Exists(popoJsonPath))
                {
                    try
                    {
                        var json = File.ReadAllText(popoJsonPath);
                        minerUDoc = System.Text.Json.JsonSerializer.Deserialize<MinerUDocument>(json, PopoJsonService.DefaultDeserializeOptions);
                    }
                    catch
                    {
                        // Ignore parse errors
                    }
                }

                // 1b: Try extract subdirectory
                if (minerUDoc is null)
                {
                    var extractDir = Path.Combine(popoDir, "extract");
                    if (Directory.Exists(extractDir))
                    {
                        minerUDoc = PopoJsonService.TryParseMinerUResultDir(extractDir);
                    }
                }

                // 1c: Try result_* subdirectories (Popo service output)
                if (minerUDoc is null)
                {
                    foreach (var subDir in Directory.GetDirectories(popoDir, "result_*"))
                    {
                        minerUDoc = PopoJsonService.TryParseMinerUResultDir(subDir);
                        if (minerUDoc is not null)
                            break;
                    }
                }
            }
        }

        // Phase 2: Check standard outputs/ directory (sibling to PDF)
        if (minerUDoc is null && LocalPath is not null)
        {
            minerUDoc = PopoJsonService.LoadMinerUDocument(LocalPath);
        }

        if (minerUDoc is null)
            return;

        MinerUDocument = minerUDoc;
        AnalysisViewModel = new AnalysisViewModel(minerUDoc);

        // Show the analysis column when Popo data is loaded
        ShowAnalysisColumn = true;

        // Assign blocks to each page view model
        foreach (var page in Pages)
        {
            page.MinerUBlocks = minerUDoc.GetBlocksForPage(page.PageNumber);
        }

        // Populate flat Popo blocks for the right column
        PopoBlocks.Clear();
        foreach (var block in minerUDoc.GetAllBlocks())
        {
            PopoBlocks.Add(new BlockViewModel(block));
        }
    }
}