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
using Caly.Core.Services.EntityProcessors;
using Caly.Core.Services.EntityProcessors.Location;
using Caly.Core.Services.EntityProcessors.Temporal;
using Caly.Core.Services.EntityProcessors.Person;
using Caly.Core.Services.EntityProcessors.Organization;
using Caly.Core.Services.EntityProcessors.Numeric;
using Caly.Core.Services.EntityProcessors.Facility;
using Caly.Core.Services.EntityProcessors.Unknown;
using Caly.Core.Services.EntityProcessors.Term;
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

    /// <summary>Whether the Enriched column is visible.</summary>
    [ObservableProperty]
    private bool _showEnrichedColumn = false;

    /// <summary>Whether the Term Discovery column is visible.</summary>
    [ObservableProperty]
    private bool _showTermColumn = false;

    /// <summary>Whether term discovery processing is running.</summary>
    [ObservableProperty]
    private bool _isTermProcessing;

    partial void OnIsTermProcessingChanged(bool value)
    {
        OnPropertyChanged(nameof(TermButtonTooltip));
        OnPropertyChanged(nameof(TermStatusText));
    }

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
    public bool HasSemanticData => ExistsInSemanticDir("semantic_result.json");

    /// <summary>Whether enriched result exists on disk.</summary>
    public bool HasEnrichedData => ExistsInSemanticDir("enriched_result.json");

    /// <summary>Whether term discovery result exists on disk.</summary>
    public bool HasTermData => ExistsInSemanticDir("term_result.json");

    /// <summary>
    /// Whether enrichment can run: requires semantic results (in-memory).
    /// GeoJSON is preferred for location enrichment but not required — if missing,
    /// LOCATION entities will be enriched without geocoding data.
    /// </summary>
    public bool CanEnrich => HasSemanticResults;

    /// <summary>
    /// Whether term discovery can run: requires enriched data on disk (enriched_result.json).
    /// Term processing is now decoupled from enrichment — it builds term_result.json
    /// from the existing enriched_result.json without re-running the full pipeline.
    /// </summary>
    public bool CanProcessTerm => HasEnrichedData && !IsTermProcessing;

    /// <summary>
    /// Dynamic tooltip for the Term Process button.
    /// </summary>
    public string TermButtonTooltip
    {
        get
        {
            if (IsTermProcessing) return "术语发现处理中...";
            if (HasTermData) return "术语发现已完成 · 点击重新加工";
            if (!HasEnrichedData) return "请先运行 Enrich（实体富化）";
            return "运行术语发现加工";
        }
    }

    /// <summary>Status text for the Term column.</summary>
    public string TermStatusText
    {
        get
        {
            if (_termStatusText is not null) return _termStatusText;
            if (IsTermProcessing) return "术语发现处理中...";
            if (_termResults is not null)
                return $"术语发现完成: {_termResults.Terms.Count} terms, {_termResults.Relations.Count} relations";
            if (HasTermData) return "术语数据已生成 · 点击术语列切换按钮加载";
            if (!HasEnrichedData) return "请先运行 Enrich（实体富化），再生成术语数据";
            return "点击 Term Process 进行术语发现";
        }
    }

    /// <summary>
    /// Dynamic tooltip for the Enrich button reflecting current state.
    /// </summary>
    public string EnrichButtonTooltip
    {
        get
        {
            if (IsSemanticProcessing)
                return $"Enrich 处理中...";
            if (HasEnrichedData)
                return "Enrich 已完成 · 点击重新处理";
            if (!HasSemanticResults)
                return "需要先完成 NLP 语义分析";
            return "运行实体富化处理";
        }
    }

    private bool ExistsInSemanticDir(string fileName)
    {
        if (ProjectPath is null) return false;
        return File.Exists(Path.Combine(ProjectPath, "semantic", fileName));
    }

    /// <summary>
    /// NLP analysis processing status.
    /// </summary>
    [ObservableProperty]
    private SemanticProcessStatus _semanticStatus = SemanticProcessStatus.Idle;

    partial void OnSemanticStatusChanged(SemanticProcessStatus value)
    {
        OnPropertyChanged(nameof(IsSemanticCompleted));
        OnPropertyChanged(nameof(IsSemanticFailed));
        OnPropertyChanged(nameof(SemanticButtonTooltip));
    }

    [ObservableProperty]
    private int _semanticProgress;

    [ObservableProperty]
    private string _semanticStatusText = "Ready";

    [ObservableProperty]
    private bool _isSemanticProcessing;

    partial void OnIsSemanticProcessingChanged(bool value)
    {
        OnPropertyChanged(nameof(EnrichButtonTooltip));
    }

    /// <summary>
    /// Whether entity enrichment is currently running.
    /// </summary>
    [ObservableProperty]
    private bool _isEnrichProcessing;

    /// <summary>
    /// Progress percentage (0-100) for entity enrichment.
    /// </summary>
    [ObservableProperty]
    private int _enrichProgress;

    partial void OnIsEnrichProcessingChanged(bool value)
    {
        OnPropertyChanged(nameof(EnrichButtonTooltip));
        OnPropertyChanged(nameof(EnrichStatusText));
    }

    /// <summary>
    /// Status text for the Enriched column during processing.
    /// </summary>
    public string EnrichStatusText
    {
        get
        {
            if (_enrichStatusText is not null)
                return _enrichStatusText;
            if (IsEnrichProcessing)
            {
                return $"Enriching entities... {EnrichProgress}%";
            }
            if (HasEnrichedData)
            {
                var count = EnrichedEntityCount;
                var enriched = EnrichedWithSourceCount;
                return $"Enrichment complete: {count} entities ({enriched} enriched)";
            }
            if (!HasSemanticResults)
                return "Run Semantic Analysis first";
            return "Click Enrich to process entities";
        }
    }

    private string? _enrichStatusText;

    private void SetEnrichStatus(string text)
    {
        _enrichStatusText = text;
        OnPropertyChanged(nameof(EnrichStatusText));
    }

    private string? _termStatusText;

    /// <summary>Sets the term status text override (called from UI for error messages).</summary>
    public void SetTermStatus(string text)
    {
        _termStatusText = text;
        OnPropertyChanged(nameof(TermStatusText));
    }

    /// <summary>
    /// Loads existing term data from disk (term_result.json) without regenerating it.
    /// Called when user chooses "Load Existing" in the term process choice dialog.
    /// </summary>
    public void LoadExistingTermData()
    {
        TryLoadTermData();
        _termStatusText = null;
        OnPropertyChanged(nameof(TermStatusText));
        OnPropertyChanged(nameof(HasTermData));
        OnPropertyChanged(nameof(TermButtonTooltip));
    }

    private CancellationTokenSource? _semanticCts;

    /// <summary>
    /// When true, skips the cached semantic_result.json and forces a full re-process.
    /// Set by the UI before calling ProcessWithSemanticCommand for re-processing.
    /// </summary>
    private bool _forceSemanticReprocess;

    /// <summary>
    /// Marks that the next semantic processing should skip cached results and re-process.
    /// Called from the UI click handler when the user confirms re-processing.
    /// </summary>
    public void MarkSemanticForReprocess()
    {
        _forceSemanticReprocess = true;
    }

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
        // Show results as soon as semantic analysis completes, even if enrichment is still running.
        // Previously required !IsSemanticProcessing which hid results until the finally block.
        ShowSemanticBlocksList = SemanticStatus == SemanticProcessStatus.Completed && HasSemanticResults;
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

    /// <summary>Enriched entity results loaded from enriched_result.json.</summary>
    [ObservableProperty]
    private EnrichedResultFile? _enrichedResults;

    private ObservableCollection<EnrichedBlockViewModel>? _enrichedBlockViewModels;

    public ObservableCollection<EnrichedBlockViewModel> EnrichedBlockViewModels
    {
        get
        {
            if (_enrichedBlockViewModels is not null) return _enrichedBlockViewModels;
            if (EnrichedResults?.Blocks is null)
            {
                _enrichedBlockViewModels = new();
                return _enrichedBlockViewModels;
            }
            _enrichedBlockViewModels = new(
                EnrichedResults.Blocks.Select(b => new EnrichedBlockViewModel(b)));
            return _enrichedBlockViewModels;
        }
    }

    partial void OnEnrichedResultsChanged(EnrichedResultFile? value)
    {
        _enrichedBlockViewModels = null;
        OnPropertyChanged(nameof(EnrichedBlockViewModels));
        OnPropertyChanged(nameof(HasEnrichedData));
        OnPropertyChanged(nameof(EnrichButtonTooltip));
    }

    /// <summary>Term discovery results loaded from term_result.json.</summary>
    [ObservableProperty]
    private TermResultFile? _termResults;

    private TermBlockViewModel? _termBlockViewModel;

    public TermBlockViewModel TermBlockViewModel
    {
        get
        {
            if (_termBlockViewModel is not null) return _termBlockViewModel;
            if (TermResults is null)
            {
                // Return an empty ViewModel to prevent binding failures
                _termBlockViewModel = new TermBlockViewModel(new TermResultFile());
                return _termBlockViewModel;
            }
            _termBlockViewModel = new TermBlockViewModel(TermResults);
            return _termBlockViewModel;
        }
    }

    partial void OnTermResultsChanged(TermResultFile? value)
    {
        _termBlockViewModel = null;
        OnPropertyChanged(nameof(TermBlockViewModel));
        OnPropertyChanged(nameof(HasTermData));
        OnPropertyChanged(nameof(TermButtonTooltip));
        OnPropertyChanged(nameof(TermStatusText));
        // Auto-open the column when data loads (e.g., after pipeline completes)
        if (value != null)
            ShowTermColumn = true;
    }

    public int EnrichedEntityCount => EnrichedResults?.Blocks.Sum(b => b.Entities.Count) ?? 0;
    public int EnrichedWithSourceCount => EnrichedResults?.Blocks.Sum(b => b.Entities.Count(e => !string.IsNullOrEmpty(e.EnrichmentSource))) ?? 0;

    /// <summary>Load enriched_result.json from disk. Auto-regenerate if stale (empty BlockIds).</summary>
    internal void TryLoadEnrichedData()
    {
        if (ProjectPath is null)
        {
            SetEnrichStatus("Project path not set");
            return;
        }
        var enrichedPath = Path.Combine(ProjectPath, "semantic", "enriched_result.json");
        if (!File.Exists(enrichedPath))
        {
            SetEnrichStatus("Enrich 数据未生成。点击 Enrich 按钮进行加工");
            return;
        }
        try
        {
            var json = File.ReadAllText(enrichedPath);
            var loaded = System.Text.Json.JsonSerializer.Deserialize<EnrichedResultFile>(
                json, Caly.Core.Services.SourceGenerationContext.Default.EnrichedResultFile);

            if (loaded is null)
            {
                SetEnrichStatus("Enrich 数据加载失败: 反序列化返回 null");
                System.Diagnostics.Debug.WriteLine("[Semantic] Enriched data deserialization returned null");
                return;
            }

            // Check for stale data: blocks with empty BlockIds (old format before SourceBlockIds fix)
            bool hasStaleBlocks = loaded.Blocks.Any(b => b.BlockIds.Count == 0);

            EnrichedResults = loaded;
            ShowEnrichedColumn = true;
            IsEnrichProcessing = false;

            if (hasStaleBlocks)
            {
                SetEnrichStatus("Enrich 数据可能过期 (empty BlockIds)，建议重新加工");
                // Notify Term-related properties that enriched data is available (even if stale)
                OnPropertyChanged(nameof(CanProcessTerm));
                OnPropertyChanged(nameof(TermButtonTooltip));
                OnPropertyChanged(nameof(TermStatusText));
                System.Diagnostics.Debug.WriteLine("[Semantic] Loaded enriched data with stale blocks (empty BlockIds)");
            }
            else
            {
                // Clear status override so computed EnrichStatusText takes effect
                _enrichStatusText = null;
                OnPropertyChanged(nameof(EnrichStatusText));
                // Notify Term-related properties that enriched data is now available
                OnPropertyChanged(nameof(CanProcessTerm));
                OnPropertyChanged(nameof(TermButtonTooltip));
                OnPropertyChanged(nameof(TermStatusText));
                System.Diagnostics.Debug.WriteLine("[Semantic] Loaded enriched data successfully");
            }
        }
        catch (Exception ex)
        {
            SetEnrichStatus($"Enrich 数据加载失败: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[Semantic] Failed to load enriched_result.json: {ex.Message}");
        }
    }

    /// <summary>Load term_result.json from disk.</summary>
    internal void TryLoadTermData()
    {
        if (ProjectPath is null)
        {
            return;
        }
        var termPath = Path.Combine(ProjectPath, "semantic", "term_result.json");
        if (!File.Exists(termPath))
        {
            return;
        }
        try
        {
            var json = File.ReadAllText(termPath);
            var options = new System.Text.Json.JsonSerializerOptions
            {
                TypeInfoResolver = Caly.Core.Services.SourceGenerationContext.Default,
                PropertyNameCaseInsensitive = true
            };
            var loaded = System.Text.Json.JsonSerializer.Deserialize<TermResultFile>(json, options);
            if (loaded is not null)
            {
                TermResults = loaded;
                ShowTermColumn = true;
                IsTermProcessing = false;
                System.Diagnostics.Debug.WriteLine($"[Term] Loaded term_result.json ({loaded.Terms.Count} terms, {loaded.Relations.Count} relations)");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Term] Failed to load term_result.json: {ex.Message}");
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
        OnPropertyChanged(nameof(CanEnrich));
        OnPropertyChanged(nameof(CanProcessTerm));
        OnPropertyChanged(nameof(TermButtonTooltip));
        OnPropertyChanged(nameof(TermStatusText));
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

    /// <summary>
    /// Called automatically when ShowSemanticColumn changes via two-way binding (ToggleButton click).
    /// Data is pre-loaded during document open, so just toggle visibility.
    /// </summary>
    partial void OnShowSemanticColumnChanged(bool value)
    {
        if (_isInitialLoading) return;
        // Data is already loaded in LoadAllColumnsAsync()
        // Just update visibility state
        UpdateSemanticVisibility();
    }

    [RelayCommand]
    private void ToggleEnrichedColumn()
    {
        ShowEnrichedColumn = !ShowEnrichedColumn;
        if (ShowEnrichedColumn && EnrichedResults == null)
            TryLoadEnrichedData();
    }

    /// <summary>
    /// Called automatically when ShowEnrichedColumn changes via two-way binding (ToggleButton click).
    /// Ensures data is loaded when the column is opened.
    /// </summary>
    partial void OnShowEnrichedColumnChanged(bool value)
    {
        if (_isInitialLoading) return;
        if (value && EnrichedResults == null)
            TryLoadEnrichedData();
    }

    [RelayCommand]
    private void ToggleTermColumn()
    {
        ShowTermColumn = !ShowTermColumn;
        if (ShowTermColumn && TermResults == null)
            TryLoadTermData();
    }

    partial void OnShowTermColumnChanged(bool value)
    {
        if (_isInitialLoading) return;
        if (value && TermResults == null)
            TryLoadTermData();
    }

    /// <summary>
    /// Build term discovery results from already-enriched data.
    /// Does NOT re-run the enrichment pipeline — only builds term_result.json
    /// from the existing enriched_result.json on disk.
    /// Requires Enrich to have been run first.
    /// </summary>
    [RelayCommand]
    private async Task ProcessTermAsync()
    {
        if (!HasEnrichedData)
        {
            SetTermStatus("请先运行 Enrich（实体富化），再生成术语数据");
            return;
        }

        if (IsTermProcessing) return;

        // 确保术语列显示
        ShowTermColumn = true;

        IsTermProcessing = true;
        SetTermStatus("正在构建术语数据...");
        try
        {
            var outputDir = GetSemanticOutputDir();

            // ── 1. Load enriched_result.json from disk ──
            var enrichedPath = Path.Combine(outputDir, "enriched_result.json");
            if (!File.Exists(enrichedPath))
            {
                SetTermStatus("错误: enriched_result.json 不存在，请先运行 Enrich");
                return;
            }

            var enrichedJson = await File.ReadAllTextAsync(enrichedPath);
            var enrichedResult = System.Text.Json.JsonSerializer.Deserialize<EnrichedResultFile>(
                enrichedJson, Caly.Core.Services.SourceGenerationContext.Default.EnrichedResultFile);

            if (enrichedResult is null)
            {
                SetTermStatus("错误: enriched_result.json 解析失败");
                return;
            }

            // ── 2. Load semantic_result.json from disk ──
            var semanticPath = Path.Combine(outputDir, "semantic_result.json");
            SemanticResultFile? semanticResult = null;
            if (File.Exists(semanticPath))
            {
                try
                {
                    semanticResult = SemanticAnalysisService.LoadFromFile(semanticPath);
                }
                catch
                {
                    // Fallback deserialization
                    var semanticJson = await File.ReadAllTextAsync(semanticPath);
                    var options = new System.Text.Json.JsonSerializerOptions
                    {
                        TypeInfoResolver = Caly.Core.Services.SourceGenerationContext.Default,
                        PropertyNameCaseInsensitive = true,
                        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                    };
                    semanticResult = System.Text.Json.JsonSerializer.Deserialize(
                        semanticJson, typeof(SemanticResultFile), options) as SemanticResultFile;
                }
            }

            if (semanticResult is null)
            {
                // Fallback: use in-memory SemanticResults if disk file unavailable
                semanticResult = SemanticResults;
            }

            if (semanticResult is null)
            {
                SetTermStatus("错误: semantic_result.json 不存在，无法构建术语数据");
                return;
            }

            // ── 3. Collect entity metadata from enriched entities ──
            var entityMetadata = new Dictionary<string, Dictionary<string, object>>();
            foreach (var block in semanticResult.Blocks)
            {
                foreach (var entity in block.Entities)
                {
                    if (entity.EnrichedMetadata.Count > 0)
                        entityMetadata[entity.Text] = entity.EnrichedMetadata;
                }
            }

            // ── 4. Build term result ──
            var termResult = TermResultBuilder.Build(enrichedResult, semanticResult, outputDir, entityMetadata);

            // ── 5. Save term_result.json ──
            var termPath = Path.Combine(outputDir, "term_result.json");
            var termJson = System.Text.Json.JsonSerializer.Serialize(
                termResult,
                Caly.Core.Services.SourceGenerationContext.Default.TermResultFile);
            await File.WriteAllTextAsync(termPath, termJson);

            System.Diagnostics.Debug.WriteLine(
                $"[Term] Saved term_result.json ({termResult.Terms.Count} terms, {termResult.Relations.Count} relations, {termResult.TermTree.Count} root nodes)");

            // ── 6. Load into memory ──
            TryLoadTermData();

            // Clear override so computed TermStatusText takes effect
            _termStatusText = null;
            OnPropertyChanged(nameof(TermStatusText));
        }
        catch (Exception ex)
        {
            SetTermStatus($"术语加工错误: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[Term] ProcessTermAsync error: {ex.Message}");
        }
        finally
        {
            IsTermProcessing = false;
            OnPropertyChanged(nameof(HasTermData));
            OnPropertyChanged(nameof(CanProcessTerm));
            OnPropertyChanged(nameof(TermButtonTooltip));
        }
    }

    private async Task ProcessWithEnrichmentAsync()
    {
        if (SemanticResults is null) return;
        try
        {
            IsEnrichProcessing = true;
            EnrichProgress = 0;
            SetEnrichStatus("Enriching entities...");
            var outputDir = Path.Combine(ProjectPath!, "semantic");
            var enrichedResult = await RunEnrichmentPipelineAsync(SemanticResults, outputDir, CancellationToken.None);
            if (enrichedResult != null)
            {
                EnrichedResults = enrichedResult;
                SetEnrichStatus("Enrichment complete");
            }
        }
        finally
        {
            IsEnrichProcessing = false;
        }
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

        // Check if cached results exist (skip when user explicitly requested re-process)
        if (!_forceSemanticReprocess)
        {
            var cachedResult = TryLoadSemanticResultFromFile();
            if (cachedResult is not null)
            {
                SemanticResults = cachedResult;
                SemanticStatus = SemanticProcessStatus.Completed;
                SemanticProgress = 100;
                SemanticStatusText = $"Loaded from cache ({SemanticNodeCount} nodes)";
                ShowSemanticColumn = true;
                OnPropertyChanged(nameof(HasSemanticData));
                UpdateSemanticVisibility();
                System.Diagnostics.Debug.WriteLine($"[Semantic] Loaded cached results: {SemanticNodeCount} nodes");
                return;
            }
        }
        _forceSemanticReprocess = false;

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
                entityCategories: null, // Phase 5 will inject domain dictionaries
                _semanticCts.Token);

            SemanticResults = result;
            SemanticStatus = SemanticProcessStatus.Completed;
            SemanticProgress = 100;
            SemanticStatusText = $"Analysis completed ({SemanticNodeCount} nodes, {SemanticEntityCount} entities, {SemanticRelationCount} relations)";
            System.Diagnostics.Debug.WriteLine($"[Semantic] Completed: {SemanticNodeCount} nodes, {SemanticEntityCount} entities, {SemanticRelationCount} relations");
            OnPropertyChanged(nameof(HasSemanticData));

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

    /// <summary>
    /// Re-run entity enrichment on already-loaded semantic results.
    /// Useful after entity processors have been updated, or when external APIs
    /// (Nominatim, Wikidata) are available and you want fresh enrichment data.
    /// </summary>
    [RelayCommand]
    private async Task ReEnrichAsync()
    {
        if (SemanticResults == null || SemanticResults.Blocks.Count == 0)
        {
            SetEnrichStatus("No semantic results to enrich. Run Semantic Analysis first.");
            return;
        }

        if (IsEnrichProcessing)
        {
            _semanticCts?.Cancel();
            SetEnrichStatus("Cancelling...");
            return;
        }

        _semanticCts = new CancellationTokenSource();
        IsEnrichProcessing = true;
        EnrichProgress = 0;
        ShowEnrichedColumn = true;
        SetEnrichStatus("Starting entity enrichment...");

        try
        {
            var enrichedResult = await RunEnrichmentPipelineAsync(SemanticResults, GetSemanticOutputDir(), _semanticCts.Token);
            if (enrichedResult != null)
            {
                TryLoadEnrichedData();
                var count = enrichedResult.Blocks.Sum(b => b.Entities.Count);
                var enrichedCount = enrichedResult.Blocks.Sum(b =>
                    b.Entities.Count(e => !string.IsNullOrEmpty(e.EnrichmentSource)));
                SetEnrichStatus($"Enrichment complete: {count} entities ({enrichedCount} enriched)");
                System.Diagnostics.Debug.WriteLine($"[Semantic] Re-enrichment saved enriched_result.json ({count} entities, {enrichedCount} enriched)");
            }
        }
        catch (OperationCanceledException)
        {
            SetEnrichStatus("Enrichment cancelled");
        }
        catch (Exception ex)
        {
            SetEnrichStatus($"Enrichment error: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[Semantic] Re-enrichment error: {ex.Message}");
        }
        finally
        {
            IsEnrichProcessing = false;
            _semanticCts?.Dispose();
            _semanticCts = null;
        }
    }

    /// <summary>
    /// Runs the entity enrichment pipeline and saves results.
    /// Parses locations.geojson for pre-geocoded location data to avoid redundant Nominatim calls.
    /// Extracted to avoid code duplication between ProcessWithSemanticAsync and ReEnrichAsync.
    /// </summary>
    async Task<EnrichedResultFile?> RunEnrichmentPipelineAsync(
        SemanticResultFile result, string outputDir, CancellationToken ct)
    {
        // Parse locations.geojson for pre-geocoded location data
        Dictionary<string, Caly.Core.Models.EntityProcessors.GeoJsonFeature>? geoLookup = null;
        var geoJsonPath = Path.Combine(outputDir, "locations.geojson");
        if (File.Exists(geoJsonPath))
        {
            try
            {
                var geoJson = File.ReadAllText(geoJsonPath);
                geoLookup = new Dictionary<string, Caly.Core.Models.EntityProcessors.GeoJsonFeature>(
                    StringComparer.OrdinalIgnoreCase);

                using var doc = System.Text.Json.JsonDocument.Parse(geoJson);
                var root = doc.RootElement;
                if (root.TryGetProperty("features", out var features) &&
                    features.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var feature in features.EnumerateArray())
                    {
                        if (!feature.TryGetProperty("properties", out var props) ||
                            props.ValueKind != System.Text.Json.JsonValueKind.Object)
                            continue;

                        var name = GetJsonProperty(props, "name");
                        if (string.IsNullOrEmpty(name)) continue;

                        var status = GetJsonProperty(props, "status") ?? "";

                        var geoFeature = new Caly.Core.Models.EntityProcessors.GeoJsonFeature
                        {
                            Name = name,
                            DisplayName = GetJsonProperty(props, "display_name") ?? "",
                            Status = status,
                            OsmType = GetJsonProperty(props, "osm_type") ?? "",
                            OsmId = GetJsonPropertyLong(props, "osm_id"),
                            Category = GetJsonProperty(props, "category") ?? "",
                            Type = GetJsonProperty(props, "type") ?? "",
                            Addresstype = GetJsonProperty(props, "addresstype") ?? "",
                            PlaceRank = (int)GetJsonPropertyDouble(props, "place_rank"),
                            Importance = GetJsonPropertyDouble(props, "importance"),
                            Confidence = GetJsonPropertyDouble(props, "confidence"),
                            MatchType = GetJsonProperty(props, "match_type") ?? "",
                            QueryText = GetJsonProperty(props, "query_text") ?? "",
                        };

                        // Parse coordinates from geometry
                        if (status == "success" &&
                            feature.TryGetProperty("geometry", out var geometry) &&
                            geometry.ValueKind == System.Text.Json.JsonValueKind.Object &&
                            geometry.TryGetProperty("coordinates", out var coords) &&
                            coords.ValueKind == System.Text.Json.JsonValueKind.Array &&
                            coords.GetArrayLength() >= 2)
                        {
                            geoFeature.Longitude = coords[0].GetDouble();
                            geoFeature.Latitude = coords[1].GetDouble();
                        }

                        // Parse bbox from properties
                        if (props.TryGetProperty("bbox", out var bbox) &&
                            bbox.ValueKind == System.Text.Json.JsonValueKind.Array &&
                            bbox.GetArrayLength() == 4)
                        {
                            geoFeature.Bbox = new double[]
                            {
                                bbox[0].GetDouble(), bbox[1].GetDouble(),
                                bbox[2].GetDouble(), bbox[3].GetDouble()
                            };
                        }

                        // Use case-insensitive key; prefer first occurrence
                        if (!geoLookup.ContainsKey(name))
                            geoLookup[name] = geoFeature;
                    }
                }
                System.Diagnostics.Debug.WriteLine($"[Semantic] Parsed {geoLookup.Count} GeoJSON features from locations.geojson");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Semantic] Failed to parse locations.geojson: {ex.Message}");
                geoLookup = null; // Continue without GeoJSON data
            }
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("[Semantic] locations.geojson not found — LOCATION entities will not be geocoded");
        }

        var pipeline = CreateDefaultPipeline((current, total, entityText, entityType) =>
        {
            Dispatcher.UIThread.Invoke(() =>
            {
                EnrichProgress = (int)((current * 100.0) / total);
                SetEnrichStatus($"Enriching entities... {EnrichProgress}% ({entityText})");
            });
        });
        var enrichedResult = await pipeline.ProcessAsync(result, ct, geoLookup);
        if (enrichedResult != null)
        {
            var enrichedPath = Path.Combine(outputDir, "enriched_result.json");
            var json = System.Text.Json.JsonSerializer.Serialize(
                enrichedResult,
                SourceGenerationContext.Default.EnrichedResultFile);
            await File.WriteAllTextAsync(enrichedPath, json);
            System.Diagnostics.Debug.WriteLine($"[Semantic] Saved enriched_result.json ({enrichedResult.Blocks.Count} blocks)");

            // ── Build and save term discovery results ──
            // NOTE: Term processing has been decoupled from enrichment.
            // Users should click the Term Process button separately to generate term_result.json.
            // This keeps Enrich focused on entity enrichment only.
        }
        return enrichedResult;
    }

    /// <summary>Helper: reads a string property from a JsonElement.</summary>
    private static string? GetJsonProperty(System.Text.Json.JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var prop) && prop.ValueKind == System.Text.Json.JsonValueKind.String)
            return prop.GetString();
        return null;
    }

    /// <summary>Helper: reads a double property from a JsonElement.</summary>
    private static double GetJsonPropertyDouble(System.Text.Json.JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var prop) && prop.ValueKind == System.Text.Json.JsonValueKind.Number)
            return prop.GetDouble();
        return 0.0;
    }

    /// <summary>Helper: reads a long property from a JsonElement.</summary>
    private static long GetJsonPropertyLong(System.Text.Json.JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var prop) && prop.ValueKind == System.Text.Json.JsonValueKind.Number)
            return prop.GetInt64();
        return 0;
    }

    [RelayCommand]
    private void CancelSemanticProcess()
    {
        _semanticCts?.Cancel();
    }

    #endregion

    /// <summary>
    /// Creates a pipeline with all default entity processors registered.
    /// Extracted as a factory method so processor registration is in one place.
    /// </summary>
    EntityProcessorPipeline CreateDefaultPipeline(Action<int, int, string, string>? onProgress = null)
    {
        var pipeline = new EntityProcessorPipeline();
        if (onProgress is not null)
        {
            pipeline.ReportProgress(onProgress);
        }
        pipeline.AddProcessor(new LocationProcessor());
        pipeline.AddProcessor(new TemporalProcessor());
        pipeline.AddProcessor(new PersonProcessor());
        pipeline.AddProcessor(new OrganizationProcessor());
        pipeline.AddProcessor(new NumericProcessor());
        pipeline.AddProcessor(new FacilityProcessor());
        pipeline.AddProcessor(new UnknownProcessor());
        pipeline.AddProcessor(new TermProcessor());
        return pipeline;
    }

    #region Semantic Data Loading

    /// <summary>
    /// Tries to load semantic analysis results from the cache directory and sets them on the ViewModel.
    /// Called automatically when the document is opened. Matches TryLoadMinerUData/TryLoadPopoData pattern.
    /// Does NOT cascade-load Enriched or Term data (those are loaded independently in OpenDocumentCore).
    /// </summary>
    internal void TryLoadSemanticData()
    {
        try
        {
            var result = TryLoadSemanticResultFromFile();
            if (result is not null)
            {
                SemanticResults = result;
                SemanticStatus = SemanticProcessStatus.Completed;
                SemanticProgress = 100;
                SemanticStatusText = $"Loaded from cache ({SemanticNodeCount} nodes, {SemanticEntityCount} entities)";
                ShowSemanticColumn = true;
                IsSemanticProcessing = false;
                OnPropertyChanged(nameof(HasSemanticData));
                OnPropertyChanged(nameof(CanProcessTerm));
                OnPropertyChanged(nameof(TermButtonTooltip));
                OnPropertyChanged(nameof(TermStatusText));
                UpdateSemanticVisibility();
                System.Diagnostics.Debug.WriteLine($"[Semantic] Auto-loaded {SemanticNodeCount} nodes, {SemanticEntityCount} entities");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[Semantic] TryLoadSemanticResultFromFile returned null");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Semantic] TryLoadSemanticData ERROR: {ex.Message}");
            SemanticStatusText = $"ERROR: {ex.Message}";
        }
    }

    /// <summary>
    /// Tries to load semantic analysis results from the cache directory.
    /// </summary>
    private SemanticResultFile? TryLoadSemanticResultFromFile()
    {
        var diagFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "semantic_load_diag.txt");
        var logLines = new System.Collections.Generic.List<string>();
        
        var outputDir = GetSemanticOutputDir();
        var filePath = Path.Combine(outputDir, "semantic_result.json");
        logLines.Add($"TryLoadSemanticResultFromFile: checking {filePath}");
        
        if (!File.Exists(filePath))
        {
            logLines.Add("File does not exist");
            File.WriteAllLines(diagFile, logLines);
            return null;
        }
        
        logLines.Add($"File exists, attempting to read...");
        
        // Read raw content for debugging
        string rawJson;
        try
        {
            rawJson = File.ReadAllText(filePath);
            logLines.Add($"Raw JSON length: {rawJson.Length} chars");
            bool hasBlocksKey = rawJson.Contains("\"blocks\"");
            logLines.Add($"JSON contains 'blocks' key: {hasBlocksKey}");
            // Log first 500 chars for debugging
            logLines.Add($"First 500 chars: {rawJson.Substring(0, Math.Min(500, rawJson.Length))}");
        }
        catch (Exception ex)
        {
            logLines.Add($"ERROR reading file: {ex.Message}");
            File.WriteAllLines(diagFile, logLines);
            return null;
        }
        
        // Try source-gen first
        logLines.Add("Attempting SemanticAnalysisService.LoadFromFile (source-gen)...");
        SemanticResultFile? result = null;
        try
        {
            result = SemanticAnalysisService.LoadFromFile(filePath);
            if (result != null)
            {
                logLines.Add($"LoadFromFile succeeded: {result.Blocks.Count} blocks");
            }
            else
            {
                logLines.Add("LoadFromFile returned null");
            }
        }
        catch (Exception ex)
        {
            logLines.Add($"LoadFromFile exception: {ex.GetType().Name}: {ex.Message}");
        }
        
        if (result is null)
        {
            logLines.Add("Attempting fallback deserialization with SourceGenerationContext...");
            // Fallback: use SourceGenerationContext with TypeInfoResolver (reflection is disabled in .NET 10)
            try
            {
                var options = new System.Text.Json.JsonSerializerOptions
                {
                    TypeInfoResolver = SourceGenerationContext.Default,
                    PropertyNameCaseInsensitive = true,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                };
                result = System.Text.Json.JsonSerializer.Deserialize(rawJson, typeof(SemanticResultFile), options) as SemanticResultFile;
                if (result != null)
                {
                    logLines.Add($"Fallback deserialization succeeded: {result.Blocks.Count} blocks");
                }
                else
                {
                    logLines.Add("Fallback deserialization also returned null");
                }
            }
            catch (Exception ex)
            {
                logLines.Add($"Fallback deserialization exception: {ex.GetType().Name}: {ex.Message}");
                logLines.Add($"StackTrace: {ex.StackTrace}");
            }
        }
        
        if (result is null)
        {
            logLines.Add("All deserialization attempts failed");
            File.WriteAllLines(diagFile, logLines);
            return null;
        }
        
        int totalEntities = result.Blocks.Sum(b => b.Entities.Count);
        int totalRelations = result.Blocks.Sum(b => b.Relations.Count);
        logLines.Add($"Loaded: {result.Blocks.Count} blocks, {totalEntities} entities, {totalRelations} relations");
        logLines.Add($"Version={result.Version}, Source={result.Source}, Timestamp={result.Timestamp}");
        
        // Log first block details for debugging
        if (result.Blocks.Count > 0)
        {
            var first = result.Blocks[0];
            logLines.Add($"First block: type={first.Type}, title={first.Title}, source_ids={first.SourceBlockIds?.Count ?? 0}, tokens={first.Tokens?.Count ?? 0}, entities={first.Entities?.Count ?? 0}, error={first.Error ?? "null"}");
        }
        
        // Only use cache if it has blocks
        if (result.Blocks.Count == 0)
        {
            logLines.Add("Cache has no blocks, returning null");
            File.WriteAllLines(diagFile, logLines);
            return null;
        }
        
        // Check for errors - allow partial results (some blocks may have errors but others are valid)
        int errorCount = result.Blocks.Count(b => !string.IsNullOrEmpty(b.Error));
        int validCount = result.Blocks.Count(b => string.IsNullOrEmpty(b.Error));
        if (errorCount > 0)
        {
            logLines.Add($"Cache has {errorCount} errors out of {result.Blocks.Count} blocks ({validCount} valid)");
        }
        
        // Only reject if ALL blocks have errors
        if (validCount == 0)
        {
            logLines.Add($"Cache has all errors ({errorCount} blocks), returning null");
            File.WriteAllLines(diagFile, logLines);
            return null;
        }
        
        logLines.Add("Cache loaded successfully");
        File.WriteAllLines(diagFile, logLines);
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
    /// Filters for para_blocks (adopted blocks) with text content, excluding images.
    /// Tables with extracted content (from table_body HTML) are now included.
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
            
            // Skip tables only if they have no extracted content (empty table_body)
            // Tables with content from table_body HTML parsing should be included
            if (block.Type == "table" && string.IsNullOrWhiteSpace(block.Content))
            {
                System.Diagnostics.Debug.WriteLine($"[Semantic]   Skipped (empty table): BlockId={block.BlockId}");
                continue;
            }
            
            System.Diagnostics.Debug.WriteLine($"[Semantic]   Collected: BlockId={block.BlockId}, Type={block.Type}, Content length={block.Content.Length}");
            
            var node = new AnalysisTreeNode
            {
                Type = block.Type,
                Title = block.Level > 0 ? block.Content : string.Empty,
                Content = block.Content,
                Level = block.Level,
                BlockIds = new List<int> { block.Id },
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