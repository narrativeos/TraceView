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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Caly.Core.ViewModels;

public sealed partial class DocumentViewModel
{
    #region Semantic Properties

    /// <summary>
    /// Whether the Semantic (NLP) column is visible (user toggle).
    /// </summary>
    [ObservableProperty]
    private bool _showSemanticColumn = false;

    /// <summary>
    /// Cached semantic analysis results loaded from disk.
    /// </summary>
    [ObservableProperty]
    private SemanticResultFile? _semanticResults;

    /// <summary>
    /// Gets whether semantic analysis results are available.
    /// </summary>
    public bool HasSemanticResults => SemanticResults?.Blocks.Count > 0;

    /// <summary>
    /// NLP analysis processing status.
    /// </summary>
    [ObservableProperty]
    private SemanticProcessStatus _semanticStatus = SemanticProcessStatus.Idle;

    [ObservableProperty]
    private int _semanticProgress;

    [ObservableProperty]
    private string _semanticStatusText = "Ready";

    [ObservableProperty]
    private bool _isSemanticProcessing;

    private CancellationTokenSource? _semanticCts;

    private bool _showSemanticBlocksList;
    private bool _showSemanticPlaceholder;

    /// <summary>
    /// Whether the semantic blocks list should be shown.
    /// </summary>
    public bool ShowSemanticBlocksList
    {
        get => _showSemanticBlocksList;
        private set => SetProperty(ref _showSemanticBlocksList, value);
    }

    /// <summary>
    /// Whether the placeholder (empty state) should be shown.
    /// </summary>
    public bool ShowSemanticPlaceholder
    {
        get => _showSemanticPlaceholder;
        private set => SetProperty(ref _showSemanticPlaceholder, value);
    }

    /// <summary>
    /// Updates visibility flags based on current state.
    /// </summary>
    private void UpdateSemanticVisibility()
    {
        ShowSemanticBlocksList = !IsSemanticProcessing && SemanticStatus != SemanticProcessStatus.Failed && HasSemanticResults;
        ShowSemanticPlaceholder = !IsSemanticProcessing && !HasSemanticResults && SemanticStatus != SemanticProcessStatus.Failed;
    }

    /// <summary>
    /// Total entity count across all semantic results.
    /// </summary>
    public int SemanticEntityCount
    {
        get
        {
            if (SemanticResults is null) return 0;
            return SemanticResults.Blocks.Sum(b => b.Entities.Count);
        }
    }

    /// <summary>
    /// Total relation count across all semantic results.
    /// </summary>
    public int SemanticRelationCount
    {
        get
        {
            if (SemanticResults is null) return 0;
            return SemanticResults.Blocks.Sum(b => b.Relations.Count);
        }
    }

    /// <summary>
    /// Total node count in semantic results.
    /// </summary>
    public int SemanticNodeCount => SemanticResults?.Blocks.Count ?? 0;

    /// <summary>
    /// Semantic blocks wrapped in ViewModels for UI binding with expand/collapse support.
    /// Cached to preserve IsDetailsExpanded state.
    /// </summary>
    private ObservableCollection<SemanticBlockViewModel>? _semanticBlockViewModels;

    public ObservableCollection<SemanticBlockViewModel> SemanticBlockViewModels
    {
        get
        {
            if (_semanticBlockViewModels is not null) return _semanticBlockViewModels;
            System.Diagnostics.Debug.WriteLine($"[Semantic] SemanticBlockViewModels getter: SemanticResults={SemanticResults != null}, Blocks={SemanticResults?.Blocks.Count ?? 0}");
            if (SemanticResults?.Blocks is null)
            {
                _semanticBlockViewModels = new ObservableCollection<SemanticBlockViewModel>();
                System.Diagnostics.Debug.WriteLine("[Semantic] Returning empty collection (no data)");
                return _semanticBlockViewModels;
            }
            _semanticBlockViewModels = new ObservableCollection<SemanticBlockViewModel>(
                SemanticResults.Blocks.Select(b => new SemanticBlockViewModel(b)));
            System.Diagnostics.Debug.WriteLine($"[Semantic] Created {_semanticBlockViewModels.Count} view models");
            return _semanticBlockViewModels;
        }
    }

    /// <summary>
    /// Overrides SemanticResults setter to reset cached ViewModels.
    /// </summary>
    partial void OnSemanticResultsChanged(SemanticResultFile? value)
    {
        _semanticBlockViewModels = null; // Reset cache to rebuild when accessed
        // Notify dependent properties so the UI updates correctly
        OnPropertyChanged(nameof(SemanticNodeCount));
        OnPropertyChanged(nameof(SemanticEntityCount));
        OnPropertyChanged(nameof(SemanticRelationCount));
        OnPropertyChanged(nameof(HasSemanticResults));
        // Critical: notify that SemanticBlockViewModels has changed so the ItemsControl re-reads the property
        OnPropertyChanged(nameof(SemanticBlockViewModels));
    }

    #endregion

    #region Semantic Commands

    [RelayCommand]
    private void ToggleSemanticColumn()
    {
        ShowSemanticColumn = !ShowSemanticColumn;
    }

    [RelayCommand]
    private async Task ProcessWithSemanticAsync()
    {
        System.Diagnostics.Debug.WriteLine("[Semantic] ProcessWithSemanticAsync called");
        System.Diagnostics.Debug.WriteLine($"[Semantic] PopoTreeRoot is null: {PopoTreeRoot is null}");
        System.Diagnostics.Debug.WriteLine($"[Semantic] IsSemanticProcessing: {IsSemanticProcessing}");
        
        if (PopoTreeRoot is null)
        {
            SemanticStatus = SemanticProcessStatus.Failed;
            SemanticStatusText = "No Popo data available. Process with Popo first.";
            System.Diagnostics.Debug.WriteLine("[Semantic] Failed: No Popo data");
            return;
        }

        // If already processing, cancel
        if (IsSemanticProcessing)
        {
            _semanticCts?.Cancel();
            SemanticStatusText = "Cancelling...";
            System.Diagnostics.Debug.WriteLine("[Semantic] Cancelling previous processing");
            return;
        }

        // Check if cached results exist
        var cachedResult = TryLoadSemanticResultFromFile();
        if (cachedResult is not null)
        {
            SemanticResults = cachedResult;
            SemanticStatus = SemanticProcessStatus.Completed;
            SemanticProgress = 100;
            SemanticStatusText = $"Loaded from cache ({SemanticNodeCount} nodes)";
            ShowSemanticColumn = true;
            UpdateSemanticVisibility();
            System.Diagnostics.Debug.WriteLine($"[Semantic] Loaded cached results: {SemanticNodeCount} nodes");
            return;
        }

        _semanticCts = new CancellationTokenSource();
        IsSemanticProcessing = true;
        ShowSemanticColumn = true;
        SemanticStatus = SemanticProcessStatus.Processing;
        SemanticProgress = 0;
        SemanticStatusText = "Starting NLP analysis...";
        UpdateSemanticVisibility();

        // Determine output directory
        var outputDir = GetSemanticOutputDir();
        System.Diagnostics.Debug.WriteLine($"[Semantic] Output directory: {outputDir}");

        try
        {
            using var service = new SemanticAnalysisService();

            // Collect all text nodes from the Popo tree
            var nodes = new List<AnalysisTreeNode>();
            CollectTextNodesFromTree(PopoTreeRoot.Children, nodes);

            System.Diagnostics.Debug.WriteLine($"[Semantic] Collected {nodes.Count} text nodes from tree");

            if (nodes.Count == 0)
            {
                SemanticStatus = SemanticProcessStatus.Failed;
                SemanticStatusText = "No text nodes found to analyze.";
                System.Diagnostics.Debug.WriteLine("[Semantic] Failed: No text nodes found");
                IsSemanticProcessing = false;
                _semanticCts?.Dispose();
                _semanticCts = null;
                return;
            }

            SemanticStatusText = $"Analyzing {nodes.Count} nodes...";

            var result = await service.ProcessAllNodesAsync(
                nodes,
                outputDir,
                (current, total) =>
                {
                    Dispatcher.UIThread.Invoke(() =>
                    {
                        SemanticProgress = (int)((current + 1) * 100.0 / total);
                        SemanticStatusText = $"Analyzing node {current + 1}/{total}...";
                    });
                },
                _semanticCts.Token);

            SemanticResults = result;
            SemanticStatus = SemanticProcessStatus.Completed;
            SemanticProgress = 100;
            SemanticStatusText = $"Analysis completed ({SemanticNodeCount} nodes, {SemanticEntityCount} entities, {SemanticRelationCount} relations)";
            System.Diagnostics.Debug.WriteLine($"[Semantic] Completed: {SemanticNodeCount} nodes, {SemanticEntityCount} entities, {SemanticRelationCount} relations");

            // Update tree nodes with semantic data
            UpdateTreeNodesWithSemantic(result);
            UpdateSemanticVisibility();
        }
        catch (OperationCanceledException)
        {
            SemanticStatus = SemanticProcessStatus.Idle;
            SemanticProgress = 0;
            SemanticStatusText = "Analysis cancelled";
            System.Diagnostics.Debug.WriteLine("[Semantic] Cancelled");
        }
        catch (Exception ex)
        {
            SemanticStatus = SemanticProcessStatus.Failed;
            SemanticProgress = 0;
            SemanticStatusText = $"Error: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[Semantic] Error: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[Semantic] Stack trace: {ex.StackTrace}");
        }
        finally
        {
            IsSemanticProcessing = false;
            _semanticCts?.Dispose();
            _semanticCts = null;
            UpdateSemanticVisibility();
        }
    }

    [RelayCommand]
    private void CancelSemanticProcess()
    {
        _semanticCts?.Cancel();
    }

    #endregion

    #region Semantic Data Loading

    /// <summary>
    /// Tries to load semantic analysis results from the cache directory and sets them on the ViewModel.
    /// Called automatically when the document is opened. Matches TryLoadMinerUData/TryLoadPopoData pattern.
    /// </summary>
    internal void TryLoadSemanticData()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[Semantic] TryLoadSemanticData called, ProjectPath=" + (ProjectPath ?? "null") + ", LocalPath=" + (LocalPath ?? "null"));
            var result = TryLoadSemanticResultFromFile();
            if (result is not null)
            {
                SemanticResults = result;
                SemanticStatus = SemanticProcessStatus.Completed;
                SemanticProgress = 100;
                SemanticStatusText = $"Loaded from cache ({SemanticNodeCount} nodes, {SemanticEntityCount} entities)";
                ShowSemanticColumn = true;
                UpdateSemanticVisibility();
                if (PopoTreeRoot is not null)
                {
                    UpdateTreeNodesWithSemantic(result);
                }
                System.Diagnostics.Debug.WriteLine($"[Semantic] Auto-loaded cached results: {SemanticNodeCount} nodes, {SemanticEntityCount} entities, PopoTreeRoot={PopoTreeRoot != null}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[Semantic] No cached semantic data found on document open");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Semantic] TryLoadSemanticData ERROR: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[Semantic] StackTrace: {ex.StackTrace}");
        }
    }

    /// <summary>
    /// Tries to load semantic analysis results from the cache directory.
    /// </summary>
    private SemanticResultFile? TryLoadSemanticResultFromFile()
    {
        var outputDir = GetSemanticOutputDir();
        var filePath = Path.Combine(outputDir, "semantic_result.json");
        System.Diagnostics.Debug.WriteLine($"[Semantic] TryLoadSemanticResultFromFile: checking {filePath}, exists={File.Exists(filePath)}");
        var result = SemanticAnalysisService.LoadFromFile(filePath);
        
        // Only use cache if it has no errors
        if (result is not null && result.Blocks.Count > 0)
        {
            bool hasErrors = result.Blocks.Any(b => !string.IsNullOrEmpty(b.Error));
            if (hasErrors)
            {
                System.Diagnostics.Debug.WriteLine("[Semantic] Cache has errors, will re-process");
                return null;
            }
        }
        
        return result;
    }

    /// <summary>
    /// Gets the output directory for semantic analysis results.
    /// Uses the project's semantic/ subdirectory when available, ensuring isolation between projects.
    /// </summary>
    private string GetSemanticOutputDir()
    {
        // Use the project's semantic/ directory for proper isolation between projects
        if (ProjectPath is not null)
        {
            var semanticDir = Path.Combine(ProjectPath, "semantic");
            System.Diagnostics.Debug.WriteLine($"[Semantic] GetSemanticOutputDir: using project path {semanticDir}");
            return semanticDir;
        }

        // No project path available - cannot determine a safe output directory
        // Return a path that won't exist, so TryLoadSemanticResultFromFile will return null
        System.Diagnostics.Debug.WriteLine("[Semantic] GetSemanticOutputDir: no ProjectPath available");
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".TraceView", "__no_project__", "semantic");
    }

    /// <summary>
    /// Collects all text nodes from the tree for analysis.
    /// </summary>
    private void CollectTextNodesFromTree(ObservableCollection<TreeNodeViewModel> children, List<AnalysisTreeNode> result)
    {
        System.Diagnostics.Debug.WriteLine($"[Semantic] CollectTextNodesFromTree: {children.Count} children");
        foreach (var child in children)
        {
            // Only analyze text-like nodes with content
            if (!string.IsNullOrWhiteSpace(child.Content) && child.Content.Length > 1 && !child.IsImage)
            {
                System.Diagnostics.Debug.WriteLine($"[Semantic]   Collected node: Type={child.Type}, Content length={child.Content.Length}, IsImage={child.IsImage}");
                result.Add(child.GetSourceNode());
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[Semantic]   Skipped node: Type={child.Type}, Content='{child.Content?.Substring(0, Math.Min(30, child.Content?.Length ?? 0))}...', IsImage={child.IsImage}, IsNullOrWhiteSpace={string.IsNullOrWhiteSpace(child.Content)}, Length={(child.Content?.Length ?? 0)}");
            }
            if (child.Children.Count > 0)
            {
                CollectTextNodesFromTree(child.Children, result);
            }
        }
    }

    /// <summary>
    /// Updates tree nodes with semantic analysis data for UI display.
    /// </summary>
    private void UpdateTreeNodesWithSemantic(SemanticResultFile results)
    {
        // Build lookup by source_block_ids
        var lookup = new Dictionary<string, SemanticBlockResult>();
        foreach (var block in results.Blocks)
        {
            foreach (var id in block.SourceBlockIds)
            {
                lookup[id] = block;
            }
        }

        // Update each tree node
        UpdateTreeNodeSemantic(PopoTreeRoot, lookup);
    }

    private void UpdateTreeNodeSemantic(TreeNodeViewModel node, Dictionary<string, SemanticBlockResult> lookup)
    {
        // Try to find semantic result for this node
        foreach (var id in node.SourceBlockIds)
        {
            if (lookup.TryGetValue(id, out var result))
            {
                node.SetSemanticResult(result);
                break;
            }
        }

        foreach (var child in node.Children)
        {
            UpdateTreeNodeSemantic(child, lookup);
        }
    }

    #endregion
}

/// <summary>
/// Semantic analysis processing status.
/// </summary>
public enum SemanticProcessStatus
{
    Idle,
    Processing,
    Completed,
    Failed,
}