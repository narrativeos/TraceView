using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Caly.Core.Views;

/// <summary>
/// Dialog that asks the user to choose between cancelling, skipping regeneration, or regenerating GeoLibre data.
/// </summary>
public partial class TermProcessChoiceWindow : Window
{
    public TermProcessChoiceWindow()
    {
        InitializeComponent();
    }

    public TermProcessChoiceWindow(string title, string message)
    : this()
    {
        Title = title;

        var titleText = this.FindControl<TextBlock>("TitleText");
        var messageText = this.FindControl<TextBlock>("MessageText");
        titleText?.SetValue(TextBlock.TextProperty, title);
        messageText?.SetValue(TextBlock.TextProperty, message);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(TermProcessChoice.Cancel);
    }

    private void OnLoadClick(object? sender, RoutedEventArgs e)
    {
        Close(TermProcessChoice.LoadExisting);
    }

    private void OnRegenerateClick(object? sender, RoutedEventArgs e)
    {
        Close(TermProcessChoice.Regenerate);
    }
}

/// <summary>
/// User's choice for GeoLibre regenerate action.
/// </summary>
public enum TermProcessChoice
{
    Cancel,
    LoadExisting,
    Regenerate
}