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
    private StructureDocument? _structureDocument;

    partial void OnStructureDocumentChanged(StructureDocument? value)
    {
        OnPropertyChanged(nameof(HasStructureDocument));
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
    /// if a StructureDocument is already loaded. Handles lazy/late page creation.
    /// </summary>
    private void OnPagesCollectionChangedForBlocks(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || e.NewItems is null)
            return;

        var doc = StructureDocument;
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
    /// Gets whether a StructureDocument has been loaded (for UI visibility binding).
    /// </summary>
    public bool HasStructureDocument => StructureDocument is not null;

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

    /// <summary>
    /// Tree root for hierarchical Popo display (Column 3).
    /// Wraps StructureDocument.TreeRoot into a TreeNodeViewModel for TreeView binding.
    /// </summary>
    [ObservableProperty]
    private TreeNodeViewModel? _popoTreeRoot;

    /// <summary>
    /// Safe access to PopoTreeRoot.Children for TreeView ItemsSource.
    /// </summary>
    public ObservableCollection<TreeNodeViewModel>? PopoTreeRootChildren =>
        PopoTreeRoot?.Children;

    /// <summary>
    /// Total visible node count in the Popo tree (excluding the root container, for header display).
    /// Counts all descendants of the root node that are actually shown in the TreeView.
    /// </summary>
    public int PopoTreeNodeCount
    {
        get
        {
            var root = PopoTreeRoot;
            if (root is null) return 0;
            return CountDescendants(root);
        }
    }

    private static int CountDescendants(TreeNodeViewModel node)
    {
        int count = 0;
        foreach (var child in node.Children)
        {
            count += 1 + CountDescendants(child);
        }
        return count;
    }

    partial void OnPopoTreeRootChanged(TreeNodeViewModel? value)
    {
        OnPropertyChanged(nameof(PopoTreeNodeCount));
        OnPropertyChanged(nameof(PopoTreeRootChildren));
    }

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

        StructureDocument? minerUDoc = null;

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
                        minerUDoc = System.Text.Json.JsonSerializer.Deserialize<StructureDocument>(json, PopoJsonService.StructureDocumentOptions);
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
                        minerUDoc = PopoJsonService.TryParsePopoResultDir(extractDir);
                    }
                }

                // 1c: Try result_* subdirectories (Popo service output)
                if (minerUDoc is null)
                {
                    foreach (var subDir in Directory.GetDirectories(popoDir, "result_*"))
                    {
                        minerUDoc = PopoJsonService.TryParsePopoResultDir(subDir);
                        if (minerUDoc is not null)
                            break;
                    }
                }
            }
        }

        // Phase 2: Check standard outputs/ directory (sibling to PDF)
        if (minerUDoc is null && LocalPath is not null)
        {
            minerUDoc = PopoJsonService.LoadStructureDocument(LocalPath);
        }

        if (minerUDoc is null)
            return;

        // Show the analysis column when Popo data is loaded
        ShowAnalysisColumn = true;

        // Build hierarchical tree for Column 3 (matches popo_result.json tree structure)
        if (minerUDoc.TreeRoot is not null)
        {
            PopoTreeRoot = new TreeNodeViewModel(minerUDoc.TreeRoot);
        }

        // Populate flat blocks for backward compatibility
        PopoBlocks.Clear();
        foreach (var block in minerUDoc.GetAllBlocks())
        {
            PopoBlocks.Add(new BlockViewModel(block));
        }
    }
}