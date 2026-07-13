using Avalonia.Controls;
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

    private async void CopyButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (Clipboard is IClipboard clipboard)
        {
            await clipboard.SetTextAsync(_documentVm.AlignmentReportText);
        }
    }
}