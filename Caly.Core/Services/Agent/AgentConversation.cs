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

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Caly.Core.Services.Agent;

public enum AgentMessageKind
{
    User,
    Agent,
    System,
    Error,
    /// <summary>Live run card: checklist text plus a collapsible tool trace.</summary>
    RunCard,
    /// <summary>Pending inline approval: the approve/deny pair replaces the old modal dialog.</summary>
    Approval,
    /// <summary>Completion card: lineage comparison and undo entry points.</summary>
    Done,
}

/// <summary>Lifecycle of one checklist step shown on a live run card.</summary>
public enum AgentStepState
{
    Pending,
    Running,
    Done,
    Failed,
}

/// <summary>One checklist line of a run card: label plus live state glyph.</summary>
public partial class AgentRunStep : ObservableObject
{
    public string Label { get; init; } = string.Empty;

    [ObservableProperty]
    private AgentStepState _state;

    public bool IsPending => State == AgentStepState.Pending;

    public bool IsRunning => State == AgentStepState.Running;

    public bool IsDone => State == AgentStepState.Done;

    public bool IsFailed => State == AgentStepState.Failed;

    partial void OnStateChanged(AgentStepState value)
    {
        OnPropertyChanged(nameof(IsPending));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsDone));
        OnPropertyChanged(nameof(IsFailed));
    }
}

/// <summary>Small labelled metric rendered as a chip (e.g. 层级 / 跳变) on the Done card.</summary>
public sealed class AgentStatChip
{
    public string Label { get; init; } = string.Empty;

    public string Value { get; init; } = string.Empty;
}

/// <summary>
/// One lineage row: identity line, one-line quality stats and the full override payload,
/// which is only ever shown inside a collapsed monospace region (never inline in the
/// thread text, which once laid out a 12 MB tree).
/// </summary>
public sealed class AgentRevisionRow
{
    private const int OverridesPreviewLength = 60_000;

    public string Label { get; init; } = string.Empty;

    public string Stats { get; init; } = string.Empty;

    public string OverridesText { get; init; } = string.Empty;

    public bool HasOverrides => OverridesText.Length > 0;

    /// <summary>Bounded text for the collapsible region so expansion never lays out megabytes.</summary>
    public string OverridesPreview => OverridesText.Length > OverridesPreviewLength
        ? OverridesText.Substring(0, OverridesPreviewLength) + "\n…（覆写 JSON 过大，此处截断；谱系接口保留完整内容）"
        : OverridesText;
}

/// <summary>
/// One message on the built-in agent's conversation thread. Plain kinds carry text;
/// card kinds additionally carry a live-updated text body, an inline action pair
/// (approve/deny labels and commands owned per message) and a status line.
/// Structured side-collections (checklist steps, stat chips, lineage rows, bullets) keep
/// large payloads out of the thread text; anything raw or oversized renders collapsed.
/// </summary>
public partial class AgentMessage : ObservableObject
{
    private const int LongTextThreshold = 1200;

    private const int LongTextPreviewLength = 600;

    private const int ExpandedTextLength = 60_000;

    public AgentMessage()
    {
        Steps.CollectionChanged += OnSideCollectionChanged;
        Stats.CollectionChanged += OnSideCollectionChanged;
        Bullets.CollectionChanged += OnSideCollectionChanged;
        Revisions.CollectionChanged += OnSideCollectionChanged;
    }

    private void OnSideCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasSteps));
        OnPropertyChanged(nameof(HasStats));
        OnPropertyChanged(nameof(HasBullets));
        OnPropertyChanged(nameof(HasRevisions));
    }

    public AgentMessageKind Kind { get; init; }

    public string RoleLabel { get; init; } = string.Empty;

    public DateTime Timestamp { get; init; } = DateTime.Now;

    public string TimeText => Timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    [ObservableProperty]
    private string _text = string.Empty;

    /// <summary>Tool-event log rendered inside the card's collapsible trace.</summary>
    [ObservableProperty]
    private string _traceText = string.Empty;

    public bool HasTrace => TraceText.Length > 0;

    partial void OnTraceTextChanged(string value) => OnPropertyChanged(nameof(HasTrace));

    /// <summary>When true the approve/deny button row under the message is shown.</summary>
    [ObservableProperty]
    private bool _showActions;

    [ObservableProperty]
    private string _approveLabel = "校准并重跑";

    [ObservableProperty]
    private string _denyLabel = "不改了";

    [ObservableProperty]
    private IRelayCommand? _approveCommand;

    [ObservableProperty]
    private IRelayCommand? _denyCommand;

    /// <summary>Short outcome line (decided / cancelled / done).</summary>
    [ObservableProperty]
    private string _status = string.Empty;

    public bool HasStatus => Status.Length > 0;

    partial void OnStatusChanged(string value) => OnPropertyChanged(nameof(HasStatus));

    // Role flags driving the item template's layout branches.
    public bool IsUser => Kind == AgentMessageKind.User;

    public bool IsAgent => Kind == AgentMessageKind.Agent;

    public bool IsSystem => Kind == AgentMessageKind.System;

    public bool IsError => Kind == AgentMessageKind.Error;

    public bool IsRunCard => Kind == AgentMessageKind.RunCard;

    public bool IsApproval => Kind == AgentMessageKind.Approval;

    public bool IsDone => Kind == AgentMessageKind.Done;

    public bool IsCard => Kind is AgentMessageKind.RunCard or AgentMessageKind.Approval or AgentMessageKind.Done;

    /// <summary>True while the message waits behind an in-flight run (shown as a 排队中 chip).</summary>
    [ObservableProperty]
    private bool _isQueued;

    /// <summary>Long payloads show a bounded preview inline; the full text lives in a collapsible region.</summary>
    public bool HasLongText => Text.Length > LongTextThreshold;

    public string PreviewText => Text.Length > LongTextThreshold
        ? Text.Substring(0, LongTextPreviewLength) + "\n…（内容较长，可展开原始内容）"
        : Text;

    /// <summary>Bounded payload for the collapsible region; MB-scale raw JSON must never enter layout.</summary>
    public string ExpandedText => Text.Length > ExpandedTextLength
        ? Text.Substring(0, ExpandedTextLength) + "\n…（内容过大，此处截断；完整内容保留在本地记录中）"
        : Text;

    partial void OnTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasLongText));
        OnPropertyChanged(nameof(PreviewText));
        OnPropertyChanged(nameof(ExpandedText));
    }

    /// <summary>Checklist shown instead of the raw card text while a run is live.</summary>
    public ObservableCollection<AgentRunStep> Steps { get; } = new();

    public bool HasSteps => Steps.Count > 0;

    /// <summary>Key metrics chips (Done card).</summary>
    public ObservableCollection<AgentStatChip> Stats { get; } = new();

    public bool HasStats => Stats.Count > 0;

    /// <summary>Short outcome lines rendered as a list under the message text.</summary>
    public ObservableCollection<string> Bullets { get; } = new();

    public bool HasBullets => Bullets.Count > 0;

    /// <summary>Revision-lineage rows with collapsible override payloads.</summary>
    public ObservableCollection<AgentRevisionRow> Revisions { get; } = new();

    public bool HasRevisions => Revisions.Count > 0;

    public static AgentMessage User(string text) =>
        new() { Kind = AgentMessageKind.User, RoleLabel = "你", Text = text };

    public static AgentMessage Agent(string text) =>
        new() { Kind = AgentMessageKind.Agent, RoleLabel = "智能体", Text = text };

    public static AgentMessage SystemNote(string note) =>
        new() { Kind = AgentMessageKind.System, RoleLabel = "系统", Text = note };

    public static AgentMessage Error(string text) =>
        new() { Kind = AgentMessageKind.Error, RoleLabel = "错误", Text = text };
}

/// <summary>
/// Append-only per-document JSONL conversation log. One line per message; the thread
/// is replayed when the document opens again, which makes who-approved-what-when auditable
/// and lets crash recovery reconstruct the run. A log failure must never sink a run,
/// so every write is best-effort.
/// </summary>
public static class AgentConversationLog
{
    private const string FileName = "agent_conversation.jsonl";

    /// <summary>
    /// Replay safety cap: a single audit line may legitimately embed a large payload, but the
    /// UI must never try to lay out one. Long lines are truncated only in the replayed view.
    /// </summary>
    private const int MaxReplayTextLength = 4096;

    private static readonly JsonSerializerOptions LineOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string? PathFor(string? projectPath) =>
        string.IsNullOrEmpty(projectPath) ? null : Path.Combine(projectPath, FileName);

    public static void Append(string? projectPath, AgentMessage message)
    {
        var path = PathFor(projectPath);
        if (path is null)
            return;

        try
        {
            var line = new JsonObject
            {
                ["ts"] = message.Timestamp.ToString("o", CultureInfo.InvariantCulture),
                ["kind"] = message.Kind.ToString().ToLowerInvariant(),
                ["role"] = message.RoleLabel,
                ["text"] = message.Text,
            };
            File.AppendAllText(path, line.ToJsonString(LineOptions) + "\n");
        }
        catch (Exception)
        {
            // Auditing is best-effort by design; a disk hiccup must not abort the agent run.
        }
    }

    public static List<AgentMessage> Load(string? projectPath)
    {
        var path = PathFor(projectPath);
        var messages = new List<AgentMessage>();
        if (path is null || !File.Exists(path))
            return messages;

        foreach (var raw in File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            try
            {
                if (JsonNode.Parse(raw) is not JsonObject line)
                    continue;

                var kind = ParseKind(line["kind"]?.GetValue<string>());
                var text = line["text"]?.GetValue<string>() ?? string.Empty;
                if (text.Length > MaxReplayTextLength)
                    text = text.Substring(0, MaxReplayTextLength) + "…（内容过长，仅在回放中截断，审计日志保留全文）";

                // Replay cards as plain text lines; live actions cannot be reconstructed.
                if (kind is AgentMessageKind.RunCard or AgentMessageKind.Approval)
                {
                    kind = AgentMessageKind.Agent;
                    text = "（历史卡片）" + text;
                }
                else if (kind == AgentMessageKind.Done)
                {
                    // Keep completion cards in the Done kind: the undo path stays valid across
                    // sessions, so the host re-attaches compare/undo actions to the last card.
                    text = "（历史卡片）" + text;
                }

                messages.Add(new AgentMessage
                {
                    Kind = kind,
                    RoleLabel = line["role"]?.GetValue<string>() ?? string.Empty,
                    Timestamp = ParseTimestamp(line["ts"]?.GetValue<string>()),
                    Text = text,
                });
            }
            catch (Exception)
            {
                // Skip malformed lines instead of failing the whole replay.
            }
        }

        return messages;
    }

    private static AgentMessageKind ParseKind(string? kind) => kind switch
    {
        "user" => AgentMessageKind.User,
        "agent" => AgentMessageKind.Agent,
        "runcard" => AgentMessageKind.RunCard,
        "approval" => AgentMessageKind.Approval,
        "done" => AgentMessageKind.Done,
        "error" => AgentMessageKind.Error,
        _ => AgentMessageKind.System,
    };

    private static DateTime ParseTimestamp(string? ts) =>
        DateTime.TryParse(ts, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : DateTime.Now;
}
