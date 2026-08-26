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

using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Caly.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Caly.Core.ViewModels;

/// <summary>
/// View model for displaying a single formula in the formula index view.
/// Wraps FormulaEntry with expand/collapse functionality.
/// </summary>
public partial class FormulaBlockViewModel : ObservableObject
{
    private readonly FormulaEntry _entry;

    [ObservableProperty]
    private bool _isExpanded;

    public FormulaBlockViewModel(FormulaEntry entry)
    {
        _entry = entry;
    }

    /// <summary>
    /// Toggle expand/collapse for formula details.
    /// </summary>
    [RelayCommand]
    private void ToggleExpand()
    {
        IsExpanded = !IsExpanded;
    }

    #region Forwarded properties from FormulaEntry

    public string FormulaId => _entry.FormulaId;
    public string FormulaCode => _entry.FormulaCode;
    public string RawContent => _entry.RawContent;
    public string Placeholder => _entry.Placeholder;
    public int Page => _entry.Page;
    public string FormulaType => _entry.FormulaType;
    public string? Caption => _entry.Caption;
    public int? Chapter => _entry.Chapter;

    /// <summary>
    /// Display label for the formula (e.g., "Eq.p5.f2").
    /// </summary>
    public string DisplayLabel => FormulaId;

    /// <summary>
    /// Short preview of the formula content for compact display.
    /// </summary>
    public string ContentPreview
    {
        get
        {
            var content = RawContent.Trim();
            return content.Length > 60 ? content.Substring(0, 60) + "..." : content;
        }
    }

    private string? _renderedCache;

    /// <summary>
    /// Unicode-rendered version of the LaTeX formula for visual display.
    /// </summary>
    public string RenderedContent => _renderedCache ??= LaTeXToUnicode(RawContent);

    #endregion

    #region LaTeX to Unicode

    private static readonly Dictionary<string, string> GreekMap = new()
    {
        ["alpha"] = "\u03B1", ["beta"] = "\u03B2", ["gamma"] = "\u03B3", ["delta"] = "\u03B4",
        ["epsilon"] = "\u03B5", ["zeta"] = "\u03B6", ["eta"] = "\u03B7",
        ["theta"] = "\u03B8", ["iota"] = "\u03B9", ["kappa"] = "\u03BA",
        ["lambda"] = "\u03BB", ["mu"] = "\u03BC", ["nu"] = "\u03BD", ["xi"] = "\u03BE",
        ["pi"] = "\u03C0", ["rho"] = "\u03C1", ["sigma"] = "\u03C3",
        ["tau"] = "\u03C4", ["upsilon"] = "\u03C5", ["phi"] = "\u03C6",
        ["chi"] = "\u03C7", ["psi"] = "\u03C8", ["omega"] = "\u03C9",
        ["Gamma"] = "\u0393", ["Delta"] = "\u0394", ["Theta"] = "\u0398", ["Lambda"] = "\u039B",
        ["Xi"] = "\u039E", ["Pi"] = "\u03A0", ["Sigma"] = "\u03A3",
        ["Phi"] = "\u03A6", ["Psi"] = "\u03A8", ["Omega"] = "\u03A9"
    };

    private static readonly Dictionary<string, string> SymbolMap = new()
    {
        ["times"] = "\u00D7", ["cdot"] = "\u00B7", ["pm"] = "\u00B1",
        ["leq"] = "\u2264", ["geq"] = "\u2265", ["neq"] = "\u2260", ["approx"] = "\u2248",
        ["propto"] = "\u221D", ["infty"] = "\u221E", ["sum"] = "\u2211", ["int"] = "\u222B",
        ["partial"] = "\u2202", ["nabla"] = "\u2207", ["in"] = "\u2208",
        ["rightarrow"] = "\u2192", ["leftarrow"] = "\u2190", ["Rightarrow"] = "\u21D2",
        ["ldots"] = "\u2026", ["cdots"] = "\u22EF", ["prime"] = "\u2032",
        ["degree"] = "\u00B0", ["hbar"] = "\u0127", ["ell"] = "\u2113",
        ["forall"] = "\u2200", ["exists"] = "\u2203", ["equiv"] = "\u2261", ["to"] = "\u2192"
    };

    // Precompiled combined regex: matches \command where command is in GreekMap or SymbolMap
    private static readonly Regex _greekSymbolRegex;
    private static readonly Dictionary<string, string> _combinedMap;

    static FormulaBlockViewModel()
    {
        _combinedMap = new Dictionary<string, string>(GreekMap.Count + SymbolMap.Count);
        foreach (var kvp in GreekMap) _combinedMap[kvp.Key] = kvp.Value;
        foreach (var kvp in SymbolMap) _combinedMap[kvp.Key] = kvp.Value;

        // Build alternation: longest first to avoid partial matches
        var keys = _combinedMap.Keys.OrderByDescending(k => k.Length).ToArray();
        var pattern = @"\\(" + string.Join("|", keys) + @")(?![a-zA-Z])";
        _greekSymbolRegex = new Regex(pattern, RegexOptions.Compiled);
    }

    private static readonly Regex _tagRegex = new(@"\\tag\{([^}]+)\}", RegexOptions.Compiled);
    private static readonly Regex _sqrtRegex = new(@"\\sqrt\{([^}]*)\}", RegexOptions.Compiled);
    private static readonly Regex _fontCmdRegex = new(@"\\(?:mathrm|text|mathbf|boldsymbol|operatorname)\{([^}]*)\}", RegexOptions.Compiled);
    private static readonly Regex _ddotRegex = new(@"\\ddot\{([^}]*)\}", RegexOptions.Compiled);
    private static readonly Regex _dotRegex = new(@"\\dot\{([^}]*)\}", RegexOptions.Compiled);
    private static readonly Regex _overrightarrowRegex = new(@"\\overrightarrow\{([^}]*)\}", RegexOptions.Compiled);
    private static readonly Regex _underlineRegex = new(@"\\underline\{([^}]*)\}", RegexOptions.Compiled);
    private static readonly Regex _beginArrayRegex = new(@"\\begin\{array\}[^e]*", RegexOptions.Compiled);
    private static readonly Regex _fracRegex = new(@"\\frac\{([^{}]*)\}\{([^{}]*)\}", RegexOptions.Compiled);
    private static readonly Regex _cmdWithArgRegex = new(@"\\[a-zA-Z]+\{([^}]*)\}", RegexOptions.Compiled);
    private static readonly Regex _cmdBareRegex = new(@"\\([a-zA-Z]+)", RegexOptions.Compiled);
    private static readonly Regex _superBraceRegex = new(@"\^\{([^}]*)\}", RegexOptions.Compiled);
    private static readonly Regex _superSingleRegex = new(@"\^(\\?[a-zA-Z0-9])", RegexOptions.Compiled);
    private static readonly Regex _subBraceRegex = new(@"_\{([^}]*)\}", RegexOptions.Compiled);
    private static readonly Regex _subSingleRegex = new(@"_(\\?[a-zA-Z0-9])", RegexOptions.Compiled);
    private static readonly Regex _multiSpaceRegex = new(@" {2,}", RegexOptions.Compiled);

    public static string LaTeXToUnicode(string latex)
    {
        if (string.IsNullOrEmpty(latex)) return string.Empty;
        var s = latex.Trim();

        var tm = _tagRegex.Match(s);
        string tag = "";
        if (tm.Success) { tag = tm.Groups[1].Value; s = s.Replace(tm.Value, ""); }

        s = s.Replace(@"\left", "").Replace(@"\right", "");
        s = ReplaceFractions(s);
        s = _sqrtRegex.Replace(s, m => $"\u221A({m.Groups[1].Value})");
        s = _fontCmdRegex.Replace(s, m => m.Groups[1].Value);
        s = _ddotRegex.Replace(s, m => m.Groups[1].Value + "\u0308");
        s = _dotRegex.Replace(s, m => m.Groups[1].Value + "\u0307");
        s = _overrightarrowRegex.Replace(s, m => $"\u2192({m.Groups[1].Value})");
        s = _underlineRegex.Replace(s, m => m.Groups[1].Value);
        s = _beginArrayRegex.Replace(s, " ");
        s = s.Replace(@"\end{array}", "").Replace("\\\\", "  ");

        // Single pass for all Greek + Symbol replacements
        s = _greekSymbolRegex.Replace(s, m => _combinedMap.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);

        s = _cmdWithArgRegex.Replace(s, m => m.Groups[1].Value);
        s = _cmdBareRegex.Replace(s, m => m.Groups[1].Value);
        s = _superBraceRegex.Replace(s, m => ToSuper(m.Groups[1].Value));
        s = _superSingleRegex.Replace(s, m => ToSuper(m.Groups[1].Value.TrimStart('\\')));
        s = _subBraceRegex.Replace(s, m => ToSub(m.Groups[1].Value));
        s = _subSingleRegex.Replace(s, m => ToSub(m.Groups[1].Value.TrimStart('\\')));
        s = s.Replace("{", "").Replace("}", "");
        s = _multiSpaceRegex.Replace(s, " ").Trim();

        if (!string.IsNullOrEmpty(tag)) s += $"  ({tag})";
        return s;
    }

    private static string ReplaceFractions(string s)
    {
        int n = 10;
        while (n-- > 0)
        {
            var m = _fracRegex.Match(s);
            if (!m.Success) break;
            var a = m.Groups[1].Value;
            var b = m.Groups[2].Value;
            s = s.Replace(m.Value, (a.Length > 1 ? $"({a})" : a) + "\u2044" + (b.Length > 1 ? $"({b})" : b));
        }
        return s;
    }

    private static string ToSuper(string text)
    {
        const string sup = "\u2070\u00B9\u00B2\u00B3\u2074\u2075\u2076\u2077\u2078\u2079\u207A\u207B\u02B0\u02B1\u02B2\u02B3\u02B4\u02B5\u02B6\u02B7\u02B8\u02B9\u02BA\u02BB\u02BC\u02BD\u02BE\u02BF\u02C0\u02C1\u02C2\u02C3\u02C4\u02C5\u02C6\u02C7\u02C8\u02C9\u02CA\u02CB\u02CC\u02CD\u02CE\u02CF\u02D0\u02D1\u02D2\u02D3\u02D4\u02D5\u02D6\u02D7\u02D8\u02D9\u02DA\u02DB\u02DC\u02DD\u02DE\u02DF\u02E0\u02E1\u02E2\u02E3\u02E4\u02E5\u02E6\u02E7\u02E8\u02E9\u02EA\u02EB\u02EC\u02ED\u02EE\u02EF\u02F0\u02F1\u02F2\u02F3\u02F4\u02F5\u02F6\u02F7\u02F8\u02F9\u02FA\u02FB\u02FC\u02FD\u02FE\u02FF";
        const string bc = "0123456789+-=()nabcdegklmoprstuvxzNABCDEGKLMOPRSTUVXYZ";
        var r = new StringBuilder();
        foreach (var c in text)
        {
            int i = bc.IndexOf(c);
            r.Append(i >= 0 && i < sup.Length ? sup[i] : c);
        }
        return r.ToString();
    }

    private static string ToSub(string text)
    {
        const string sub = "\u2080\u2081\u2082\u2083\u2084\u2085\u2086\u2087\u2088\u2089\u2090\u2091\u2092\u2093\u2094\u2095\u2096\u2097\u2098\u2099\u209A\u209B\u209C\u209D\u209E\u209F\u20A0\u20A1\u20A2\u20A3\u20A4\u20A5\u20A6\u20A7\u20A8\u20A9\u20AA\u20AB\u20AC\u20AD\u20AE\u20AF\u20B0\u20B1\u20B2\u20B3\u20B4\u20B5\u20B6\u20B7\u20B8\u20B9\u20BA\u20BB\u20BC\u20BD\u20BE\u20BF\u20C0\u20C1\u20C2\u20C3\u20C4\u20C5\u20C6\u20C7\u20C8\u20C9\u20CA\u20CB\u20CC\u20CD\u20CE\u20CF\u20D0\u20D1\u20D2\u20D3\u20D4\u20D5\u20D6\u20D7\u20D8\u20D9\u20DA\u20DB\u20DC\u20DD\u20DE\u20DF\u20E0\u20E1\u20E2\u20E3\u20E4\u20E5\u20E6\u20E7\u20E8\u20E9\u20EA\u20EB\u20EC\u20ED\u20EE\u20EF\u20F0\u20F1\u20F2\u20F3\u20F4\u20F5\u20F6\u20F7\u20F8\u20F9\u20FA\u20FB\u20FC\u20FD\u20FE\u20FF";
        const string bc = "0123456789+-=()nabcdegklmoprstuvxzNABCDEGKLMOPRSTUVXYZ";
        var r = new StringBuilder();
        foreach (var c in text)
        {
            int i = bc.IndexOf(c);
            r.Append(i >= 0 && i < sub.Length ? sub[i] : c);
        }
        return r.ToString();
    }

    #endregion
}