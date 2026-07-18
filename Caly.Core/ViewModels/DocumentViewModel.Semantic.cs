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
    /// Gets whether semantic analysis results are available (in-memory).
    /// </summary>
    public bool HasSemanticResults => SemanticResults?.Blocks.Count > 0;

    /// <summary>
    /// Whether semantic analysis output files exist on disk (semantic/semantic_result.json).
    /// Used to determine button visibility based on actual artifacts rather than in-memory state.
    /// </summary>
    public bool HasSemanticData
    {
        get
        {
            if (ProjectPath is null)
                return false;

            var semanticDir = Path.Combine(ProjectPath, "semantic");
            var filePath = Path.Combine(semanticDir, "semantic_result.json");
            return File.Exists(filePath);
        }
    }

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
    /// Whether semantic analysis has been completed successfully.
    /// </summary>
    public bool IsSemanticCompleted => SemanticStatus == SemanticProcessStatus.Completed;

    /// <summary>
    /// Whether semantic analysis has failed.
    /// </summary>
    public bool IsSemanticFailed => SemanticStatus == SemanticProcessStatus.Failed;

    /// <summary>
    /// Human-readable status for the NLP button tooltip.
    /// </summary>
    public string SemanticButtonTooltip
    {
        get
        {
            return SemanticStatus switch
            {
                SemanticProcessStatus.Completed => "NLP 分析已完成 · 点击重新分析",
                SemanticProcessStatus.Failed => "NLP 分析失败 · 点击重试",
                SemanticProcessStatus.Processing => $"NLP 分析中... {SemanticProgress}%",
                _ => "NLP 语义分析"
            };
        }
    }

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
        System.Diagnostics.Debug.WriteLine($"[Semantic] HasMinerUBlocks: {HasMinerUBlocks}");
        System.Diagnostics.Debug.WriteLine($"[Semantic] IsSemanticProcessing: {IsSemanticProcessing}");
        
        if (!HasMinerUBlocks)
        {
            SemanticStatus = SemanticProcessStatus.Failed;
            SemanticStatusText = "No MinerU data available. Parse with MinerU first.";
            System.Diagnostics.Debug.WriteLine("[Semantic] Failed: No MinerU data");
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

            // Collect all text para_blocks from MinerU blocks
            var nodes = CollectTextNodesFromMinerUBlocks();

            System.Diagnostics.Debug.WriteLine($"[Semantic] Collected {nodes.Count} text nodes from MinerU para_blocks");

            if (nodes.Count == 0)
            {
                SemanticStatus = SemanticProcessStatus.Failed;
                SemanticStatusText = "No text blocks found to analyze.";
                System.Diagnostics.Debug.WriteLine("[Semantic] Failed: No text blocks found");
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
                System.Diagnostics.Debug.WriteLine($"[Semantic] Auto-loaded cached results: {SemanticNodeCount} nodes, {SemanticEntityCount} entities");
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
        
        if (!File.Exists(filePath))
        {
            System.Diagnostics.Debug.WriteLine("[Semantic] File does not exist");
            return null;
        }
        
        // Read raw content for debugging
        var rawJson = File.ReadAllText(filePath);
        System.Diagnostics.Debug.WriteLine($"[Semantic] Raw JSON length: {rawJson.Length} chars");
        // Check if the JSON contains "blocks" key
        bool hasBlocksKey = rawJson.Contains("\"blocks\"");
        System.Diagnostics.Debug.WriteLine($"[Semantic] JSON contains 'blocks' key: {hasBlocksKey}");
        
        var result = SemanticAnalysisService.LoadFromFile(filePath);
        
        if (result is null)
        {
            System.Diagnostics.Debug.WriteLine("[Semantic] LoadFromFile returned null (deserialization failed)");
            return null;
        }
        
        int totalEntities = result.Blocks.Sum(b => b.Entities.Count);
        int totalRelations = result.Blocks.Sum(b => b.Relations.Count);
        System.Diagnostics.Debug.WriteLine($"[Semantic] LoadFromFile succeeded: {result.Blocks.Count} blocks, {totalEntities} entities, {totalRelations} relations");
        System.Diagnostics.Debug.WriteLine($"[Semantic] Version={result.Version}, Source={result.Source}, Timestamp={result.Timestamp}");
        
        // Log first block details for debugging
        if (result.Blocks.Count > 0)
        {
            var first = result.Blocks[0];
            System.Diagnostics.Debug.WriteLine($"[Semantic] First block: type={first.Type}, title={first.Title}, source_ids={first.SourceBlockIds?.Count ?? 0}, tokens={first.Tokens?.Count ?? 0}, entities={first.Entities?.Count ?? 0}, error={first.Error ?? "null"}");
        }
        
        // Only use cache if it has blocks
        if (result.Blocks.Count == 0)
        {
            System.Diagnostics.Debug.WriteLine("[Semantic] Cache has no blocks, will re-process");
            return null;
        }
        
        // Check for errors - allow partial results (some blocks may have errors but others are valid)
        int errorCount = result.Blocks.Count(b => !string.IsNullOrEmpty(b.Error));
        int validCount = result.Blocks.Count(b => string.IsNullOrEmpty(b.Error));
        if (errorCount > 0)
        {
            System.Diagnostics.Debug.WriteLine($"[Semantic] Cache has {errorCount} errors out of {result.Blocks.Count} blocks ({validCount} valid), loading partial results");
        }
        
        // Only reject if ALL blocks have errors
        if (validCount == 0)
        {
            System.Diagnostics.Debug.WriteLine($"[Semantic] Cache has all errors ({errorCount} blocks), will re-process");
            return null;
        }
        
        System.Diagnostics.Debug.WriteLine("[Semantic] Cache loaded successfully");
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
    /// Collects all text para_blocks from MinerUBlocks for NLP analysis.
    /// Filters for para_blocks (adopted blocks) with text content, excluding images and tables.
    /// </summary>
    private List<AnalysisTreeNode> CollectTextNodesFromMinerUBlocks()
    {
        var result = new List<AnalysisTreeNode>();
        System.Diagnostics.Debug.WriteLine($"[Semantic] CollectTextNodesFromMinerUBlocks: {MinerUBlocks.Count} blocks");
        
        foreach (var block in MinerUBlocks)
        {
            // Only analyze para_blocks (adopted) with text content
            if (!block.IsParaBlock)
            {
                System.Diagnostics.Debug.WriteLine($"[Semantic]   Skipped (not para): BlockId={block.BlockId}, Source={block.BlockSource}");
                continue;
            }
            
            if (block.IsImage)
            {
                System.Diagnostics.Debug.WriteLine($"[Semantic]   Skipped (image): BlockId={block.BlockId}");
                continue;
            }
            
            if (string.IsNullOrWhiteSpace(block.Content) || block.Content.Length <= 1)
            {
                System.Diagnostics.Debug.WriteLine($"[Semantic]   Skipped (empty): BlockId={block.BlockId}, Content='{block.Content?.Substring(0, Math.Min(30, block.Content?.Length ?? 0))}...'");
                continue;
            }
            
            // Skip tables - only analyze text/paragraph/title blocks
            if (block.Type == "table")
            {
                System.Diagnostics.Debug.WriteLine($"[Semantic]   Skipped (table): BlockId={block.BlockId}");
                continue;
            }
            
            System.Diagnostics.Debug.WriteLine($"[Semantic]   Collected: BlockId={block.BlockId}, Type={block.Type}, Content length={block.Content.Length}");
            
            var node = new AnalysisTreeNode
            {
                Type = block.Type,
                Title = block.Level > 0 ? block.Content : string.Empty,
                Content = block.Content,
                Level = block.Level,
                SourceBlockIds = new List<string>(block.SourceBlockIds)
            };
            
            result.Add(node);
        }
        
        return result;
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