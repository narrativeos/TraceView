using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Caly.Core.Views;

/// <summary>
/// Dialog that asks the user to choose between saving a GeoLibre file or opening it in GeoLibre app.
/// Returns GeoLibreExportChoice enum indicating the user's selection.
/// </summary>
public partial class GeoLibreExportChoiceWindow : Window
{
    public GeoLibreExportChoiceWindow()
    {
        InitializeComponent();

#if DEBUG
        // Show debug button only in Debug builds
        var debugButton = this.FindControl<Button>("OpenInDebugButton");
        if (debugButton != null)
            debugButton.IsVisible = true;
#endif
    }

    public GeoLibreExportChoiceWindow(string title, string message)
    : this()
    {
        Title = title;

        // Set text via FindControl
        var titleText = this.FindControl<TextBlock>("TitleText");
        var messageText = this.FindControl<TextBlock>("MessageText");
        titleText?.SetValue(TextBlock.TextProperty, title);
        messageText?.SetValue(TextBlock.TextProperty, message);
    }

    private void OnSaveFileClick(object? sender, RoutedEventArgs e)
    {
        Close(GeoLibreExportChoice.SaveFile);
    }

    private void OnOpenInAppClick(object? sender, RoutedEventArgs e)
    {
        Close(GeoLibreExportChoice.OpenInRelease);
    }

    private void OnOpenInDebugClick(object? sender, RoutedEventArgs e)
    {
        Close(GeoLibreExportChoice.OpenInDebug);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(GeoLibreExportChoice.Cancel);
    }
}

/// <summary>
/// User's choice for GeoLibre export action.
/// </summary>
public enum GeoLibreExportChoice
{
    Cancel,
    SaveFile,
    OpenInRelease,
    OpenInDebug
}