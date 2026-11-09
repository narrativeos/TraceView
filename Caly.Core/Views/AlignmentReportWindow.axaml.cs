using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Caly.Core.ViewModels;

namespace Caly.Core.Views;

public partial class AlignmentReportWindow : Window
{
    private readonly DocumentViewModel _documentVm;

    public AlignmentReportWindow(DocumentViewModel documentVm)
    {
        InitializeComponent();
        _documentVm = documentVm;
        DataContext = documentVm;
    }

    /// <summary>
    /// Esc closes the window (platform convention for dismissing a report window).
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

    private async void CopyButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (Clipboard is IClipboard clipboard)
        {
            await clipboard.SetTextAsync(_documentVm.AlignmentReportText);
        }
    }
}