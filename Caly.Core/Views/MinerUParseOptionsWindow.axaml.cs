using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Linq;

namespace Caly.Core.Views;

/// <summary>
/// Dialog shown when the user clicks the MinerU parse button.
/// Lets the user choose the page range (all pages, or a start/end page range,
/// defaulting to pages 1-15), the parse tier (default: the last tier the
/// user selected, persisted in settings; "standard" initially) and the parse
/// engine (<c>ocr_mode</c>: auto / txt / ocr, default "auto").
/// Closing with Cancel returns <c>null</c>.
/// </summary>
public partial class MinerUParseOptionsWindow : Window
{
    public MinerUParseOptionsWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Creates the dialog with the tier and engine combo boxes pre-selected to
    /// <paramref name="initialTier"/> and <paramref name="initialOcrMode"/>
    /// (typically the last values the user chose, persisted in settings). Each falls
    /// back to its XAML default ("standard" / "auto") when null or not a known value.
    /// </summary>
    public MinerUParseOptionsWindow(string? initialTier, string? initialOcrMode)
        : this()
    {
        Preselect("TierCombo", initialTier);
        Preselect("EngineCombo", initialOcrMode);
    }

    /// <summary>
    /// Selects <paramref name="value"/> in the named string combo box, ignoring
    /// unknown or empty values so the XAML default stays selected.
    /// </summary>
    private void Preselect(string comboName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        var combo = this.FindControl<ComboBox>(comboName);
        if (combo is null)
            return;

        var index = combo.Items
            ?.OfType<object>()
            .ToList()
            .FindIndex(item => string.Equals(item as string, value.Trim(), System.StringComparison.OrdinalIgnoreCase));

        if (index is not null && index >= 0)
            combo.SelectedIndex = index.Value;
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        var rangeRadio = this.FindControl<RadioButton>("RangePagesRadio");
        var startBox = this.FindControl<NumericUpDown>("StartPageBox");
        var endBox = this.FindControl<NumericUpDown>("EndPageBox");
        var tierCombo = this.FindControl<ComboBox>("TierCombo");
        var engineCombo = this.FindControl<ComboBox>("EngineCombo");

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
        var ocrMode = engineCombo?.SelectedItem as string ?? Services.MinerUService.DefaultOcrMode;

        Close(new MinerUParseOptions
        {
            PageRange = pageRange,
            Tier = tier,
            OcrMode = ocrMode
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

    /// <summary>
    /// Parse engine (MinerU <c>ocr_mode</c>): "auto" (pick the parsing branch per page,
    /// the default), "txt" (native text layer for the whole document) or
    /// "ocr" (forced OCR for the whole document).
    /// </summary>
    public string OcrMode { get; init; } = Services.MinerUService.DefaultOcrMode;
}
