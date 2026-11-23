using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Caly.Core.Services.Store;

/// <summary>
/// Pipeline stage a project currently sits at, derived from on-disk store state plus the
/// rerun report overlay (大规模生产模式: MinerU 解析 → Popo 分析 流水线).
/// </summary>
public enum QueueStage
{
    /// <summary>MinerU job in flight (per the rerun report overlay).</summary>
    MinerURunning,

    /// <summary>MinerU failed and no artifacts exist; needs attention.</summary>
    MinerUFailed,

    /// <summary>MinerU output on disk, Popo output missing; waiting for the Popo stage.</summary>
    MinerUDone,

    /// <summary>Popo task in flight (per the rerun report overlay).</summary>
    PopoRunning,

    /// <summary>Popo failed after MinerU succeeded; needs attention or a rerun.</summary>
    PopoFailed,

    /// <summary>Both MinerU and Popo outputs exist: fully done.</summary>
    Done,

    /// <summary>Nothing produced yet; waiting to enter the MinerU stage.</summary>
    AwaitingMinerU,

    /// <summary>Original PDF no longer exists; the project cannot be processed.</summary>
    PdfMissing,
}

/// <summary>One card on the queue board: a project plus its derived pipeline position.</summary>
public sealed class QueueBoardItem
{
    public required string ProjectId { get; init; }

    public required string Title { get; init; }

    public required string ProjectPath { get; init; }

    /// <summary>Original PDF location; missing file reported through <see cref="QueueStage.PdfMissing"/>.</summary>
    public string PdfPath { get; init; } = string.Empty;

    public required QueueStage Stage { get; init; }

    /// <summary>Short human detail: artifact sizes, report status, error snippets.</summary>
    public string Detail { get; init; } = string.Empty;

    /// <summary>Latest known activity timestamp (from the report overlay, if any).</summary>
    public DateTime? LastActivity { get; init; }
}

/// <summary>Result of one board refresh pass.</summary>
public sealed class QueueBoardSnapshot
{
    public required DateTime BuiltAt { get; init; }

    public required IReadOnlyList<QueueBoardItem> Items { get; init; }

    /// <summary>Whether the live rerun report overlay was present at build time.</summary>
    public bool OverlayPresent { get; init; }
}

/// <summary>
/// Derives the production queue board for a store root: an index scan of all projects,
/// per-project on-disk stage detection, and an optional rerun report overlay
/// ({root}/rerun-report.json, a JSON object keyed by project id with
/// {mineru:{status,error,at},popo:{status,error,reason,at}} entries) that adds live
/// pipeline information while a batch rerun is running. Disk state is authoritative;
/// the overlay only refines stages that disk cannot see (in-flight / failed causes).
/// No services or DI: usable from tests and any store root.
/// </summary>
public static class QueueBoard
{
    /// <summary>Overlay file name holding live rerun/queue state under the store root.</summary>
    public const string RerunReportFileName = "rerun-report.json";

    /// <summary>Board column order for display and counts.</summary>
    public static readonly QueueStage[] ColumnOrder =
    {
        QueueStage.MinerURunning,
        QueueStage.MinerUDone,
        QueueStage.PopoRunning,
        QueueStage.PopoFailed,
        QueueStage.Done,
        QueueStage.MinerUFailed,
        QueueStage.AwaitingMinerU,
        QueueStage.PdfMissing,
    };

    /// <summary>Human 阶段 label for board columns and 项目主页 badges (single source of truth).</summary>
    public static string GetStageLabel(QueueStage stage) => stage switch
    {
        QueueStage.MinerURunning => "解析中",
        QueueStage.MinerUFailed => "解析失败",
        QueueStage.MinerUDone => "待 Popo",
        QueueStage.PopoRunning => "Popo 中",
        QueueStage.PopoFailed => "Popo 失败",
        QueueStage.Done => "完成",
        QueueStage.AwaitingMinerU => "待解析",
        QueueStage.PdfMissing => "PDF 缺失",
        _ => string.Empty,
    };

    public static QueueBoardSnapshot Build(string root)
    {
        var overlayPath = Path.Combine(root, RerunReportFileName);
        var overlay = LoadRerunReport(overlayPath);
        var items = new List<QueueBoardItem>();

        foreach (var entry in StoreIndex.Build(root).Projects)
        {
            items.Add(Classify(entry, overlay));
        }

        return new QueueBoardSnapshot
        {
            BuiltAt = DateTime.Now,
            Items = items,
            OverlayPresent = overlay.Count > 0,
        };
    }

    private static QueueBoardItem Classify(StoreProjectEntry entry, Dictionary<string, RerunStage> overlay)
    {
        overlay.TryGetValue(entry.ProjectId ?? entry.DirName, out var stage);
        var activity = LatestActivity(stage);

        QueueBoardItem Card(QueueStage s, string detail) => new()
        {
            ProjectId = entry.ProjectId ?? entry.DirName,
            Title = entry.PdfFileName,
            ProjectPath = entry.ProjectPath,
            PdfPath = entry.PdfPath,
            Stage = s,
            Detail = detail,
            LastActivity = activity,
        };

        if (!File.Exists(entry.PdfPath))
        {
            return Card(QueueStage.PdfMissing, "PDF 已不存在");
        }

        var mineru = ProbeMinerU(entry.ProjectPath);
        var popoDone = File.Exists(Path.Combine(entry.ProjectPath, "popo", "popo_result.json"));

        if (mineru.Done && popoDone)
        {
            return Card(QueueStage.Done, mineru.Detail + " · Popo 完成");
        }

        if (mineru.Done)
        {
            if (IsStatus(stage?.Popo, "error"))
            {
                return Card(QueueStage.PopoFailed,
                    mineru.Detail + " · Popo 失败: " + ShortError(stage!.Popo!));
            }

            if (IsStatus(stage?.Popo, "running") || IsStatus(stage?.Popo, "queued") || IsStatus(stage?.Popo, "pending"))
            {
                return Card(QueueStage.PopoRunning, mineru.Detail + " · Popo 队列中");
            }

            return Card(QueueStage.MinerUDone, mineru.Detail + " · 等待 Popo");
        }

        if (IsStatus(stage?.Mineru, "error"))
        {
            return Card(QueueStage.MinerUFailed, "MinerU 失败: " + ShortError(stage!.Mineru!));
        }

        if (IsStatus(stage?.Mineru, "running") || IsStatus(stage?.Mineru, "queued") || IsStatus(stage?.Mineru, "pending"))
        {
            return Card(QueueStage.MinerURunning, "解析队列中");
        }

        if (mineru.Legacy)
        {
            return Card(QueueStage.MinerUDone, mineru.Detail + " · 等待 Popo");
        }

        return Card(QueueStage.AwaitingMinerU, "等待解析");
    }

    private static bool IsStatus(RerunEntry? entry, string status) =>
        string.Equals(entry?.Status, status, StringComparison.OrdinalIgnoreCase);

    private static string ShortError(RerunEntry entry)
    {
        var text = entry.Error ?? entry.Reason ?? "未知错误";
        return text.Length > 120 ? text[..120] + "…" : text;
    }

    private static DateTime? LatestActivity(RerunStage? stage)
    {
        var candidates = new[] { stage?.Mineru?.At, stage?.Popo?.At };
        return candidates.Where(d => d.HasValue).Select(d => d!.Value).DefaultIfEmpty().Max() is var max
               && max != default ? max : null;
    }

    private sealed record MinerUProbeResult(bool Done, bool Legacy, string Detail);

    /// <summary>
    /// Current-convention MinerU output: a {mineru}/{sha32}/ directory holding
    /// middle_json.json. Falls back to the legacy layout (any middle*.json under mineru/).
    /// </summary>
    private static MinerUProbeResult ProbeMinerU(string projectPath)
    {
        var dir = Path.Combine(projectPath, "mineru");
        if (!Directory.Exists(dir))
        {
            return new MinerUProbeResult(false, false, string.Empty);
        }

        try
        {
            var doneDir = Directory.GetDirectories(dir)
                .FirstOrDefault(d => IsDocKey(Path.GetFileName(d)) &&
                                     File.Exists(Path.Combine(d, "middle_json.json")));
            if (doneDir != null)
            {
                var zip = Path.Combine(dir, Path.GetFileName(doneDir) + ".zip");
                var size = File.Exists(zip) ? FormatBytes(new FileInfo(zip).Length) : "无 zip";
                return new MinerUProbeResult(true, false, $"MinerU 完成 {size}");
            }

            if (Directory.EnumerateFiles(dir, "middle*.json", SearchOption.AllDirectories).Any())
            {
                return new MinerUProbeResult(true, true, "MinerU 完成（旧版布局）");
            }
        }
        catch (Exception)
        {
            // Unreadable subtree: treat as no output yet.
        }

        return new MinerUProbeResult(false, false, string.Empty);
    }

    /// <summary>Popo output: popo/popo_result.json exists.</summary>
    internal static bool ProbePopo(string projectPath) =>
        File.Exists(Path.Combine(projectPath, "popo", "popo_result.json"));

    private static bool IsDocKey(string name) =>
        name.Length == StorePaths.ProjectIdHexLength && name.All(Uri.IsHexDigit);

    internal static string FormatBytes(long bytes) =>
        bytes >= 1L << 30 ? $"{bytes / 1024.0 / 1024 / 1024:0.##} GB"
        : bytes >= 1L << 20 ? $"{bytes / 1024.0 / 1024:0.#} MB"
        : $"{bytes / 1024.0:0.#} KB";

    // ---------- rerun report overlay ----------

    internal sealed class RerunStage
    {
        public RerunEntry? Mineru { get; set; }

        public RerunEntry? Popo { get; set; }
    }

    internal sealed class RerunEntry
    {
        public string? Status { get; set; }

        public string? Error { get; set; }

        public string? Reason { get; set; }

        public DateTime? At { get; set; }
    }

    /// <summary>
    /// Lenient overlay reader: the file is written concurrently by the batch tool, so any
    /// partial or corrupt read degrades to an empty overlay (disk detection stays authoritative).
    /// </summary>
    internal static Dictionary<string, RerunStage> LoadRerunReport(string path)
    {
        var empty = new Dictionary<string, RerunStage>(StringComparer.Ordinal);
        try
        {
            if (!File.Exists(path))
            {
                return empty;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return empty;
            }

            var result = new Dictionary<string, RerunStage>(StringComparer.Ordinal);
            foreach (var project in doc.RootElement.EnumerateObject())
            {
                var stage = new RerunStage();
                if (project.Value.ValueKind == JsonValueKind.Object)
                {
                    if (project.Value.TryGetProperty("mineru", out var m))
                    {
                        stage.Mineru = ReadEntry(m);
                    }

                    if (project.Value.TryGetProperty("popo", out var p))
                    {
                        stage.Popo = ReadEntry(p);
                    }
                }

                result[project.Name] = stage;
            }

            return result;
        }
        catch (Exception)
        {
            return empty;
        }
    }

    private static RerunEntry ReadEntry(JsonElement e)
    {
        var r = new RerunEntry();
        if (e.ValueKind != JsonValueKind.Object)
        {
            return r;
        }

        if (e.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String)
        {
            r.Status = s.GetString();
        }

        if (e.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String)
        {
            r.Error = err.GetString();
        }

        if (e.TryGetProperty("reason", out var reason) && reason.ValueKind == JsonValueKind.String)
        {
            r.Reason = reason.GetString();
        }

        if (e.TryGetProperty("at", out var at) && at.ValueKind == JsonValueKind.String &&
            DateTime.TryParse(at.GetString(), out var parsed))
        {
            r.At = parsed;
        }

        return r;
    }
}
