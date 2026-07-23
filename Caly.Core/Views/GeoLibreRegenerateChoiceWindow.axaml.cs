using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Caly.Core.Views;

/// <summary>
/// Dialog that asks the user to choose between cancelling, skipping regeneration, or regenerating GeoLibre data.
/// </summary>
public partial class GeoLibreRegenerateChoiceWindow : Window
{
    public GeoLibreRegenerateChoiceWindow()
    {
        InitializeComponent();
    }

    public GeoLibreRegenerateChoiceWindow(string title, string message)
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
        Close(GeoLibreRegenerateChoice.Cancel);
    }

    private void OnSkipClick(object? sender, RoutedEventArgs e)
    {
        Close(GeoLibreRegenerateChoice.Skip);
    }

    private void OnRegenerateClick(object? sender, RoutedEventArgs e)
    {
        Close(GeoLibreRegenerateChoice.Regenerate);
    }
}

/// <summary>
/// User's choice for GeoLibre regenerate action.
/// </summary>
public enum GeoLibreRegenerateChoice
{
    Cancel,
    Skip,
    Regenerate
}