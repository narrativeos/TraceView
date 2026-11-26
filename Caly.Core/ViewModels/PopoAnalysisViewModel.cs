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

using Avalonia.Collections;
using Caly.Core.Models;
using Caly.Core.Services;
using Caly.Core.Services.Agent;
using Caly.Core.Services.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Caly.Core.ViewModels;

/// <summary>
/// Main view model for the Popo Analysis panel.
/// Manages block list, tree view, aggregation, and markdown preview.
/// </summary>
public partial class AnalysisViewModel : ViewModelBase
{
    private readonly StructureDocument _structureDocument;
    private readonly string? _projectPath;

    /// <summary>
    /// Path to the extracted artifacts directory (images, etc.).
    /// </summary>
    public string? ArtifactsDirectory { get; }

    public AnalysisViewModel(StructureDocument structureDocument, string? artifactsDirectory = null, string? projectPath = null)
    {
        _structureDocument = structureDocument;
        _projectPath = projectPath;
        ArtifactsDirectory = artifactsDirectory;

        // Store raw blocks for lazy ViewModel creation
        _rawBlocks = structureDocument.GetAllBlocks();

        // Build tree view model and wire selection to page block highlighting
        if (structureDocument.TreeRoot is not null)
        {
            var treeRoot = new TreeNodeViewModel(structureDocument.TreeRoot, artifactsDirectory, structureDocument);
            WireTreeSelection(treeRoot, this);
            TreeRoot = treeRoot;
        }

        // Load aggregations
        Aggregations = structureDocument.Aggregations;

        // Initialize visible types
        VisibleTypes.Add("title");
        VisibleTypes.Add("text");
        VisibleTypes.Add("image");
        VisibleTypes.Add("image_footnote");
        VisibleTypes.Add("table");
        VisibleTypes.Add("caption");

        ApplyFilters();
        LoadAgentConversation();
        AgentMessages.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsConversationEmpty));
    }

    public StructureDocument StructureDocument => _structureDocument;

    // === Data availability indicators ===
    public bool HasNormalization => _structureDocument.PagesBlocks.Count > 0;
    public bool HasInference => _structureDocument.InferenceBlocks.Count > 0;
    public bool HasTree => _structureDocument.TreeRoot is not null;

    /// <summary>
    /// Human-readable status text showing which stages are available.
    /// </summary>
    public string StatusText
    {
        get
        {
            var parts = new System.Collections.Generic.List<string>();
            if (HasNormalization) parts.Add("归一化 ✓");
            if (HasInference) parts.Add("推理 ✓");
            if (HasTree) parts.Add("树形 ✓");
            return parts.Count > 0 ? string.Join(" | ", parts) : "无 Popo 数据";
        }
    }

    // === Block list ===
    /// <summary>
    /// Raw blocks source for lazy ViewModel creation.
    /// </summary>
    private IReadOnlyList<MinerUBlock> _rawBlocks = System.Array.Empty<MinerUBlock>();

    /// <summary>
    /// Lazy ViewModel factory cache: maps block index to its ViewModel.
    /// </summary>
    private System.Collections.Generic.Dictionary<int, BlockViewModel> _viewModelCache = new();

    [ObservableProperty]
    private ObservableCollection<BlockViewModel> _allBlocks = new();

    [ObservableProperty]
    private ObservableCollection<BlockViewModel> _filteredBlocks = new();

    [ObservableProperty]
    private string _blockSearchText = string.Empty;

    public HashSet<string> VisibleTypes { get; } = new();

    // === Tree view ===
    [ObservableProperty]
    private TreeNodeViewModel? _treeRoot;

    /// <summary>
    /// Safe access to TreeRoot.Children for TreeView binding.
    /// </summary>
    public ObservableCollection<TreeNodeViewModel>? TreeRootChildren =>
        TreeRoot?.Children;

    // === Aggregation ===
    [ObservableProperty]
    private IReadOnlyList<AnalysisAggregation>? _aggregations;

    partial void OnTreeRootChanged(TreeNodeViewModel? value)
    {
        // Reload aggregations from StructureDocument
        Aggregations = _structureDocument.Aggregations;
    }

    // === Selection/Highlight ===
    [ObservableProperty]
    private int? _selectedBlockId;

    [ObservableProperty]
    private BlockViewModel? _selectedBlock;

    [ObservableProperty]
    private bool _isSelectedBlockChanged;

    partial void OnSelectedBlockIdChanged(int? value)
    {
        // Clear previous selection
        foreach (var block in AllBlocks)
        {
            block.IsSelected = false;
        }

        // Highlight selected block
        if (value.HasValue)
        {
            foreach (var block in AllBlocks)
            {
                if (block.Id == value.Value)
                {
                    block.IsSelected = true;
                    SelectedBlock = block;
                    break;
                }
            }
        }
        else
        {
            SelectedBlock = null;
        }

        IsSelectedBlockChanged = !IsSelectedBlockChanged; // Trigger refresh
    }

    partial void OnSelectedBlockChanged(BlockViewModel? value)
    {
        // Sync SelectedBlockId when SelectedBlock changes (from ListBox selection)
        if (value is not null)
        {
            SelectedBlockId = value.Id;
        }
        else
        {
            SelectedBlockId = null;
        }
    }

    /// <summary>
    /// Recursively wires tree node selection to page block highlighting.
    /// When a tree node is selected, its first original block ID is used to
    /// highlight the corresponding MinerUBlock on the PDF page overlay.
    /// </summary>
    private static void WireTreeSelection(TreeNodeViewModel node, AnalysisViewModel vm)
    {
        node.OnBlockSelected = (blockId) =>
        {
            if (blockId.HasValue)
            {
                // Search by OriginalBlockIds first (tree-sourced blocks),
                // fall back to direct Id match (inference-sourced blocks)
                foreach (var block in vm.AllBlocks)
                {
                    if (block.OriginalBlockIds.Contains(blockId.Value))
                    {
                        vm.SelectedBlockId = block.Id;
                        return;
                    }
                }
                // Fallback: direct Id match
                vm.SelectedBlockId = blockId.Value;
            }
            else
            {
                vm.SelectedBlockId = null;
            }
        };

        foreach (var child in node.Children)
        {
            WireTreeSelection(child, vm);
        }
    }

    // === UI state ===
    [ObservableProperty]
    private bool _showBlockOverlay = true;

    [ObservableProperty]
    private bool _showAggregationLines = true;

    [ObservableProperty]
    private bool _isPaneOpen = true;

    // === Type filter commands ===
    [RelayCommand]
    private void ToggleTypeFilter(string type)
    {
        if (VisibleTypes.Contains(type))
        {
            VisibleTypes.Remove(type);
        }
        else
        {
            VisibleTypes.Add(type);
        }
        ApplyFilters();
    }

    [RelayCommand]
    private void SelectBlock(int id)
    {
        SelectedBlockId = id;
    }

    [RelayCommand]
    private void ResetSelection()
    {
        SelectedBlockId = null;
    }

    // === Markdown generation ===
    [ObservableProperty]
    private string _markdownText = string.Empty;

    partial void OnIsPaneOpenChanged(bool value)
    {
        if (value)
        {
            MarkdownText = GenerateMarkdown();
        }
    }

    public string GenerateMarkdown()
    {
        if (TreeRoot is null)
            return string.Empty;

        var sb = new System.Text.StringBuilder();
        GenerateMarkdownRecursive(TreeRoot, sb);
        return sb.ToString();
    }

    private void GenerateMarkdownRecursive(TreeNodeViewModel node, System.Text.StringBuilder sb)
    {
        var type = node.Type;
        var level = node.Level;
        var title = node.Title;
        var content = node.Content;

        if (type == "title")
        {
            var headers = new string('#', level + 1);
            sb.AppendLine($"{headers} {title}");
            sb.AppendLine();
        }
        else if (type == "text")
        {
            if (!string.IsNullOrEmpty(title))
                sb.AppendLine($"## {title}");

            // Nodes with ordered lists render their content as markdown
            // numbered lists (segments resolved via segment_range);
            // fall back to the plain content otherwise.
            var orderedMarkdown = PopoJsonService.GenerateOrderedListMarkdown(node.GetSourceNode());
            if (!string.IsNullOrEmpty(orderedMarkdown))
            {
                sb.AppendLine(orderedMarkdown);
            }
            else
            {
                // Replace <|txt_split|> with newlines
                sb.AppendLine(content.Replace("<|txt_split|>", "\n"));
            }
            sb.AppendLine();
        }
        else if (type == "image")
        {
            sb.AppendLine($"![{title}](image_placeholder)");
            if (!string.IsNullOrEmpty(node.Metadata))
                sb.AppendLine($"*图注: {node.Metadata}*");
            sb.AppendLine();
        }
        else if (type == "image_footnote")
        {
            sb.AppendLine($"*图注: {content}*");
            sb.AppendLine();
        }
        else if (type == "table")
        {
            // If the table has structured children (table_data / table_key_value),
            // let the children render the structured markdown to avoid duplicating
            // the raw HTML content.
            var hasStructuredChildren = node.Children.Any(c => c.Rows is not null || c.Pairs is not null);
            if (!hasStructuredChildren)
            {
                if (!string.IsNullOrEmpty(title))
                    sb.AppendLine($"### {title}");
                // Prefer the parsed HTML grid as a proper markdown table;
                // fall back to the raw content if no grid was parsed.
                var gridMarkdown = PopoJsonService.GenerateTableMarkdown(node.GetSourceNode());
                sb.AppendLine(string.IsNullOrEmpty(gridMarkdown) ? content : gridMarkdown);
                sb.AppendLine();
            }
            else if (!string.IsNullOrEmpty(title))
            {
                sb.AppendLine($"### {title}");
                sb.AppendLine();
            }
        }
        else if (type == "table_data")
        {
            if (!string.IsNullOrEmpty(title))
            {
                sb.AppendLine($"#### {title}");
            }
            sb.Append(PopoJsonService.GenerateTableMarkdown(node.GetSourceNode()));
            sb.AppendLine();
        }
        else if (type == "table_key_value")
        {
            if (!string.IsNullOrEmpty(title))
            {
                sb.AppendLine($"#### {title}");
            }
            sb.Append(PopoJsonService.GenerateTableMarkdown(node.GetSourceNode()));
            sb.AppendLine();
        }
        else if (type == "caption")
        {
            sb.AppendLine($"*{content}*");
            sb.AppendLine();
        }

        // Process children
        foreach (var child in node.Children)
        {
            GenerateMarkdownRecursive(child, sb);
        }
    }

    private void ApplyFilters()
    {
        // First, ensure AllBlocks is populated lazily from raw blocks
        EnsureAllBlocksPopulated();

        var filtered = AllBlocks
            .Where(b => VisibleTypes.Contains(b.Type))
            .ToList();

        if (!string.IsNullOrEmpty(BlockSearchText))
        {
            var search = BlockSearchText.ToLowerInvariant();
            filtered = filtered.Where(b => b.Content.ToLowerInvariant().Contains(search)).ToList();
        }

        // Clear old collection
        FilteredBlocks.Clear();
        foreach (var block in filtered)
        {
            FilteredBlocks.Add(block);
        }
    }

    /// <summary>
    /// Lazily creates BlockViewModels for all raw blocks.
    /// Only creates ViewModels that haven't been created yet.
    /// </summary>
    private void EnsureAllBlocksPopulated()
    {
        // Already populated
        if (AllBlocks.Count == _rawBlocks.Count)
            return;

        for (int i = AllBlocks.Count; i < _rawBlocks.Count; i++)
        {
            var raw = _rawBlocks[i];
            if (!_viewModelCache.TryGetValue(i, out var vm))
            {
                vm = new BlockViewModel(raw);
                _viewModelCache[i] = vm;
            }
            AllBlocks.Add(vm);
        }
    }

    partial void OnBlockSearchTextChanged(string value)
    {
        ApplyFilters();
    }

    // === Alignment Report ===
    [ObservableProperty]
    private string _alignmentReportText = string.Empty;

    [ObservableProperty]
    private bool _showAlignmentReport;

    [RelayCommand]
    internal void GenerateAlignmentReport()
    {
        var report = MinerUPopoAlignmentReportService.GenerateReport(_structureDocument);
        AlignmentReportText = report.GenerateSummary();
        ShowAlignmentReport = true;
    }

    [RelayCommand]
    private void CloseAlignmentReport()
    {
        ShowAlignmentReport = false;
    }

    #region Table of Contents

    [ObservableProperty]
    private string _tocMarkdownText = string.Empty;

    /// <summary>Structured TOC rows for the popup, styled like the 改前改后对照 rows.</summary>
    [ObservableProperty]
    private IReadOnlyList<TocEntryViewModel> _tocEntries = System.Array.Empty<TocEntryViewModel>();

    /// <summary>False only when generation failed; the popup then falls back to the message text.</summary>
    [ObservableProperty]
    private bool _hasTocRows;

    [ObservableProperty]
    private bool _showTocPanel;

    [ObservableProperty]
    private bool _isTocGenerating;

    /// <summary>
    /// Gets whether the TOC can be generated (tree data available).
    /// </summary>
    public bool CanGenerateToc => _structureDocument.TreeRoot is not null;

    /// <summary>
    /// Generates the table of contents from the in-memory StructureDocument (parsed from popo_result.json).
    /// Populates TocMarkdownText for inline preview. No intermediate files are written.
    /// </summary>
    [RelayCommand]
    private void GenerateToc()
    {
        if (_structureDocument.TreeRoot is null)
            return;

        IsTocGenerating = true;

        try
        {
            var entries = PopoJsonService.BuildTableOfContents(_structureDocument.TreeRoot, _structureDocument);
            TocMarkdownText = PopoJsonService.GenerateTocMarkdown(entries);
            TocEntries = TocEntryViewModel.From(entries);
            HasTocRows = TocEntries.Count > 0;
            ShowTocPanel = true;
            _ = LoadTocLineageAsync();
        }
        finally
        {
            IsTocGenerating = false;
        }
    }

    [RelayCommand]
    private void CloseTocPanel()
    {
        ShowTocPanel = false;
    }

    #region TOC lineage (M4-C)

    /// <summary>Head badge of the document's revision lineage: "rev N · pack · 跳变 · 编号",
    /// or a short explanation when no ledger exists yet.</summary>
    [ObservableProperty]
    private string _tocLineageBadgeText = string.Empty;

    [ObservableProperty]
    private bool _tocLineageWarning;

    [ObservableProperty]
    private bool _hasTocLineage;

    [ObservableProperty]
    private bool _isTocLineageBusy;

    /// <summary>Revision history, newest first.</summary>
    public ObservableCollection<TocRevisionEntryViewModel> TocRevisions { get; } = new();

    /// <summary>
    /// Loads the document's revision lineage sidecar from the Popo service (the ledger is
    /// a document property, not a product of TOC assembly — it loads whenever the TOC
    /// panel opens). Best effort: "谱系未开账" says the server has no ledger for this task.
    /// </summary>
    private async System.Threading.Tasks.Task LoadTocLineageAsync()
    {
        TocRevisions.Clear();
        HasTocLineage = false;
        HasTocCompare = false;

        var taskId = PopoJsonService.TryReadTaskId(_projectPath);
        if (string.IsNullOrWhiteSpace(taskId))
        {
            TocLineageBadgeText = "谱系未开账（无任务号）";
            TocLineageWarning = false;
            return;
        }

        try
        {
            var settingsService = App.Current?.Services?.GetRequiredService<ISettingsService>();
            using var service = new PopoService(settingsService?.GetSettings().PopoBaseUrl);
            var lineage = await service.FetchTocLineageAsync(taskId);
            if (lineage is null)
            {
                TocLineageBadgeText = "谱系未开账";
                TocLineageWarning = false;
                return;
            }

            foreach (var revision in lineage.Revisions.OrderByDescending(r => r.Rev))
            {
                TocRevisions.Add(new TocRevisionEntryViewModel(revision));
            }
            HasTocLineage = TocRevisions.Count > 0;

            // The compare entry lights up only when the head replay actually moved levels and
            // the tree is there to draw both sides against.
            HasTocCompare = _structureDocument.TreeRoot is not null &&
                lineage.Revisions.Count > 0 &&
                lineage.Revisions[lineage.Revisions.Count - 1].LevelDiffs.Any(d => d.Old != d.New);

            var head = TocRevisions.FirstOrDefault();
            TocLineageBadgeText = head is null
                ? "谱系已开账（尚无重放）"
                : $"rev {head.Rev} · {head.Fingerprint} · 跳变 {head.Revision.LevelJumpViolations} · " +
                  $"编号 {head.Revision.NumberingViolationCount} · 共 {lineage.Revisions.Count} 次重放";
            TocLineageWarning = head?.Warning ?? false;
        }
        catch (System.Exception ex)
        {
            TocLineageBadgeText = $"谱系读取失败：{ex.Message}";
            TocLineageWarning = true;
        }
    }

    /// <summary>
    /// "按此 rev 重放": replays the TOC stage through /retoc with the revision's recorded
    /// override tree (source: user). The ledger is append-only — the replay itself becomes
    /// the new head revision. The in-memory tree stays as displayed (same contract as the
    /// agent's retoc_document: the full tree/toc stays in the task result; reopening the
    /// document shows the new structure).
    /// </summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task ReplayAtRevisionAsync(TocRevisionEntryViewModel? entry)
    {
        if (entry is null || IsTocLineageBusy)
            return;

        var taskId = PopoJsonService.TryReadTaskId(_projectPath);
        if (string.IsNullOrWhiteSpace(taskId))
        {
            TocLineageBadgeText = "无法重放：无任务号";
            TocLineageWarning = true;
            return;
        }

        var dialogService = App.Current?.Services?.GetRequiredService<IDialogService>();
        if (dialogService is not null &&
            !await dialogService.ShowConfirmationDialogAsync(
                "按此 rev 重放",
                $"将以 rev {entry.Rev} 记录的规则包（指纹 {entry.Fingerprint}）与覆盖重写服务端目录，\n" +
                "并作为新的一条 rev 记入谱系。页内树保持当前显示。"))
            return;

        IsTocLineageBusy = true;
        try
        {
            var settingsService = App.Current?.Services?.GetRequiredService<ISettingsService>();
            using var service = new PopoService(settingsService?.GetSettings().PopoBaseUrl);
            var result = await service.RetocTaskAsync(taskId, entry.NestedOverrides);
            var levelChanges = (result?["level_diff"] as System.Text.Json.Nodes.JsonArray)?.Count ?? 0;
            var newRev = 0;
            if (result?["artifacts"]?["revision"]?["rev"] is System.Text.Json.Nodes.JsonValue revValue)
            {
                revValue.TryGetValue(out newRev);
            }

            var outcome = $"已按 rev {entry.Rev} 重放：层级变更 {levelChanges}，新 rev {newRev}";
            App.Messenger.Send(new ShowNotificationMessage(
                Avalonia.Controls.Notifications.NotificationType.Success, "目录谱系", outcome));
            await LoadTocLineageAsync();
        }
        catch (System.Exception ex)
        {
            App.Messenger.Send(new ShowNotificationMessage(
                Avalonia.Controls.Notifications.NotificationType.Error, "目录谱系重放失败", ex.Message));
        }
        finally
        {
            IsTocLineageBusy = false;
        }
    }

    #region TOC 改前改后对照 (compare view)

    [ObservableProperty]
    private bool _showTocComparePanel;

    /// <summary>Whether a 改前改后对照 exists to show: the head replay moved levels and a
    /// structure tree is loaded to align the two columns against.</summary>
    [ObservableProperty]
    private bool _hasTocCompare;

    /// <summary>Header line, e.g. "改前 r2 → 改后 r3 · 层级变更 5 · 共 40 条".</summary>
    [ObservableProperty]
    private string _tocCompareSummaryText = string.Empty;

    /// <summary>Aligned rows for the compare overlay: one heading node per row, left at its
    /// pre-replay level and right at its post-replay level.</summary>
    public ObservableCollection<TocCompareRowViewModel> TocCompareRows { get; } = new();

    /// <summary>Loads the lineage sidecar for a panel that did not open through
    /// <see cref="GenerateToc"/> (the document-level TOC popup), so the compare entry's
    /// visibility resolves there too. Best-effort; failures show in the lineage badge.</summary>
    internal System.Threading.Tasks.Task RefreshTocLineageAsync() => LoadTocLineageAsync();

    /// <summary>
    /// Builds the 改前/改后 rows from the head revision's level_diff and opens the compare
    /// overlay. The head's diff describes exactly that one replay: left is <c>old</c>, right
    /// is <c>new</c>; unchanged rows use the tree's current level on both sides (the in-memory
    /// tree may not reflect the last replay, so the diff wins wherever it speaks). Returns
    /// false when there is nothing to connect (no tree, no head, no level change matching a
    /// tree row) so callers can fall back to the inline lineage summary.
    /// </summary>
    private bool TryOpenTocCompare(PopoTocRevision? head, PopoTocRevision? previous)
    {
        if (_structureDocument.TreeRoot is null ||
            head is null ||
            !head.LevelDiffs.Any(d => d.Old != d.New))
            return false;

        var diffMap = new Dictionary<string, PopoTocLevelDiff>(head.LevelDiffs.Count);
        foreach (var diff in head.LevelDiffs)
        {
            if (!string.IsNullOrEmpty(diff.NodeId))
                diffMap[diff.NodeId] = diff;
        }

        TocCompareRows.Clear();
        var changed = 0;
        foreach (var row in TocCompareBuilder.BuildRows(_structureDocument.TreeRoot, diffMap))
        {
            if (row.IsChanged)
                changed++;
            TocCompareRows.Add(row);
        }

        // Diffs that match no tree row (foreign artifact, or a tree from another task):
        // drawing an all-unchanged compare would misreport, so fall back instead.
        if (changed == 0)
            return false;

        TocCompareSummaryText = previous is null
            ? $"改后 r{head.Rev} · 层级变更 {changed} · 共 {TocCompareRows.Count} 条"
            : $"改前 r{previous.Rev} → 改后 r{head.Rev} · 层级变更 {changed} · 共 {TocCompareRows.Count} 条";
        HasTocCompare = true;
        ShowTocComparePanel = true;
        return true;
    }

    /// <summary>Fetches the lineage and tries to open the compare view; false means nothing
    /// comparable (caller falls back to the inline lineage summary).</summary>
    private async System.Threading.Tasks.Task<bool> TryOpenTocCompareFromLineageAsync(string? taskId, string? popoBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(taskId))
            return false;

        try
        {
            using var popo = new PopoService(popoBaseUrl);
            var lineage = await popo.FetchTocLineageAsync(taskId);
            var head = lineage is { Revisions.Count: > 0 } ? lineage.Revisions[lineage.Revisions.Count - 1] : null;
            var previous = lineage is { Revisions.Count: > 1 } ? lineage.Revisions[lineage.Revisions.Count - 2] : null;
            return TryOpenTocCompare(head, previous);
        }
        catch (System.Exception)
        {
            // The fallback path reports the fetch failure in its own message.
            return false;
        }
    }

    /// <summary>The TOC popups' 改前改后对照 entry: fetches the head revision's level changes
    /// and opens the compare overlay; when nothing is comparable it only notifies, leaving
    /// the conversation untouched.</summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task OpenTocCompareAsync()
    {
        if (_structureDocument.TreeRoot is null)
            return;

        var settings = App.Current?.Services?.GetRequiredService<ISettingsService>()?.GetSettings();
        if (await TryOpenTocCompareFromLineageAsync(PopoJsonService.TryReadTaskId(_projectPath), settings?.PopoBaseUrl))
            return;

        HasTocCompare = false;
        App.Messenger.Send(new ShowNotificationMessage(
            Avalonia.Controls.Notifications.NotificationType.Information,
            "目录对照",
            "当前修订没有可展示的层级变更（需要至少一次 AI 重放并产生变更）。"));
    }

    [RelayCommand]
    private void CloseTocCompare()
    {
        ShowTocComparePanel = false;
    }

    #endregion

    #endregion

    #endregion

    #region References

    [ObservableProperty]
    private string _referenceMarkdownText = string.Empty;

    [ObservableProperty]
    private bool _showReferencePanel;

    [ObservableProperty]
    private bool _isReferenceGenerating;

    /// <summary>
    /// Gets whether the reference list can be generated (pre-parsed data available).
    /// </summary>
    public bool CanGenerateReferences => _structureDocument.ReferenceEntries is { Count: > 0 };

    /// <summary>
    /// Generates the formatted reference list and saves reference.json + reference.md
    /// files to the project's popo/ directory. Also populates ReferenceMarkdownText for inline preview.
    /// </summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task GenerateReferencesAsync()
    {
        if (_projectPath is null)
            return;

        IsReferenceGenerating = true;

        try
        {
            var popoDir = System.IO.Path.Combine(_projectPath, "popo");
            var entries = await PopoJsonService.SaveReferenceFilesAsync(_structureDocument, popoDir);
            ReferenceMarkdownText = PopoJsonService.GenerateReferenceMarkdown(entries);
            ShowReferencePanel = true;
        }
        finally
        {
            IsReferenceGenerating = false;
        }
    }

    [RelayCommand]
    private void CloseReferencePanel()
    {
        ShowReferencePanel = false;
    }

    #endregion

    #region Agent

    /// <summary>
    /// Last patch validated by the TOC alignment agent, waiting for the user to apply
    /// or discard it. Applying stays a deterministic, user-driven step: the agent can
    /// only ever propose.
    /// </summary>
    private TocAlignmentPatch? _pendingAgentPatch;

    [ObservableProperty]
    private bool _isAgentRunning;

    [ObservableProperty]
    private string _agentStatusText = string.Empty;

    [ObservableProperty]
    private string _agentPatchSummary = string.Empty;

    [ObservableProperty]
    private bool _showAgentPatchPanel;

    /// <summary>
    /// Per-tool trace of the last popo.toc-tune run (one line per tool outcome), shown
    /// in a collapsible panel so the loop's evaluate-gate and approval decisions are
    /// visible to the user rather than hidden inside the model's final prose.
    /// </summary>
    [ObservableProperty]
    private string _agentRunTraceText = string.Empty;

    [ObservableProperty]
    private bool _showAgentRunTrace;

    // === Agent 对话（conversation-first 运行体验）===

    /// <summary>The agent thread: user prompts, live run cards, inline approvals, results.</summary>
    public ObservableCollection<AgentMessage> AgentMessages { get; } = new();

    /// <summary>Drives the empty-state overlay (P01) until the first message lands.</summary>
    public bool IsConversationEmpty => AgentMessages.Count == 0;

    [ObservableProperty]
    private bool _showAgentConversation;

    /// <summary>Width of the conversation column; the resize handle writes it while dragging.</summary>
    [ObservableProperty]
    private double _agentColumnWidth = 360;

    [RelayCommand]
    private void HideAgentConversation() => ShowAgentConversation = false;

    [ObservableProperty]
    private string _agentComposerText = string.Empty;

    /// <summary>True while a run is in flight: composer input is queued, not re-run.</summary>
    [ObservableProperty]
    private bool _agentComposerIsBusy;

    [ObservableProperty]
    private string _agentConnectionStatus = string.Empty;

    /// <summary>Failure reasons surfaced in the panel banner (S01) while endpoints are broken.</summary>
    [ObservableProperty]
    private string _connectionBanner = string.Empty;

    public bool HasConnectionBanner => ConnectionBanner.Length > 0;

    partial void OnConnectionBannerChanged(string value) => OnPropertyChanged(nameof(HasConnectionBanner));

    [ObservableProperty]
    private bool _isConnectionFailing;

    /// <summary>Header dot state: green when a probe passed, red while failing, grey when never probed.</summary>
    public bool IsConnectionOk => !IsConnectionFailing && AgentConnectionStatus.Contains('✓');

    public bool IsConnectionIdle => !IsConnectionFailing && !IsConnectionOk;

    public bool HasConnectionStatus => AgentConnectionStatus.Length > 0;

    partial void OnIsConnectionFailingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsConnectionOk));
        OnPropertyChanged(nameof(IsConnectionIdle));
    }

    partial void OnAgentConnectionStatusChanged(string value)
    {
        OnPropertyChanged(nameof(IsConnectionOk));
        OnPropertyChanged(nameof(IsConnectionIdle));
        OnPropertyChanged(nameof(HasConnectionStatus));
    }

    /// <summary>Incremented by prompt seeding so the view can focus and select the composer.</summary>
    [ObservableProperty]
    private int _agentComposerFocusToken;

    private System.Threading.CancellationTokenSource? _agentRunCts;
    private AgentMessage? _activeRunCard;
    private readonly List<string> _queuedInterjections = new();

    /// <summary>4-step checklist mirrored on the run card: ✓ done, ● running, … pending, ✗ failed.</summary>
    private readonly string[] _tuneStepState = { "…", "…", "…", "…" };

    private static readonly string[] TuneStepLabels =
    {
        "读取目录质量",
        "起草最小规则覆写（等你校准）",
        "校准后重跑校验",
        "汇报改前改后对比",
    };

    private static readonly Dictionary<string, int> TuneStepByTool = new(System.StringComparer.Ordinal)
    {
        ["get_toc_quality"] = 0,
        ["propose_rule_override"] = 1,
        ["evaluate_rules"] = 2,
        ["retoc_document"] = 2,
    };

    /// <summary>Default prompt seeded into the composer by the 调优 entry button.</summary>
    private const string DefaultTocTunePrompt =
        "请检查当前文档的目录质量（层级跳变与低置信条目）；如果不理想，起草最小规则覆写并用 gold 集门禁校验；" +
        "在我校准覆写卡片后重跑目录生成，最后汇报改前改后的层级对比与规则包指纹。";

    /// <summary>
    /// Runs the popo.toc-align call site on the current document. The agent reads the
    /// structure through its bound tools and must submit a patch that passes
    /// <see cref="TocAlignmentPatcher"/> validation; the resulting diff is previewed
    /// here and applied only if the user confirms.
    /// </summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task RunTocAlignAgentAsync()
    {
        if (IsAgentRunning)
            return;

        IsAgentRunning = true;
        AgentStatusText = "TOC 对齐智能体运行中…";

        // GetRequiredService throws (it does not return null) when the service
        // cannot be activated, and AsyncRelayCommand rethrows task failures on
        // the UI thread unhandled — resolve inside the try so a DI failure
        // surfaces as a status message instead of crashing the app.
        try
        {
            var agentService = App.Current?.Services?.GetRequiredService<IAgentService>();
            if (agentService is null)
            {
                AgentStatusText = "智能体服务不可用。";
                return;
            }

            var context = new AgentToolContext { Document = _structureDocument };

            var result = await agentService.RunAsync(
                AgentCallSiteDefinitions.PopoTocAlign.Id,
                "请阅读当前文档的目录与全部正文块，完成 TOC 二次编排与正文对齐，" +
                "并通过 propose_alignment_patch 提交一个通过校验的完整补丁。",
                context);

            if (context.Items.TryGetValue(PopoTocAlignTools.SessionItemKey, out var sessionObject) &&
                sessionObject is PopoTocAlignSession { ProposedPatch: not null } session)
            {
                _pendingAgentPatch = session.ProposedPatch;

                var validation = session.LastValidation;
                AgentPatchSummary =
                    $"新目录 {_pendingAgentPatch.Nodes.Count} 个节点（当前 {_structureDocument.TocEntries.Count} 个），" +
                    $"正文块对齐 {validation?.AssignedBlocks ?? 0}/{validation?.TotalBlocks ?? 0}。";
                AgentStatusText = "补丁已生成并通过校验，请预览后应用。";
                ShowAgentPatchPanel = true;
            }
            else
            {
                _pendingAgentPatch = null;
                AgentStatusText = string.IsNullOrWhiteSpace(result.FinalText)
                    ? "智能体未能产出有效补丁。"
                    : result.FinalText;
                App.Messenger.Send(new ShowNotificationMessage(
                    Avalonia.Controls.Notifications.NotificationType.Warning,
                    "TOC 对齐", "智能体未能产出有效补丁。"));
            }
        }
        catch (System.Exception ex)
        {
            AgentStatusText = ex.Message;
            App.Messenger.Send(new ShowNotificationMessage(
                Avalonia.Controls.Notifications.NotificationType.Error, "TOC 对齐智能体", ex.Message));
        }
        finally
        {
            IsAgentRunning = false;
        }
    }

    /// <summary>
    /// Entry point for the TOC 规则调优 toolbar button. Clicking it no longer starts a run:
    /// it seeds the editable default prompt into the composer (zero side effects), and the
    /// task begins only when the user sends it from the conversation (<see cref="SendAgentPromptAsync"/>).
    /// </summary>
    [RelayCommand]
    private void SeedTocTunePrompt()
    {
        AgentComposerText = DefaultTocTunePrompt;
        ShowAgentConversation = true;
        AgentComposerFocusToken++;
    }

    /// <summary>
    /// TOC 预览弹框的「AI 重排编排」入口：关闭弹框，再按工具栏按钮的同一方式
    /// 预填可编辑调优提示词并打开对话栏（用户发送后才开始执行）。
    /// </summary>
    [RelayCommand]
    private void OpenTocTuneConversation()
    {
        ShowTocPanel = false;
        SeedTocTunePrompt();
    }

    /// <summary>
    /// Sends whatever is in the composer. When a run is in flight the message is logged and
    /// carried into the next run instead of starting one; otherwise a preflight must pass
    /// before the popo.toc-tune loop begins — no silent zero-write failures.
    /// </summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task SendAgentPromptAsync()
    {
        var prompt = AgentComposerText.Trim();
        if (prompt.Length == 0)
            return;

        AgentComposerText = string.Empty;
        var sent = AgentMessage.User(prompt);
        if (IsAgentRunning)
            sent.IsQueued = true;
        AppendConversation(sent);

        if (IsAgentRunning)
        {
            // S02: the queued chip on the bubble carries the state; no separate system note.
            _queuedInterjections.Add(prompt);
            return;
        }

        if (!await RunAgentPreflightAsync())
            return;

        await RunTocTuneConversationAsync(prompt);
    }

    /// <summary>Stops the in-flight run. Approvals still pending resolve as denied; the
    /// deterministic loop never writes past an unapproved step.</summary>
    [RelayCommand]
    private void CancelAgentRun()
    {
        _agentRunCts?.Cancel();
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task TestAgentConnectionAsync()
    {
        var settings = App.Current?.Services?.GetRequiredService<ISettingsService>()?.GetSettings();
        AgentConnectionStatus = "测试中…";

        var problems = await CollectPreflightProblemsAsync(settings);
        var ok = problems.Count == 0;
        AgentConnectionStatus = ok
            ? "配置齐备，端点可达 ✓"
            : "✗ " + string.Join("；", problems);
        IsConnectionFailing = !ok;
        ConnectionBanner = ok ? string.Empty : string.Join("；", problems);
    }

    /// <summary>
    /// Preflight before a run starts (C01): model configured, credential resolvable,
    /// LLM and Popo endpoints reachable. On failure the reason is posted into the
    /// conversation and the task never begins — zero side effects by construction.
    /// </summary>
    private async System.Threading.Tasks.Task<bool> RunAgentPreflightAsync()
    {
        var settings = App.Current?.Services?.GetRequiredService<ISettingsService>()?.GetSettings();
        var problems = await CollectPreflightProblemsAsync(settings);

        if (problems.Count == 0)
        {
            IsConnectionFailing = false;
            ConnectionBanner = string.Empty;
            return true;
        }

        // S01: the banner carries the reasons in the panel; the thread line stays for the audit log.
        IsConnectionFailing = true;
        ConnectionBanner = string.Join("；", problems);
        AppendConversation(AgentMessage.Error(
            "预检未通过：" + string.Join("；", problems) + "\n任务未开始，未产生任何写入。修好后重新发送即可。"));
        return false;
    }

    private static async System.Threading.Tasks.Task<System.Collections.Generic.List<string>> CollectPreflightProblemsAsync(CalySettings? settings)
    {
        var problems = new System.Collections.Generic.List<string>();

        if (string.IsNullOrWhiteSpace(settings?.AiModel))
            problems.Add("未配置智能体模型");

        if (string.IsNullOrWhiteSpace(settings?.AiCredentialRef))
        {
            problems.Add("未配置 API 密钥引用");
        }
        else if (App.Current?.Services?.GetRequiredService<ISecretStore>() is { } store &&
                 !store.TryResolve(settings!.AiCredentialRef, out _))
        {
            problems.Add("密钥引用无法解析（密钥未存入或环境变量缺失）");
        }

        if (!string.IsNullOrWhiteSpace(settings?.AiBaseUrl))
        {
            var error = await ProbeTcpEndpointAsync(settings!.AiBaseUrl);
            if (error is not null)
                problems.Add($"LLM 端点不可达（{settings!.AiBaseUrl}：{error}）");
        }

        var popoUrl = settings?.PopoBaseUrl;
        if (!string.IsNullOrWhiteSpace(popoUrl))
        {
            var error = await ProbeTcpEndpointAsync(popoUrl!);
            if (error is not null)
                problems.Add($"Popo 服务不可达（{popoUrl}：{error}）");
        }

        return problems;
    }

    /// <summary>
    /// TCP-connect probe for a base URL. Returns null when reachable, otherwise a short
    /// failure reason (distinguishing timeout from refusal, e.g. a denied macOS
    /// Local Network permission surfaces as NetworkUnreachable) so the problem line is actionable.
    /// </summary>
    private static async System.Threading.Tasks.Task<string?> ProbeTcpEndpointAsync(string baseUrl)
    {
        using var probe = new System.Net.Sockets.TcpClient();
        using var timeout = new System.Threading.CancellationTokenSource(System.TimeSpan.FromSeconds(4));
        try
        {
            var uri = new System.Uri(baseUrl);
            await probe.ConnectAsync(uri.Host, uri.IsDefaultPort ? (uri.Scheme == "https" ? 443 : 80) : uri.Port, timeout.Token);
            return null;
        }
        catch (System.OperationCanceledException)
        {
            return "连接超时";
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            return ex.SocketErrorCode.ToString();
        }
        catch (System.Exception ex)
        {
            return ex.GetType().Name;
        }
    }

    /// <summary>
    /// Conversation-driven popo.toc-tune run: a live 4-step card in the thread, tool events
    /// as they happen, inline approval instead of a modal dialog, and a completion card with
    /// the r(N-1)→rN lineage and the undo entry point.
    /// </summary>
    private async System.Threading.Tasks.Task RunTocTuneConversationAsync(string userPrompt)
    {
        IsAgentRunning = true;
        AgentComposerIsBusy = true;
        for (var i = 0; i < _tuneStepState.Length; i++)
            _tuneStepState[i] = "…";

        var card = new AgentMessage
        {
            Kind = AgentMessageKind.RunCard,
            RoleLabel = "智能体",
            Text = BuildTuneCardText(),
        };
        foreach (var label in TuneStepLabels)
            card.Steps.Add(new AgentRunStep { Label = label });
        _activeRunCard = card;
        AppendConversation(card);

        var startedAt = System.DateTime.Now;
        _agentRunCts = new System.Threading.CancellationTokenSource();

        try
        {
            var agentService = App.Current?.Services?.GetRequiredService<IAgentService>();
            if (agentService is null)
            {
                card.Status = "✗ 智能体服务不可用。";
                AppendConversation(AgentMessage.Error("智能体服务不可用。"));
                return;
            }

            var context = new AgentToolContext { Document = _structureDocument };

            var settings = App.Current?.Services?.GetRequiredService<ISettingsService>()?.GetSettings();
            if (!string.IsNullOrWhiteSpace(settings?.PopoBaseUrl))
            {
                context.Items[TocRuleTuningTools.BaseUrlItemKey] = settings!.PopoBaseUrl;
            }

            var taskId = PopoJsonService.TryReadTaskId(_projectPath);
            if (!string.IsNullOrWhiteSpace(taskId))
            {
                context.Items[TocRuleTuningTools.TaskIdItemKey] = taskId;
            }

            var prompt = userPrompt;
            if (_queuedInterjections.Count > 0)
            {
                prompt = string.Join("；", _queuedInterjections) + "\n\n" + prompt;
                _queuedInterjections.Clear();
            }

            context.Items[AgentToolProgressKeys.ItemKey] =
                new System.Action<string, string>(
                    (name, state) => Avalonia.Threading.Dispatcher.UIThread.Post(
                        () => OnAgentToolProgress(card, name, state, startedAt)));

            AgentRunTraceText = string.Empty;
            ShowAgentRunTrace = false;

            var result = await agentService.RunAsync(
                AgentCallSiteDefinitions.PopoTocTune.Id,
                prompt,
                context,
                BuildTocTuneApprover(),
                _agentRunCts.Token);

            AgentRunTraceText = string.Join("\n", result.ToolTrace);
            ShowAgentRunTrace = result.ToolTrace.Count > 0;

            SetTuneStep(3, "✓");
            card.Status = $"完成（{(System.DateTime.Now - startedAt).ToString("mm\\:ss")}）";

            AppendConversation(AgentMessage.Agent(
                string.IsNullOrWhiteSpace(result.FinalText) ? "智能体未产出调优结论。" : result.FinalText));

            await AppendCompletionCardAsync();
        }
        catch (System.OperationCanceledException)
        {
            card.Status = "已取消。未通过校准的覆写不会写入；若批准已发生，新修订保留。";
            AppendConversation(AgentMessage.SystemNote("运行已取消。"));
        }
        catch (System.Exception ex)
        {
            card.Status = "✗ 失败";
            AppendConversation(AgentMessage.Error(ex.Message));
            App.Messenger.Send(new ShowNotificationMessage(
                Avalonia.Controls.Notifications.NotificationType.Error, "TOC 规则调优智能体", ex.Message));
        }
        finally
        {
            _activeRunCard = null;
            _agentRunCts.Dispose();
            _agentRunCts = null;
            IsAgentRunning = false;
            AgentComposerIsBusy = false;
        }
    }

    private void OnAgentToolProgress(AgentMessage card, string toolName, string state, System.DateTime startedAt)
    {
        var elapsed = (System.DateTime.Now - startedAt).ToString("mm\\:ss");
        AgentRunTraceText += $"\n{System.DateTime.Now:HH:mm:ss} {toolName} {state}";
        ShowAgentRunTrace = true;
        card.TraceText += $"{elapsed} {toolName} {state}\n";

        if (!TuneStepByTool.TryGetValue(toolName, out var step))
            return;

        // start → running; done on the tool that ends a step marks the step done.
        SetTuneStep(step, state == "start" ? "●" : state == "done" ? "✓" : "✗");
    }

    private void SetTuneStep(int index, string glyph)
    {
        // A later running step implies the earlier ones finished.
        for (var i = 0; i < index; i++)
            if (_tuneStepState[i] != "✓")
                _tuneStepState[i] = "✓";

        _tuneStepState[index] = glyph;
        if (_activeRunCard is { } card)
        {
            card.Text = BuildTuneCardText();
            for (var i = 0; i < card.Steps.Count && i < _tuneStepState.Length; i++)
                card.Steps[i].State = StepStateFromGlyph(_tuneStepState[i]);
        }
    }

    private static AgentStepState StepStateFromGlyph(string glyph) => glyph switch
    {
        "✓" => AgentStepState.Done,
        "●" => AgentStepState.Running,
        "✗" => AgentStepState.Failed,
        _ => AgentStepState.Pending,
    };

    private string BuildTuneCardText()
    {
        var lines = new System.Text.StringBuilder();
        lines.Append("TOC 规则调优 · 运行中\n");
        for (var i = 0; i < TuneStepLabels.Length; i++)
            lines.Append($"{_tuneStepState[i]} {TuneStepLabels[i]}\n");
        return lines.ToString().TrimEnd();
    }

    private void AppendConversation(AgentMessage message)
    {
        AgentMessages.Add(message);
        ShowAgentConversation = true;
        AgentConversationLog.Append(_projectPath, message);
    }

    private void LoadAgentConversation()
    {
        try
        {
            var restored = AgentConversationLog.Load(_projectPath);
            if (restored.Count == 0)
                return;

            foreach (var message in restored)
                AgentMessages.Add(message);

            // Commands cannot be serialized; re-attach the non-destructive compare and the
            // undo entry to the last completion card so the undo path survives a reload.
            var lastDone = restored.FindLast(m => m.Kind == AgentMessageKind.Done);
            if (lastDone is not null)
            {
                var settings = App.Current?.Services?.GetRequiredService<ISettingsService>()?.GetSettings();
                lastDone.ApproveLabel = "查看改前改后对比";
                lastDone.DenyLabel = "撤销到上一修订";
                lastDone.ApproveCommand = new AsyncRelayCommand(async () =>
                {
                    var compareTaskId = PopoJsonService.TryReadTaskId(_projectPath);
                    if (!await TryOpenTocCompareFromLineageAsync(compareTaskId, settings?.PopoBaseUrl))
                        await AppendLineageComparisonAsync(compareTaskId, settings?.PopoBaseUrl);
                });
                lastDone.DenyCommand = new AsyncRelayCommand(async () =>
                {
                    lastDone.ShowActions = false;
                    await UndoToPreviousRevisionAsync(PopoJsonService.TryReadTaskId(_projectPath), settings?.PopoBaseUrl);
                });
                lastDone.ShowActions = true;
            }

            AgentMessages.Add(AgentMessage.SystemNote($"已恢复 {restored.Count} 条历史对话（本地 JSONL）。"));
            ShowAgentConversation = true;
        }
        catch (System.Exception)
        {
            // A corrupt log must never block opening the document.
        }
    }

    /// <summary>
    /// Inline approval for the popo.toc-tune write: instead of a separate dialog window the
    /// run blocks on an approval card inside the conversation, showing the exact write,
    /// the arguments and the undo path. A missing UI dispatcher fails closed.
    /// </summary>
    private AgentToolApprover BuildTocTuneApprover() => async (request, ct) =>
    {
        var completion = new System.Threading.Tasks.TaskCompletionSource<bool>(
            System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);

        var approval = new AgentMessage
        {
            Kind = AgentMessageKind.Approval,
            RoleLabel = "智能体",
            Text = $"本次要写入：重跑目录生成（新修订）\n工具：{request.ToolName}（站点 {request.CallSiteId}）\n参数：{request.ArgumentsSummary}\n当前目录会被服务端标为已替换；完成卡内可一键撤销。",
            ShowActions = true,
            Status = "等待校准…",
        };

        approval.ApproveCommand = new RelayCommand(() =>
        {
            approval.ShowActions = false;
            approval.Status = "已校准，重跑开始。";
            AppendConversation(AgentMessage.SystemNote($"覆写已批准：{request.ArgumentsSummary}"));
            completion.TrySetResult(true);
        });

        approval.DenyCommand = new RelayCommand(() =>
        {
            approval.ShowActions = false;
            approval.Status = "已拒绝，按零写入收尾。";
            AppendConversation(AgentMessage.SystemNote("覆写已拒绝：保持当前修订不变。"));
            completion.TrySetResult(false);
        });

        ct.Register(() =>
        {
            approval.ShowActions = false;
            approval.Status = "已取消。";
            completion.TrySetResult(false);
        });

        // Append the card on the UI thread whether or not we are already there
        // (InvokeAsync executes inline when called from the UI thread).
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => AppendConversation(approval));
        return await completion.Task.WaitAsync(ct);
    };

    /// <summary>
    /// Completion card with the lineage comparison entry (P05) and the undo entry (P04),
    /// built from the Popo revision ledger for the task bound to this document.
    /// </summary>
    private async System.Threading.Tasks.Task AppendCompletionCardAsync()
    {
        var taskId = PopoJsonService.TryReadTaskId(_projectPath);
        var settings = App.Current?.Services?.GetRequiredService<ISettingsService>()?.GetSettings();

        string text;
        PopoTocRevision? head = null;
        PopoTocRevision? previous = null;

        if (string.IsNullOrWhiteSpace(taskId))
        {
            text = "目录重写完成。本次运行未绑定 Popo 任务，无法展示修订谱系。";
        }
        else
        {
            try
            {
                using var popo = new PopoService(settings?.PopoBaseUrl);
                var lineage = await popo.FetchTocLineageAsync(taskId!);
                head = lineage.Revisions.Count > 0 ? lineage.Revisions[lineage.Revisions.Count - 1] : null;
                previous = lineage.Revisions.Count > 1 ? lineage.Revisions[lineage.Revisions.Count - 2] : null;
                text = previous is null || head is null
                    ? $"目录重写完成，当前修订 r{lineage.HeadRevision}。这是首个修订，没有改前改后对比。"
                    : $"目录重写完成 · 改前 r{previous.Rev} → 改后 r{head.Rev}。";
            }
            catch (System.Exception ex)
            {
                text = "目录重写完成，但修订谱系读取失败：" + ex.Message;
            }
        }

        var card = new AgentMessage
        {
            Kind = AgentMessageKind.Done,
            RoleLabel = "智能体",
            Text = text,
            ShowActions = true,
            ApproveLabel = "查看改前改后对比",
            DenyLabel = "撤销到上一修订",
        };

        // P05: the card carries stat chips and the head's override summary; the raw
        // override JSON and the full lineage stay out of the thread text.
        if (head is not null)
        {
            card.Stats.Add(new AgentStatChip
            {
                Label = "修订",
                Value = previous is null ? $"r{head.Rev}" : $"r{head.Rev}（基线 r{previous.Rev}）",
            });
            card.Stats.Add(new AgentStatChip { Label = "层级", Value = head.MaxDepth.ToString(CultureInfo.InvariantCulture) });
            card.Stats.Add(new AgentStatChip { Label = "跳变", Value = head.LevelJumpViolations.ToString(CultureInfo.InvariantCulture) });
            card.Stats.Add(new AgentStatChip { Label = "编号违规", Value = head.NumberingViolationCount.ToString(CultureInfo.InvariantCulture) });

            if (head.Overrides is { Count: > 0 } headOverrides)
            {
                foreach (var pair in headOverrides)
                    card.Bullets.Add($"{pair.Key} = {TruncateForDisplay(pair.Value?.ToJsonString() ?? "null", 200)}");
            }
            else
            {
                card.Bullets.Add("基线覆写（本修订未携带自定义覆写）");
            }
        }

        card.ApproveCommand = new AsyncRelayCommand(async () =>
        {
            // Compare is non-destructive and repeatable; unlike approval it must not hide the
            // undo action — losing undo after viewing the compare would strand the write.
            // Prefer the side-by-side compare view; the head/previous revisions are already
            // at hand, so no refetch is needed to build it.
            if (!TryOpenTocCompare(head, previous))
                await AppendLineageComparisonAsync(taskId, settings?.PopoBaseUrl);
        });

        card.DenyCommand = new AsyncRelayCommand(async () =>
        {
            card.ShowActions = false;
            await UndoToPreviousRevisionAsync(taskId, settings?.PopoBaseUrl);
        });

        AppendConversation(card);
    }

    private static string TruncateForDisplay(string value, int maxLength) =>
        value.Length > maxLength ? value.Substring(0, maxLength) + "…" : value;

    private async System.Threading.Tasks.Task AppendLineageComparisonAsync(string? taskId, string? popoBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            AppendConversation(AgentMessage.Error("本文档未绑定 Popo 任务，无法给出修订对比。"));
            return;
        }

        try
        {
            using var popo = new PopoService(popoBaseUrl);
            var lineage = await popo.FetchTocLineageAsync(taskId!);

            // P06: one compact row per revision; the full override JSON only ever appears
            // inside the row's collapsed monospace region, never in the thread text.
            var message = AgentMessage.Agent("修订谱系（新→旧，最多 5 条）：");
            for (var i = lineage.Revisions.Count - 1; i >= 0 && i >= lineage.Revisions.Count - 5; i--)
            {
                var rev = lineage.Revisions[i];
                message.Revisions.Add(new AgentRevisionRow
                {
                    Label = $"r{rev.Rev}（{rev.Source}，{rev.Fingerprint}）",
                    Stats = $"层级 {rev.MaxDepth} · 跳变 {rev.LevelJumpViolations} · 编号违规 {rev.NumberingViolationCount}",
                    OverridesText = rev.Overrides is null or { Count: 0 } ? string.Empty : rev.Overrides.ToJsonString(),
                });
            }

            AppendConversation(message);
        }
        catch (System.Exception ex)
        {
            AppendConversation(AgentMessage.Error("修订谱系读取失败：" + ex.Message));
        }
    }

    /// <summary>
    /// Undo (P04): replays the previous revision's own overrides through retoc, producing a
    /// new revision that restores its outcome. The previous revision stays in the ledger.
    /// </summary>
    private async System.Threading.Tasks.Task UndoToPreviousRevisionAsync(string? taskId, string? popoBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            AppendConversation(AgentMessage.Error("本文档未绑定 Popo 任务，无法撤销。"));
            return;
        }

        try
        {
            using var popo = new PopoService(popoBaseUrl);
            var lineage = await popo.FetchTocLineageAsync(taskId!);
            if (lineage.Revisions.Count < 2)
            {
                AppendConversation(AgentMessage.SystemNote("当前已是首个修订，没有可恢复的上一修订。"));
                return;
            }

            var target = lineage.Revisions[lineage.Revisions.Count - 2];
            AppendConversation(AgentMessage.SystemNote(
                $"撤销：以 r{target.Rev} 的覆写重跑 retoc，会产生新修订；r{target.Rev} 本身保留在谱系中。"));

            // The retoc response embeds the whole TOC tree; inlining it in a chat line makes the
            // conversation view unrenderable (multi-megabyte TextBlock layout spins the UI thread).
            // The revision ledger stays the source of truth for what the undo produced.
            await popo.RetocTaskAsync(taskId!, target.Overrides);
            var undoDone = AgentMessage.Agent($"撤销完成：已按 r{target.Rev} 的覆写重跑 retoc，新修订已写入谱系。");
            undoDone.Bullets.Add($"r{target.Rev} 本身保留在谱系中，可在完成卡再次查看对比。");
            AppendConversation(undoDone);
        }
        catch (System.Exception ex)
        {
            AppendConversation(AgentMessage.Error("撤销失败：" + ex.Message));
        }
    }

    /// <summary>
    /// Applies the pending patch to the in-memory structure via the deterministic
    /// patcher (replaces the TOC entries). The block→node mapping stays in the patch
    /// artifact; tree rewiring is a separate persistence step.
    /// </summary>
    [RelayCommand]
    private void ApplyTocPatch()
    {
        if (_pendingAgentPatch is null)
            return;

        TocAlignmentPatcher.Apply(_structureDocument, _pendingAgentPatch);
        AgentStatusText = $"已应用补丁：目录 {_pendingAgentPatch.Nodes.Count} 个节点。";
        _pendingAgentPatch = null;
        ShowAgentPatchPanel = false;
    }

    [RelayCommand]
    private void CloseAgentPatchPanel()
    {
        ShowAgentPatchPanel = false;
    }

    /// <summary>
    /// Pushes the current Popo structure to the Jiulu hub. This path stays outside the
    /// agent loop: deterministic plan (book draft + one chapter per section), one
    /// read-only duplicate check, then drafts; audit submission is a separate,
    /// explicitly confirmed step. Local writes stop at drafts unless the user submits.
    /// </summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task PushToJiuluAsync()
    {
        if (IsAgentRunning)
            return;

        var services = App.Current?.Services;
        var publisher = services?.GetRequiredService<JiuluPublisher>();
        var dialogService = services?.GetRequiredService<IDialogService>();
        if (publisher is null || dialogService is null)
        {
            AgentStatusText = "推送服务不可用。";
            return;
        }

        var docName = string.IsNullOrWhiteSpace(_projectPath)
            ? _structureDocument.DocId
            : System.IO.Path.GetFileName(_projectPath.TrimEnd('/', '\\'));
        if (string.IsNullOrWhiteSpace(docName))
            docName = _structureDocument.DocId;

        JiuluPushPlan plan;
        try
        {
            plan = JiuluPublisher.BuildPlan(_structureDocument, docName);
        }
        catch (System.Exception ex)
        {
            AgentStatusText = ex.Message;
            return;
        }

        if (!await dialogService.ShowConfirmationDialogAsync(
                "推送至九录文华", JiuluPublisher.SummarizePlan(plan)))
            return;

        IsAgentRunning = true;
        try
        {
            // Duplicate check first (the hub's own instructions: search, then write).
            AgentStatusText = "查询重复书目…";
            var search = await publisher.SearchBooksAsync(plan.DocName);
            var total = JiuluPublisher.SearchTotalMatches(search);
            if (total is > 0 &&
                !await dialogService.ShowConfirmationDialogAsync(
                    "疑似同名书目",
                    $"九录已有 {total} 条与《{plan.DocName}》相关的结果，仍要创建新草稿吗？"))
            {
                AgentStatusText = "已取消：九录存在同名书目。";
                return;
            }

            AgentStatusText = "创建书籍草稿…";
            var bookResult = await publisher.CreateBookDraftAsync(plan.BookDraft);
            var bookId = JiuluPublisher.ExtractDraftId(bookResult);
            if (bookId is null)
            {
                AgentStatusText = $"书籍草稿已创建，但未从响应中取到 book_id：{JiuluPublisher.ResponseText(bookResult)}";
                App.Messenger.Send(new ShowNotificationMessage(
                    Avalonia.Controls.Notifications.NotificationType.Warning, "九录文华", AgentStatusText));
                return;
            }

            for (var i = 0; i < plan.Chapters.Count; i++)
            {
                AgentStatusText = $"创建章节草稿 {i + 1}/{plan.Chapters.Count}…";
                await publisher.CreateChapterDraftAsync(bookId.Value, plan.Chapters[i]);
            }

            // Audit submission is a separate, explicitly confirmed decision.
            var outcome = $"草稿已保存（book_id={bookId}，章节 {plan.Chapters.Count} 个）";
            if (await dialogService.ShowConfirmationDialogAsync(
                    "提交人工审批",
                    $"{outcome}。\n\n是否提交至九录的人工审批队列？"))
            {
                AgentStatusText = "提交人工审批…";
                await publisher.SubmitBookRevisionAsync(bookId.Value, $"Popo 加工结果推送：《{plan.DocName}》");
                outcome += "；已提交人工审批";
            }

            AgentStatusText = outcome;
            App.Messenger.Send(new ShowNotificationMessage(
                Avalonia.Controls.Notifications.NotificationType.Success, "九录文华", outcome));
        }
        catch (System.Exception ex)
        {
            AgentStatusText = ex.Message;
            App.Messenger.Send(new ShowNotificationMessage(
                Avalonia.Controls.Notifications.NotificationType.Error, "九录文华推送失败", ex.Message));
        }
        finally
        {
            IsAgentRunning = false;
        }
    }

    #endregion
}
