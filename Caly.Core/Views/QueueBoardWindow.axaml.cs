using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Caly.Core.ViewModels;

namespace Caly.Core.Views;

public partial class QueueBoardWindow : Window
{
    private readonly QueueBoardViewModel _viewModel;

    public QueueBoardWindow(QueueBoardViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        Opened += (_, _) =>
        {
            _ = _viewModel.RefreshCommand.ExecuteAsync(null);
            _viewModel.StartAutoRefresh(TimeSpan.FromSeconds(10));
        };
        Closed += (_, _) => _viewModel.StopAutoRefresh();
    }

    /// <summary>
    /// Esc closes the board (same convention as the other report windows).
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }
}
