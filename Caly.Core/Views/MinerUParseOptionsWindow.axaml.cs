using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Linq;

namespace Caly.Core.Views;

/// <summary>
/// Dialog shown when the user clicks the MinerU parse button.
/// Lets the user choose the page range (all pages, or a start/end page range,
/// defaulting to pages 1-15) and the parse tier (default: the last tier the
/// user selected, persisted in settings; "standard" initially).
/// Closing with Cancel returns <c>null</c>.
/// </summary>
public partial class MinerUParseOptionsWindow : Window
{
    public MinerUParseOptionsWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Creates the dialog with the tier combo box pre-selected to
    /// <paramref name="initialTier"/> (typically the last tier the user
    /// chose, persisted in settings). Falls back to "standard" when null
    /// or not a known tier.
    /// </summary>
    public MinerUParseOptionsWindow(string? initialTier)
        : this()
    {
        if (string.IsNullOrWhiteSpace(initialTier))
            return;

        var tierCombo = this.FindControl<ComboBox>("TierCombo");
        if (tierCombo is null)
            return;

        var index = tierCombo.Items
            ?.OfType<object>()
            .ToList()
            .FindIndex(item => string.Equals(item as string, initialTier.Trim(), System.StringComparison.OrdinalIgnoreCase));

        if (index is not null && index >= 0)
            tierCombo.SelectedIndex = index.Value;
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        var rangeRadio = this.FindControl<RadioButton>("RangePagesRadio");
        var startBox = this.FindControl<NumericUpDown>("StartPageBox");
        var endBox = this.FindControl<NumericUpDown>("EndPageBox");
        var tierCombo = this.FindControl<ComboBox>("TierCombo");

        string? pageRange = null;
        if (rangeRadio?.IsChecked == true)
        {
            var start = (int)(startBox?.Value ?? 1);
            var end = (int)(endBox?.Value ?? 15);
            if (start > end)
                (start, end) = (end, start);

            // MinerU page_range is a 1-based inclusive range, e.g. "1-15".
            pageRange = start == end ? start.ToString() : $"{start}-{end}";
        }

        var tier = tierCombo?.SelectedItem as string ?? Services.MinerUService.DefaultTier;

        Close(new MinerUParseOptions
        {
            PageRange = pageRange,
            Tier = tier
        });
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}

/// <summary>
/// The user's MinerU parse options selected in <see cref="MinerUParseOptionsWindow"/>.
/// </summary>
public sealed class MinerUParseOptions
{
    /// <summary>
    /// MinerU <c>page_range</c> string (1-based inclusive, e.g. "1-15").
    /// <c>null</c> means all pages.
    /// </summary>
    public string? PageRange { get; init; }

    /// <summary>
    /// Parse tier: "flash", "basic", "standard" or "advanced".
    /// </summary>
    public string Tier { get; init; } = Services.MinerUService.DefaultTier;
}
