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

/// <summary>
/// One message on the built-in agent's conversation thread. Plain kinds carry text;
/// card kinds additionally carry a live-updated text body, an inline action pair
/// (approve/deny labels and commands owned per message) and a status line.
/// </summary>
public partial class AgentMessage : ObservableObject
{
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

                // Replay cards as plain text lines; live actions cannot be reconstructed.
                if (kind is AgentMessageKind.RunCard or AgentMessageKind.Approval or AgentMessageKind.Done)
                {
                    kind = AgentMessageKind.Agent;
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
