using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Caly.Core.Services;
using Caly.Core.Services.Interfaces;
using Caly.Core.Services.Store;
using Caly.Core.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace Caly.Core.ViewModels;

/// <summary>
/// View model for the 队列看板 window: the production pipeline board
/// (MinerU 解析 → Popo 分析) over every project in the store, optionally enriched by the
/// live rerun report overlay. Refresh reads the store on a background thread; columns are
/// updated on the UI thread.
/// </summary>
public sealed partial class QueueBoardViewModel : ObservableObject
{
    private static readonly (string Title, QueueStage[] Stages)[] DisplayColumns =
    {
        ("处理中", new[] { QueueStage.MinerURunning, QueueStage.PopoRunning }),
        ("排队", new[] { QueueStage.AwaitingMinerU, QueueStage.MinerUDone }),
        ("完成", new[] { QueueStage.Done }),
        ("异常", new[] { QueueStage.MinerUFailed, QueueStage.PopoFailed }),
        ("PDF 缺失", new[] { QueueStage.PdfMissing }),
    };

    private static string StageLabel(QueueStage stage) => QueueBoard.GetStageLabel(stage);

    private DispatcherTimer? _autoRefreshTimer;

    public ObservableCollection<QueueColumnViewModel> Columns { get; } = new();

    [ObservableProperty]
    private string _storeRoot = string.Empty;

    [ObservableProperty]
    private string _lastRefreshText = "尚未刷新";

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool _isBusy;

    public QueueBoardViewModel()
    {
        foreach (var (title, _) in DisplayColumns)
        {
            Columns.Add(new QueueColumnViewModel(title));
        }
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var snapshot = await Task.Run(() => QueueBoard.Build(StorePaths.ResolveRoot()));
            await Dispatcher.UIThread.InvokeAsync(() => Apply(snapshot));
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsBusy = false);
        }
    }

    private bool CanRefresh() => !IsBusy;

    /// <summary>
    /// Opens a card's project PDF in the main window (same path as 近期项目 on the 项目主页).
    /// A missing file is reported via notification instead of failing silently.
    /// </summary>
    [RelayCommand]
    private async Task OpenCard(string? pdfPath, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(pdfPath))
        {
            return;
        }

        if (!File.Exists(pdfPath))
        {
            App.Current?.Services?.GetService<IDialogService>()?
                .ShowNotification("队列看板",
                    $"项目文件已不存在：{Path.GetFileName(pdfPath)}。",
                    Avalonia.Controls.Notifications.NotificationType.Warning);
            return;
        }

        try
        {
            var pdfDocumentsService = App.Current?.Services?.GetRequiredService<IPdfDocumentsManagerService>();
            if (pdfDocumentsService is null)
            {
                throw new NullReferenceException($"Missing {nameof(IPdfDocumentsManagerService)} instance.");
            }

            await pdfDocumentsService.OpenLoadDocument(pdfPath, token);
        }
        catch (OperationCanceledException)
        {
            // No op
        }
        catch (Exception ex)
        {
            Debug.WriteExceptionToFile(ex);
            App.Current?.Services?.GetService<IDialogService>()?
                .ShowNotification("队列看板",
                    $"打开失败：{ex.Message}",
                    Avalonia.Controls.Notifications.NotificationType.Error);
        }
    }

    /// <summary>Periodic refresh while the board window is open; stopped by <see cref="StopAutoRefresh"/>.</summary>
    public void StartAutoRefresh(TimeSpan interval)
    {
        StopAutoRefresh();
        _autoRefreshTimer = new DispatcherTimer { Interval = interval };
        _autoRefreshTimer.Tick += (_, _) => _ = RefreshCommand.ExecuteAsync(null);
        _autoRefreshTimer.Start();
    }

    public void StopAutoRefresh()
    {
        _autoRefreshTimer?.Stop();
        _autoRefreshTimer = null;
    }

    private void Apply(QueueBoardSnapshot snapshot)
    {
        StoreRoot = StorePaths.ResolveRoot();
        LastRefreshText = snapshot.BuiltAt.ToString("HH:mm:ss");
        SummaryText = snapshot.OverlayPresent
            ? $"共 {snapshot.Items.Count} 个项目 · 含实时重跑报告"
            : $"共 {snapshot.Items.Count} 个项目";

        for (var i = 0; i < DisplayColumns.Length; i++)
        {
            var cards = snapshot.Items
                .Where(item => DisplayColumns[i].Stages.Contains(item.Stage))
                .OrderByDescending(item => item.LastActivity ?? DateTime.MinValue)
                .ThenBy(item => item.Title, StringComparer.CurrentCulture)
                .Select(item => new QueueCardViewModel(
                    item.Title,
                    StageLabel(item.Stage),
                    item.Detail,
                    item.ProjectId.Length > 12 ? item.ProjectId[..12] : item.ProjectId,
                    item.LastActivity,
                    item.PdfPath))
                .ToList();
            Columns[i].Replace(cards);
        }
    }
}

/// <summary>One kanban column with its live card collection.</summary>
public sealed class QueueColumnViewModel : ObservableObject
{
    public QueueColumnViewModel(string title)
    {
        Title = title;
    }

    public string Title { get; }

    public ObservableCollection<QueueCardViewModel> Cards { get; } = new();

    public int Count => Cards.Count;

    public void Replace(IReadOnlyList<QueueCardViewModel> cards)
    {
        Cards.Clear();
        foreach (var card in cards)
        {
            Cards.Add(card);
        }

        OnPropertyChanged(nameof(Count));
    }
}

/// <summary>One project card on the board.</summary>
public sealed record QueueCardViewModel(
    string Title,
    string StageLabel,
    string Detail,
    string ProjectIdPrefix,
    DateTime? ActivityAt,
    string PdfPath = "")
{
    public string ActivityText => ActivityAt?.ToString("MM-dd HH:mm") ?? string.Empty;

    public bool HasDetail => !string.IsNullOrEmpty(Detail);
}
