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
using System.Text;
using System.Text.Json;
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
        OnPropertyChanged(nameof(CanGenerateReferences));

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
                if (page.MinerUPageType == PageType.unknown && doc.PageTypes.TryGetValue(page.PageNumber, out var pageType))
                {
                    page.MinerUPageType = pageType;
                }
                if (page.MinerUPageTypeSecondary == PageType.unknown && doc.GetPageTypeSecondary(page.PageNumber) is PageType secondaryType)
                {
                    page.MinerUPageTypeSecondary = secondaryType;
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
        OnPropertyChanged(nameof(CanGenerateToc));
        OnPropertyChanged(nameof(CanGenerateReferences));
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

    // === Table of Contents (TOC) ===
    [ObservableProperty]
    private string _tocMarkdownText = string.Empty;

    [ObservableProperty]
    private bool _showTocPanel;

    /// <summary>
    /// Can generate TOC when there is a Popo tree available.
    /// </summary>
    public bool CanGenerateToc => PopoTreeRoot is not null;

    /// <summary>
    /// Generates a Table of Contents from the Popo tree and displays it in an overlay.
    /// Reads directly from the in-memory StructureDocument (parsed from popo_result.json).
    /// No intermediate files (toc.json/toc.md) are written.
    /// </summary>
    [RelayCommand]
    private void GenerateToc()
    {
        if (PopoTreeRoot is null || StructureDocument is null)
            return;

        try
        {
            // Build TOC directly from in-memory data (popo_result.json's parsed tree)
            var entries = StructureDocument.TreeRoot is not null
                ? PopoJsonService.BuildTableOfContents(StructureDocument.TreeRoot, StructureDocument)
                : new System.Collections.Generic.List<TocEntry>();

            TocMarkdownText = PopoJsonService.GenerateTocMarkdown(entries);
            ShowTocPanel = true;
        }
        catch (System.Exception ex)
        {
            TocMarkdownText = $"# 目录\n\n生成失败: {ex.Message}";
            ShowTocPanel = true;
        }
    }

    /// <summary>
    /// Closes the TOC overlay panel.
    /// </summary>
    [RelayCommand]
    private void CloseTocPanel()
    {
        ShowTocPanel = false;
    }

    // === References (参考文献) ===
    [ObservableProperty]
    private string _referenceMarkdownText = string.Empty;

    [ObservableProperty]
    private bool _showReferencePanel;

    /// <summary>
    /// Can generate references when there are pre-parsed reference entries available.
    /// </summary>
    public bool CanGenerateReferences => StructureDocument?.ReferenceEntries is { Count: > 0 };

    /// <summary>
    /// Generates a formatted reference list and displays it in an overlay.
    /// Also saves reference.json and reference.md to the project's popo/ directory.
    /// </summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task GenerateReferences()
    {
        if (StructureDocument is null)
            return;

        try
        {
            System.Collections.Generic.List<ReferenceEntry> entries;
            if (ProjectPath is not null)
            {
                var popoDir = Path.Combine(ProjectPath, "popo");
                entries = await PopoJsonService.SaveReferenceFilesAsync(StructureDocument, popoDir);
            }
            else
            {
                entries = StructureDocument.ReferenceEntries;
            }

            ReferenceMarkdownText = PopoJsonService.GenerateReferenceMarkdown(entries);
            ShowReferencePanel = true;
        }
        catch (System.Exception ex)
        {
            ReferenceMarkdownText = $"# 参考文献\n\n生成失败: {ex.Message}";
            ShowReferencePanel = true;
        }
    }

    /// <summary>
    /// Closes the reference overlay panel.
    /// </summary>
    [RelayCommand]
    private void CloseReferencePanel()
    {
        ShowReferencePanel = false;
    }

    /// <summary>
    /// Whether Popo output files exist on disk (popo/popo_result.json or popo/extract/).
    /// Used to determine column visibility based on actual artifacts rather than in-memory state.
    /// </summary>
    public bool HasPopoData
    {
        get
        {
            if (ProjectPath is null)
                return false;

            var popoDir = Path.Combine(ProjectPath, "popo");
            if (!Directory.Exists(popoDir))
                return false;

            // Check for popo_result.json
            var resultJson = Path.Combine(popoDir, "popo_result.json");
            if (File.Exists(resultJson))
                return true;

            // Check for extract subdirectory
            var extractDir = Path.Combine(popoDir, "extract");
            return Directory.Exists(extractDir);
        }
    }

    /// <summary>
    /// Attempts to load Popo analysis data for the currently opened document.
    /// Only loads from popo/popo_result.json in the project's popo/ directory.
    /// Called silently after the document is successfully opened.
    /// </summary>
    internal void TryLoadPopoData()
    {
        if (ProjectPath is null)
            return;

        StructureDocument? minerUDoc = null;

        // Check project's popo/ directory
        var popoDir = Path.Combine(ProjectPath, "popo");
        if (Directory.Exists(popoDir))
        {
            // Try popo_result.json directly
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

        if (minerUDoc is null)
            return;

        // Preserve PreprocBlocks and PageTypes from the existing MinerU data (loaded by TryLoadMinerUData)
        // PopoJsonService does not parse preproc_blocks or page_type, so we need to merge them
        if (StructureDocument?.PreprocBlocks is not null && StructureDocument.PreprocBlocks.Count > 0)
        {
            minerUDoc.PreprocBlocks = StructureDocument.PreprocBlocks;
        }
        if (StructureDocument?.PageTypes is not null && StructureDocument.PageTypes.Count > 0)
        {
            minerUDoc.PageTypes = StructureDocument.PageTypes;
        }

        // Find artifacts directory for image display.
        // Uses the MinerU extracted directory because popo_result.json's img_path (e.g., "images/xxx.jpg")
        // is relative to the hybrid_auto directory. This avoids duplicating image files.
        string? artifactsDir = null;

        // 0: Check ArtifactsDir from popo_result.json (fastest - embedded in the data itself)
        // This works even when no project is open (API response).
        if (minerUDoc.ArtifactsDir is not null)
        {
            var cachedDir = minerUDoc.ArtifactsDir;
            if (!Path.IsPathRooted(cachedDir))
            {
                // _artifactsDir is relative to the docId directory (parent of popo/), not popo/ itself.
                // e.g., popoDir = ~/.TraceView/{docId}/popo/, so baseDir = ~/.TraceView/{docId}/
                // and _artifactsDir = "mineru/{docId}/hybrid_auto" → baseDir + _artifactsDir
                var baseDir = ProjectPath ?? Path.GetDirectoryName(popoDir);
                cachedDir = Path.Combine(baseDir ?? popoDir, cachedDir);
            }
            if (Directory.Exists(cachedDir))
            {
                artifactsDir = cachedDir;
                System.Diagnostics.Debug.WriteLine($"[Popo] Using _artifactsDir from popo_result.json: {artifactsDir}");
            }
        }

        if (ProjectPath is not null)
        {
            // 1: Check project.json cache (backward compatibility)
            if (artifactsDir is null)
            {
                var projectService = App.Current?.Services?.GetService(typeof(ProjectService)) as ProjectService;
                if (projectService is not null)
                {
                    artifactsDir = projectService.GetCachedMinerUImagesDir(ProjectPath);
                }
            }

            // 2: Check MinerUService cache %LocalAppData%/Caly/mineru/{docId}/
            // This is where MinerUService extracts the ZIP files.
            // 3.x extracts to {docId}/hybrid_auto/; 4.0 extracts flat to {docId}/.
            if (artifactsDir is null && LocalPath is not null)
            {
                var docId = Path.GetFileNameWithoutExtension(LocalPath);
                var minerUCacheBase = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var minerUDocDir = Path.Combine(minerUCacheBase, "Caly", "mineru", docId);
                foreach (var candidate in new[] { Path.Combine(minerUDocDir, "hybrid_auto"), minerUDocDir })
                {
                    var imagesDir = Path.Combine(candidate, "images");
                    if (Directory.Exists(imagesDir))
                    {
                        var imageFiles = System.IO.Directory.GetFiles(imagesDir, "*.*", System.IO.SearchOption.TopDirectoryOnly)
                            .Where(f => System.IO.Path.GetExtension(f).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg")
                            .ToList();
                        if (imageFiles.Count > 0)
                        {
                            artifactsDir = candidate;
                            System.Diagnostics.Debug.WriteLine($"[Popo] Found images in MinerUService cache: {artifactsDir}");
                            break;
                        }
                    }
                }
            }
        }

        // Cache the discovered artifactsDir for next time (saves scanning on future loads)
        if (artifactsDir is not null && ProjectPath is not null)
        {
            var projectService = App.Current?.Services?.GetService(typeof(ProjectService)) as ProjectService;
            projectService?.SaveMinerUImagesDir(ProjectPath, artifactsDir);

            // If artifactsDir was found via fallback (not from popo_result.json),
            // update the JSON file so next load is instant
            if (minerUDoc.ArtifactsDir is null)
            {
                UpdatePopoResultArtifactsDir(popoDir, artifactsDir);
            }
        }

        // 3: Fallback to legacy cache directory ~/.TraceView/{docId}/mineru/
        // This ensures images load even if they were never extracted to the project directory.
        if (artifactsDir is null && LocalPath is not null)
        {
            artifactsDir = FindMinerUImagesInCache(Path.GetFileNameWithoutExtension(LocalPath));
        }

        // Assign the loaded document to StructureDocument (needed for TOC generation and other features)
        StructureDocument = minerUDoc;

        // Show the analysis column when Popo data is loaded
        ShowAnalysisColumn = true;

        // Populate block_ids from middle.json for tree nodes (Popo API returns empty block_ids)
        // Note: PopulateTreeBlockIds internally calls PopulateTreeImgPaths, so no need to call it again
        if (minerUDoc.TreeRoot is not null && artifactsDir is not null)
        {
            PopoJsonService.PopulateTreeBlockIds(minerUDoc, artifactsDir);

            // Persist resolved image paths back to popo_result.json for future loads
            PopoJsonService.UpdatePopoResultImgPaths(minerUDoc, popoDir);
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

        // Mark Popo as completed so the UI shows the checkmark icon
        PopoStatus = PopoProcessStatus.Completed;
        PopoProgress = 100;
        PopoStatusText = $"Loaded ({PopoBlocks.Count} blocks)";
        
        // Reset processing flag so ShowPopoBlocksList becomes true
        IsPopoProcessing = false;
    }

    /// <summary>
    /// Finds MinerU images in the cache directory ~/.TraceView/{docId}/mineru/.
    /// Mirrors PopoService.FindMinerUImagesDirectory for use during project reopen.
    /// </summary>
    private string? FindMinerUImagesInCache(string docId)
    {
        try
        {
            var cacheBase = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".TraceView", docId);
            var mineruCacheDir = Path.Combine(cacheBase, "mineru");

            if (!Directory.Exists(mineruCacheDir))
                return null;

            // Scan all subdirectories for artifacts (ZIP internal name may differ from docId).
            // 3.x: {sub}/hybrid_auto/; 4.0: {sub}/ flat.
            foreach (var subDir in Directory.GetDirectories(mineruCacheDir))
            {
                foreach (var candidate in new[] { Path.Combine(subDir, "hybrid_auto"), subDir })
                {
                    var imagesDir = Path.Combine(candidate, "images");
                    if (Directory.Exists(imagesDir))
                    {
                        var imageFiles = System.IO.Directory.GetFiles(imagesDir, "*.*", System.IO.SearchOption.TopDirectoryOnly)
                            .Where(f => System.IO.Path.GetExtension(f).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg")
                            .ToList();
                        if (imageFiles.Count > 0)
                            return candidate;
                    }
                }
            }
        }
        catch
        {
            // Ignore errors finding the cache directory
        }

        return null;
    }

    /// <summary>
    /// Updates popo_result.json with the discovered artifactsDir path.
    /// This is called when fallback search finds the images directory,
    /// so that next load can use the cached path instead of scanning.
    /// </summary>
    private void UpdatePopoResultArtifactsDir(string popoDir, string artifactsDir)
    {
        try
        {
            var resultPath = Path.Combine(popoDir, "popo_result.json");
            if (!File.Exists(resultPath))
                return;

            var content = File.ReadAllText(resultPath);

            try
            {
                using var doc = JsonDocument.Parse(content);
                var ms = new MemoryStream();
                using var writer = new Utf8JsonWriter(
                    ms,
                    new JsonWriterOptions { Indented = true });
                writer.WriteStartObject();

                // Write _artifactsDir first (relative path for portability)
                var relativePath = artifactsDir;
                if (ProjectPath is not null && artifactsDir.StartsWith(ProjectPath, StringComparison.OrdinalIgnoreCase))
                {
                    relativePath = Path.GetRelativePath(ProjectPath, artifactsDir);
                }
                writer.WriteString("_artifactsDir", relativePath);

                // Copy all other properties from the original JSON
                foreach (var property in doc.RootElement.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    property.Value.WriteTo(writer);
                }
                writer.WriteEndObject();
                writer.Flush();

                content = Encoding.UTF8.GetString(ms.ToArray());
                File.WriteAllText(resultPath, content);
                System.Diagnostics.Debug.WriteLine($"[Popo] Updated popo_result.json with _artifactsDir='{relativePath}'");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Popo] Failed to update popo_result.json: {ex.Message}");
                // Non-fatal: continue with original content
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Popo] Failed to read popo_result.json for update: {ex.Message}");
        }
    }
}
