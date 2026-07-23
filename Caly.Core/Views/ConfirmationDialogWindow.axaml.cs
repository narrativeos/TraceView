using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Caly.Core.Views;

public partial class ConfirmationDialogWindow : Window
{
    public ConfirmationDialogWindow()
    {
        InitializeComponent();
    }

    public ConfirmationDialogWindow(string title, string message)
    : this()
    {
        Title = title;

        // Set text via FindControl
        var titleText = this.FindControl<TextBlock>("TitleText");
        var messageText = this.FindControl<TextBlock>("MessageText");
        titleText?.SetValue(TextBlock.TextProperty, title);
        messageText?.SetValue(TextBlock.TextProperty, message);
    }

    private void OnContinueClick(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}