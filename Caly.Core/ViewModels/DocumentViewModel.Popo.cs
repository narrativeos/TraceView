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
using System.Linq;
using System;

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
            if (item is PageViewModel page)
            {
                if (page.MinerUBlocks is null)
                {
                    page.MinerUBlocks = doc.GetBlocksForPage(page.PageNumber);
                }
                if (page.PreprocBlocks is null)
                {
                    page.PreprocBlocks = doc.GetPreprocBlocksForPage(page.PageNumber);
                }
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
    /// Gets whether Popo tree is available (for NLP button visibility).
    /// </summary>
    public bool HasPopoTree => PopoTreeRoot != null;

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

    /// <summary>
    /// Gets a flat list of visible Popo tree nodes, respecting the IsExpanded state.
    /// When a node is collapsed (IsExpanded=false), its children are excluded.
    /// When a node is expanded (IsExpanded=true), its children are included recursively.
    /// This is used for drawing connection lines from MinerU blocks to Popo nodes.
    /// </summary>
    public System.Collections.Generic.List<TreeNodeViewModel> VisiblePopoNodes
    {
        get
        {
            var result = new System.Collections.Generic.List<TreeNodeViewModel>();
            FlattenVisibleNodes(PopoTreeRoot?.Children, result);
            return result;
        }
    }

    private static void FlattenVisibleNodes(
        ObservableCollection<TreeNodeViewModel>? nodes,
        System.Collections.Generic.List<TreeNodeViewModel> result)
    {
        if (nodes is null || nodes.Count == 0)
            return;

        foreach (var node in nodes)
        {
            result.Add(node);

            // Only include children if the node is expanded
            if (node.IsExpanded && node.Children.Count > 0)
            {
                FlattenVisibleNodes(node.Children, result);
            }
        }
    }

    partial void OnPopoTreeRootChanged(TreeNodeViewModel? value)
    {
        OnPropertyChanged(nameof(PopoTreeNodeCount));
        OnPropertyChanged(nameof(PopoTreeRootChildren));
        OnPropertyChanged(nameof(VisiblePopoNodes));
        OnPropertyChanged(nameof(HasPopoTree));
    }

    [RelayCommand]
    private void TogglePopoPane()
    {
        IsPopoPaneOpen = !IsPopoPaneOpen;
    }

    // === Alignment Report ===
    [ObservableProperty]
    private string _alignmentReportText = string.Empty;

    /// <summary>
    /// Wrapper command that generates the alignment report and opens it in a window.
    /// Used by the top toolbar button.
    /// </summary>
    [RelayCommand]
    private void GenerateAlignmentReport()
    {
        if (StructureDocument is null)
            return;

        var report = MinerUPopoAlignmentReportService.GenerateReport(StructureDocument);
        AlignmentReportText = report.GenerateSummary();

        // Open in a new window
        var window = new Caly.Core.Views.AlignmentReportWindow(this);
        window.Show();
    }

    /// <summary>
    /// Closes the alignment report dialog (for the AnalysisView overlay).
    /// </summary>
    [RelayCommand]
    private void CloseAlignmentReport()
    {
        // No-op for the window-based approach
    }

    /// <summary>
    /// Attempts to load Popo analysis data for the currently opened document.
    /// Only loads from popo/popo_result.json in the project's popo/ directory.
    /// Called silently after the document is successfully opened.
    /// </summary>
    internal void TryLoadPopoData()
    {
        if (ProjectPath is null && LocalPath is null)
            return;

        StructureDocument? minerUDoc = null;

        // Phase 1: Check project's popo/popo_result.json directly (Popo service output)
        if (ProjectPath is not null)
        {
            var popoDir = Path.Combine(ProjectPath, "popo");
            if (Directory.Exists(popoDir))
            {
                // Try popo_result.json directly in the popo directory
                var resultJson = Path.Combine(popoDir, "popo_result.json");
                if (File.Exists(resultJson))
                {
                    minerUDoc = PopoJsonService.TryParsePopoResultJson(resultJson);
                }

                // Fallback: Try extract subdirectory
                if (minerUDoc is null)
                {
                    var extractDir = Path.Combine(popoDir, "extract");
                    if (Directory.Exists(extractDir))
                    {
                        minerUDoc = PopoJsonService.TryParsePopoResultDir(extractDir);
                    }
                }
            }
        }

        if (minerUDoc is null)
            return;


        // Preserve PreprocBlocks from the existing MinerU data (loaded by TryLoadMinerUData)
        // PopoJsonService does not parse preproc_blocks, so we need to merge them
        if (StructureDocument?.PreprocBlocks is not null && StructureDocument.PreprocBlocks.Count > 0)
        {
            minerUDoc.PreprocBlocks = StructureDocument.PreprocBlocks;
        }

        // Find artifacts directory for image display.
        // Uses the MinerU extracted directory because popo_result.json's img_path (e.g., "images/xxx.jpg")
        // is relative to the hybrid_auto directory. This avoids duplicating image files.
        string? artifactsDir = null;
        if (ProjectPath is not null)
        {
            // Get docId
            string? docId = LocalPath is not null
                ? System.IO.Path.GetFileNameWithoutExtension(LocalPath)
                : null;

            if (docId is null)
            {
                var mineruDir = Path.Combine(ProjectPath, "mineru");
                if (Directory.Exists(mineruDir))
                {
                    var zipFiles = Directory.GetFiles(mineruDir, "*.zip");
                    if (zipFiles.Length > 0)
                    {
                        // ZIP filename is now simply {docId}.zip
                        docId = Path.GetFileNameWithoutExtension(zipFiles[0]);
                    }
                }
            }

            // 1: Try MinerU cache hybrid_auto directory (~/.TraceView/{docId}/mineru/{docId}/hybrid_auto/)
            // The ZIP contains {docId}/hybrid_auto/... which extracts to {mineru}/{docId}/hybrid_auto/...
            if (docId is not null)
            {
                var cacheBase = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".TraceView", docId);
                var mineruCacheDir = Path.Combine(cacheBase, "mineru");

                // The ZIP extracts to: {mineru}/{docId}/hybrid_auto/
                var hybridAutoDir = Path.Combine(mineruCacheDir, docId, "hybrid_auto");

                if (Directory.Exists(hybridAutoDir))
                {
                    var imagesDir = Path.Combine(hybridAutoDir, "images");
                    if (Directory.Exists(imagesDir))
                    {
                        var imageFiles = System.IO.Directory.GetFiles(imagesDir, "*.*", System.IO.SearchOption.TopDirectoryOnly)
                            .Where(f => System.IO.Path.GetExtension(f).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg")
                            .ToList();
                        if (imageFiles.Count > 0)
                        {
                            artifactsDir = hybridAutoDir;
                        }
                    }
                }

                // Fallback: try the extracted directory directly (some ZIP structures may be flat)
                if (artifactsDir is null)
                {
                    var extractedDir = Path.Combine(mineruCacheDir, docId);
                    if (Directory.Exists(extractedDir))
                    {
                        var imageFiles = System.IO.Directory.GetFiles(extractedDir, "*.*", System.IO.SearchOption.AllDirectories)
                            .Where(f => System.IO.Path.GetExtension(f).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg")
                            .ToList();
                        if (imageFiles.Count > 0)
                            artifactsDir = extractedDir;
                    }
                }
            }

            // 2: Try popo/extract as fallback
            if (artifactsDir is null)
            {
                var extractDir = Path.Combine(ProjectPath, "popo", "extract");
                if (Directory.Exists(extractDir))
                {
                    var imageFiles = System.IO.Directory.GetFiles(extractDir, "*.*", System.IO.SearchOption.AllDirectories)
                        .Where(f => System.IO.Path.GetExtension(f).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg")
                        .ToList();
                    if (imageFiles.Count > 0)
                        artifactsDir = extractDir;
                }
            }

            // 3: Try all popo/ subdirectories for images
            if (artifactsDir is null)
            {
                var popoDir = Path.Combine(ProjectPath, "popo");
                if (Directory.Exists(popoDir))
                {
                    foreach (var subDir in Directory.GetDirectories(popoDir))
                    {
                        if (artifactsDir is not null) break;
                        var imageFiles = System.IO.Directory.GetFiles(subDir, "*.*", System.IO.SearchOption.AllDirectories)
                            .Where(f => System.IO.Path.GetExtension(f).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg")
                            .ToList();
                        if (imageFiles.Count > 0)
                            artifactsDir = subDir;
                    }
                }
            }
        }

        // Show the analysis column when Popo data is loaded
        ShowAnalysisColumn = true;

        // Populate block_ids from middle.json for tree nodes (Popo API returns empty block_ids)
        if (minerUDoc.TreeRoot is not null && artifactsDir is not null)
        {
            PopoJsonService.PopulateTreeBlockIds(minerUDoc, artifactsDir);
        }

        // Build hierarchical tree for Column 3 (matches popo_result.json tree structure)
        if (minerUDoc.TreeRoot is not null)
        {
            PopoTreeRoot = new TreeNodeViewModel(minerUDoc.TreeRoot, artifactsDir, minerUDoc);
        }

        // Populate flat blocks for backward compatibility
        PopoBlocks.Clear();
        foreach (var block in minerUDoc.GetAllBlocks())
        {
            PopoBlocks.Add(new BlockViewModel(block));
        }
    }
}