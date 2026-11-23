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
using System.Linq;

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

        var agentService = App.Current?.Services?.GetRequiredService<IAgentService>();
        if (agentService is null)
        {
            AgentStatusText = "智能体服务不可用。";
            return;
        }

        IsAgentRunning = true;
        AgentStatusText = "TOC 对齐智能体运行中…";

        try
        {
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
    /// Approval prompt for the popo.toc-tune write tool: shows the tool name and the
    /// summarized arguments and asks the user to confirm the rewrite. A missing dialog
    /// service fails closed — the write never runs.
    /// </summary>
    private static AgentToolApprover BuildTocTuneApprover() => async (request, ct) =>
    {
        var dialogService = App.Current?.Services?.GetRequiredService<IDialogService>();
        if (dialogService is null)
            return false;

        return await dialogService.ShowConfirmationDialogAsync(
            "智能体重写目录",
            $"智能体请求执行 {request.ToolName}（站点 {request.CallSiteId}）。\n\n" +
            $"参数：{request.ArgumentsSummary}\n\n" +
            "这将用新规则包重写该文档已保存的任务目录（tree/toc）。是否批准？");
    };

    /// <summary>
    /// Runs the popo.toc-tune call site on the current document: the agent inspects TOC
    /// quality, drafts a minimal rule override against the rule-pack whitelist, gates it on
    /// the gold-set replay, and only then may rewrite this document's stored task result
    /// through the approval-gated retoc_document. The Popo base URL comes from settings and
    /// the task id from the project's popo_result.json when present; without a task id the
    /// run stays read-only (retoc_document refuses without it).
    /// </summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task RunTocTuneAgentAsync()
    {
        if (IsAgentRunning)
            return;

        var agentService = App.Current?.Services?.GetRequiredService<IAgentService>();
        if (agentService is null)
        {
            AgentStatusText = "智能体服务不可用。";
            return;
        }

        IsAgentRunning = true;
        AgentStatusText = "TOC 规则调优智能体运行中…";
        ShowAgentRunTrace = false;
        AgentRunTraceText = string.Empty;

        try
        {
            var context = new AgentToolContext { Document = _structureDocument };

            var settingsService = App.Current?.Services?.GetRequiredService<ISettingsService>();
            var popoBaseUrl = settingsService?.GetSettings().PopoBaseUrl;
            if (!string.IsNullOrWhiteSpace(popoBaseUrl))
            {
                context.Items[TocRuleTuningTools.BaseUrlItemKey] = popoBaseUrl;
            }

            var taskId = PopoJsonService.TryReadTaskId(_projectPath);
            if (!string.IsNullOrWhiteSpace(taskId))
            {
                context.Items[TocRuleTuningTools.TaskIdItemKey] = taskId;
            }

            var result = await agentService.RunAsync(
                AgentCallSiteDefinitions.PopoTocTune.Id,
                "请改善当前文档的目录层级质量：先调用 get_toc_quality 查看层级跳变与低置信条目；" +
                "再用 propose_rule_override 起草最小规则覆盖；用 evaluate_rules 通过 gold 集门禁（零退化）；" +
                "仅在门禁通过且用户批准后调用 retoc_document 重写本文档目录，并报告规则包指纹与层级差异。",
                context,
                BuildTocTuneApprover());

            var traceLines = new System.Collections.Generic.List<string>(result.ToolTrace);
            if (context.Items.TryGetValue(TocRuleTuningTools.SessionItemKey, out var sessionObject) &&
                sessionObject is TocRuleTuningSession { AcceptedOverride: not null } session)
            {
                traceLines.Add($"accepted override: {session.AcceptedOverride.ToJsonString()}");
            }

            AgentRunTraceText = string.Join("\n", traceLines);
            ShowAgentRunTrace = traceLines.Count > 0;

            AgentStatusText = string.IsNullOrWhiteSpace(result.FinalText)
                ? "智能体未产出调优结论。"
                : result.FinalText;
        }
        catch (System.Exception ex)
        {
            AgentStatusText = ex.Message;
            App.Messenger.Send(new ShowNotificationMessage(
                Avalonia.Controls.Notifications.NotificationType.Error, "TOC 规则调优智能体", ex.Message));
        }
        finally
        {
            IsAgentRunning = false;
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
