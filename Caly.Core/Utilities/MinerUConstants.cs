using Avalonia.Media;

namespace Caly.Core.Utilities;

/// <summary>
/// Shared constants for MinerU block analysis: source types, colors, thresholds.
/// Centralizes values that are used across models, viewmodels, and controls.
/// </summary>
public static class MinerUConstants
{
    #region Block Source Types

    /// <summary>Block was adopted into para_blocks (merged paragraph).</summary>
    public const string SourcePara = "para";

    /// <summary>Block was rejected and placed in discarded_blocks.</summary>
    public const string SourceDiscarded = "discarded";

    #endregion

    #region Destination Types

    /// <summary>preproc_block was merged into a para_block.</summary>
    public const string DestPara = "para";

    /// <summary>preproc_block was placed in discarded_blocks.</summary>
    public const string DestDiscarded = "discarded";

    #endregion

    #region Colors (Material Design palette)

    /// <summary>Green 500 — adopted/accepted blocks.</summary>
    public const string AdoptedColor = "#4CAF50";

    /// <summary>Red 500 — discarded/rejected blocks.</summary>
    public const string DiscardedColor = "#F44336";

    /// <summary>Blue Gray 300 — default/unknown blocks.</summary>
    public const string DefaultColor = "#B0BEC5";

    /// <summary>Amber 500 — highlight color.</summary>
    public const string HighlightColor = "#FFD600";

    #endregion

    #region Cached Brushes (thread-safe static, avoid per-access allocation)

    /// <summary>Cached green brush for adopted blocks.</summary>
    public static readonly SolidColorBrush AdoptedBrush = new(Color.Parse(AdoptedColor));

    /// <summary>Cached red brush for discarded blocks.</summary>
    public static readonly SolidColorBrush DiscardedBrush = new(Color.Parse(DiscardedColor));

    /// <summary>Cached gray brush for default/unknown blocks.</summary>
    public static readonly SolidColorBrush DefaultBrush = new(Color.Parse(DefaultColor));

    #endregion

    #region Badge Text

    /// <summary>Badge text for adopted blocks.</summary>
    public const string AdoptedBadge = "✓ 采纳";

    /// <summary>Badge text for discarded blocks.</summary>
    public const string DiscardedBadge = "✗ 抛弃";

    #endregion

    #region Overlap Threshold

    /// <summary>
    /// Minimum overlap ratio (overlap area / preproc_block area) for matching.
    /// Values >= 0.3 are considered a match.
    /// </summary>
    public const double MinimumOverlapRatio = 0.3;

    #endregion
}