// Copyright (c) 2025 BobLd
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System.Text.RegularExpressions;
using Xunit;

namespace Caly.Tests;

/// <summary>
/// Guards the theme colour configuration: the primary colour must have a single source of truth per
/// appearance, every primary-tinted brush must derive from it, and both appearances must declare the
/// same set of semantic tokens so that a control cannot silently lose its Dark value.
/// </summary>
public class ThemeTokenConsistencyTests
{
    private static readonly string[] _derivedBrushes =
    [
        "CalyPrimaryBrush",
        "CalySelectionRingBrush",
        "CalyToolbarActiveBackgroundBrush",
        "CalyToolbarActiveBorderBrush"
    ];

    private static string AppXaml => File.ReadAllText(LocateFile("Caly.Core", "App.axaml"));

    private static string CalyThemeXaml => File.ReadAllText(LocateFile("Caly.Core", "CalyTheme.axaml"));

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void PrimaryColor_IsTheSingleSourceForThePaletteAccent(string theme)
    {
        var primary = GetPrimaryColor(theme);
        Assert.NotNull(primary);
        Assert.Equal(primary, GetAccent(theme));
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void PrimaryTintedBrushes_DeriveFromThePrimaryColor(string theme)
    {
        var dictionary = GetThemeDictionary(AppXaml, theme);

        foreach (var brush in _derivedBrushes)
        {
            var match = Regex.Match(dictionary,
                $"<SolidColorBrush x:Key=\"{brush}\"[^>]*Color=\"([^\"]+)\"");

            Assert.True(match.Success, $"{brush} is not defined for {theme}.");
            Assert.Equal("{StaticResource CalyPrimaryColor}", match.Groups[1].Value);
        }
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void PrimaryColor_IsNotHardcodedOutsideOfItsDefinition(string theme)
    {
        // Only the <Color> definition itself may carry the literal value; everything else must go
        // through {StaticResource CalyPrimaryColor} so that re-theming stays a one line change.
        var dictionary = Regex.Replace(GetThemeDictionary(AppXaml, theme),
            "<Color x:Key=\"CalyPrimaryColor\">[^<]*</Color>", string.Empty);

        var primary = GetPrimaryColor(theme)!.ToLowerInvariant();

        var offenders = Regex.Matches(dictionary, "#[0-9a-fA-F]{6,8}")
            .Select(m => Normalize(m.Value))
            .Where(v => v == primary)
            .Distinct()
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void LightAndDark_DeclareTheSameSemanticTokens()
    {
        var light = GetTokenKeys(AppXaml, "Light");
        var dark = GetTokenKeys(AppXaml, "Dark");

        var missingInDark = light.Except(dark).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var missingInLight = dark.Except(light).OrderBy(x => x, StringComparer.Ordinal).ToArray();

        Assert.True(missingInDark.Length == 0, $"Missing in Dark: {string.Join(", ", missingInDark)}");
        Assert.True(missingInLight.Length == 0, $"Missing in Light: {string.Join(", ", missingInLight)}");
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void PaperFirstTokens_AreDefinedInBothAppearances(string theme)
    {
        var dictionary = GetThemeDictionary(AppXaml, theme);

        foreach (var key in new[]
                 {
                     "CalyCardBackgroundBrush", "CalyCardBorderBrush", "CalyCanvasBackgroundBrush",
                     "CalyCanvasTileBrush", "CalySelectionRingBrush", "CalyHighlightBackgroundBrush",
                     "CalyHighlightForegroundBrush", "CalyToolbarHoverBackgroundBrush",
                     "CalyToolbarPressedBackgroundBrush", "CalyToolbarActiveBackgroundBrush",
                     "CalyToolbarActiveBorderBrush"
                 })
        {
            Assert.Contains($"x:Key=\"{key}\"", dictionary);
        }
    }

    [Theory]
    [InlineData("CalyCardCornerRadius")]
    [InlineData("CalyFloatingCornerRadius")]
    [InlineData("CalyToolbarButtonCornerRadius")]
    public void ShapeTokens_AreDefinedOnceAndSharedByBothAppearances(string key)
    {
        // Corner radii do not depend on the appearance, so they live outside the theme dictionaries.
        Assert.Equal(1, Regex.Matches(CalyThemeXaml, $"x:Key=\"{key}\"").Count);
    }

    private static string? GetPrimaryColor(string theme)
    {
        var match = Regex.Match(GetThemeDictionary(AppXaml, theme),
            "<Color x:Key=\"CalyPrimaryColor\">(#[0-9a-fA-F]{6,8})</Color>");

        return match.Success ? Normalize(match.Groups[1].Value) : null;
    }

    private static string GetAccent(string theme)
    {
        var match = Regex.Match(AppXaml,
            $"x:Key=\"{theme}\"[^>]*Accent=\"(#[0-9a-fA-F]{{6,8}})\"");

        Assert.True(match.Success, $"No Accent defined for {theme}.");
        return Normalize(match.Groups[1].Value);
    }

    private static string Normalize(string hex)
    {
        hex = hex[1..].ToLowerInvariant();
        return hex.Length == 6 ? "ff" + hex : hex;
    }

    private static string GetThemeDictionary(string xaml, string theme)
    {
        var start = xaml.IndexOf($"<ResourceDictionary x:Key=\"{theme}\">", StringComparison.Ordinal);
        Assert.True(start >= 0, $"No theme dictionary for {theme}.");

        var end = xaml.IndexOf("</ResourceDictionary>", start, StringComparison.Ordinal);
        return xaml[start..end];
    }

    private static IEnumerable<string> GetTokenKeys(string xaml, string theme) =>
        Regex.Matches(GetThemeDictionary(xaml, theme), "x:Key=\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value)
            .Where(key => key != theme); // the dictionary key itself is not a token

    private static string LocateFile(string project, string file)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var path = Path.Combine(directory.FullName, project, file);
            if (File.Exists(path))
            {
                return path;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {project}/{file}.");
    }
}
