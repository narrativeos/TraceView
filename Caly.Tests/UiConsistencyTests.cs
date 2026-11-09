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
/// Guards the visual/voice consistency of the UI layer: neutral surfaces must come from theme tokens so
/// that Dark mode keeps working, icon-only buttons must stay accessible, glyphs must be drawn (not emoji),
/// and the dialog chrome must speak the same language as the rest of the app.
/// </summary>
public class UiConsistencyTests
{
    private static readonly string[] _neutralColors =
    [
        "White", "Black", "Gray", "Grey", "LightGray", "DimGray", "DarkGray"
    ];

    /// <summary>Chrome strings that must never surface in English (proper nouns are fine).</summary>
    private static readonly string[] _englishChrome =
    [
        "Content=\"Cancel\"", "Content=\"Retry\"", "Content=\"OK\"", "Content=\"Print\"",
        "Content=\"All pages\"", "Content=\"Browse...\"", "Content=\"Open logs folder\"",
        "Content=\"Open in GeoLibre\"", "Content=\"Refresh GeoJSON\"", "Text=\"Server URL\"",
        "Text=\"Project Home\"", "Text=\"GeoLibre URL\"", "PlaceholderText=\"Search...\""
    ];

    private static IEnumerable<string> XamlFiles()
    {
        var root = LocateDirectory("Caly.Core");

        return Directory.GetFiles(root, "*.axaml", SearchOption.AllDirectories)
            .Where(f => !Path.GetFileName(f).Equals("App.axaml", StringComparison.Ordinal)); // tokens/palette live there
    }

    [Fact]
    public void Controls_DoNotHardcodeNeutralColors()
    {
        var offenders = new List<string>();

        foreach (var file in XamlFiles())
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(file),
                         "(Background|Foreground|Fill|BorderBrush)=\"([A-Za-z]+)\""))
            {
                if (_neutralColors.Contains(match.Groups[2].Value, StringComparer.OrdinalIgnoreCase))
                {
                    offenders.Add($"{Path.GetFileName(file)}: {match.Value}");
                }
            }
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void Buttons_DoNotUseEmojiAsIcons()
    {
        var offenders = new List<string>();

        foreach (var file in XamlFiles())
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(file), "(Content|Text)=\"([^\"]*)\""))
            {
                var value = match.Groups[2].Value;

                // Emoji pictographs only: typographic marks such as ✓ or ✗ stay legitimate text.
                if (value.Any(c => char.IsHighSurrogate(c) || (c >= '\u2600' && c <= '\u26FF')))
                {
                    offenders.Add($"{Path.GetFileName(file)}: {match.Value}");
                }
            }
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void IconOnlyButtons_ExposeToolTipAndAutomationName()
    {
        var offenders = new List<string>();

        foreach (var file in XamlFiles())
        {
            var text = File.ReadAllText(file);

            foreach (Match glyph in Regex.Matches(text, "Classes=\"toolIcon\""))
            {
                var start = Math.Max(text.LastIndexOf("<Button", glyph.Index, StringComparison.Ordinal),
                                     text.LastIndexOf("<ToggleButton", glyph.Index, StringComparison.Ordinal));
                if (start < 0)
                {
                    continue; // not hosted by a button
                }

                var openTagEnd = text.IndexOf('>', start);
                Assert.True(openTagEnd > start, $"Unterminated button in {Path.GetFileName(file)}.");

                var end = text.IndexOf("</Button>", openTagEnd, StringComparison.Ordinal);
                var closing = text.IndexOf("</ToggleButton>", openTagEnd, StringComparison.Ordinal);
                if (closing > end || end < 0)
                {
                    end = closing;
                }

                var block = end < 0 ? text[start..(openTagEnd + 1)] : text[start..end]; // self-closing: attributes only
                if (block.Contains("Content=\"", StringComparison.Ordinal) || block.Contains("<TextBlock", StringComparison.Ordinal))
                {
                    continue; // labelled buttons state their purpose in text
                }

                var hasToolTip = Regex.IsMatch(block, "ToolTip\\.Tip=\"[^\"]+\"")
                                 || block.Contains("<ToolTip.Tip>", StringComparison.Ordinal);

                if (!hasToolTip || !Regex.IsMatch(block, "AutomationProperties\\.Name=\"[^\"]+\""))
                {
                    offenders.Add($"{Path.GetFileName(file)}: {block[..Math.Min(100, block.Length)].ReplaceLineEndings(" ")}");
                }
            }
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void DialogChrome_IsNotLeftInEnglish()
    {
        var offenders = new List<string>();

        foreach (var file in XamlFiles())
        {
            var text = File.ReadAllText(file);

            offenders.AddRange(_englishChrome.Where(text.Contains).Select(s => $"{Path.GetFileName(file)}: {s}"));
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void MonospaceText_UsesTheMonoFontToken()
    {
        // A hardcoded mono stack has no CJK fallback, which silently turns Chinese into boxes on macOS.
        var offenders = new List<string>();

        foreach (var file in XamlFiles())
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(file), "FontFamily=\"([^\"]+)\""))
            {
                if (!match.Groups[1].Value.Contains("CalyMonoFontFamily", StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetFileName(file)}: {match.Value}");
                }
            }
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    [Theory]
    [InlineData("CalyScrimBrush")]
    [InlineData("CalyOverlayBackgroundBrush")]
    [InlineData("CalyCardBackgroundBrush")]
    [InlineData("CalyCardBorderBrush")]
    [InlineData("CalyDividerBrush")]
    [InlineData("CalyPaperBrush")]
    [InlineData("CalyOnSolidBrush")]
    public void SurfaceTokens_AreDefinedInBothAppearances(string key)
    {
        var app = File.ReadAllText(LocateFile("Caly.Core", "App.axaml"));

        foreach (var theme in new[] { "Light", "Dark" })
        {
            var start = app.IndexOf($"<ResourceDictionary x:Key=\"{theme}\">", StringComparison.Ordinal);
            Assert.True(start >= 0, $"No theme dictionary for {theme}.");

            var end = app.IndexOf("</ResourceDictionary>", start, StringComparison.Ordinal);
            Assert.Contains($"x:Key=\"{key}\"", app[start..end]);
        }
    }

    private static string LocateFile(string project, string file) => Path.Combine(LocateDirectory(project), file);

    private static string LocateDirectory(string project)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var path = Path.Combine(directory.FullName, project);
            if (Directory.Exists(path))
            {
                return path;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"Could not locate {project}.");
    }
}
