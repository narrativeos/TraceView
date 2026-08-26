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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Caly.Core.Models;

namespace Caly.Core.Services;

/// <summary>
/// Service for extracting formulas/equations from document content.
/// Extracts both display (block) formulas and inline formulas,
/// replaces them with placeholders for NLP analysis,
/// and provides restoration functionality.
/// </summary>
public static class FormulaExtractor
{
    // ── Regex patterns for formula detection ──

    /// <summary>Matches LaTeX display math: \[ ... \]</summary>
    private static readonly Regex LatexDisplayRegex = new(
        @"\\\[(.*?)\\\]", RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>Matches LaTeX inline math: \( ... \) or $ ... $</summary>
    private static readonly Regex LatexInlineRegex = new(
        @"\\\((.*?)\\\)|\$(.+?)\$", RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>Matches LaTeX equation environment</summary>
    private static readonly Regex LatexEquationEnvRegex = new(
        @"\\begin\{equation\}(.*?)\\end\{equation\}", RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>Matches equation number: (1), (2.1), (3-1)</summary>
    private static readonly Regex EquationNumberRegex = new(
        @"\((\d+[\.\-]?\d*)\)\s*$", RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>Matches Greek letters</summary>
    private static readonly Regex GreekLetterRegex = new(
        @"[\u0391-\u03C9\u03B1-\u03BF]", RegexOptions.Compiled);

    /// <summary>Matches math symbols</summary>
    private static readonly Regex MathSymbolRegex = new(
        @"[\u2200-\u22FF\u2190-\u21FF]", RegexOptions.Compiled);

    /// <summary>Matches LaTeX commands</summary>
    private static readonly Regex LatexCommandRegex = new(
        @"\\(?:frac|sqrt|sum|int|prod|lim|partial|nabla|infty|alpha|beta|gamma|delta|" +
        @"epsilon|theta|lambda|mu|sigma|omega|Gamma|Delta|Theta|Lambda|Sigma|Omega|" +
        @"left|right|ldots|cdots|cdot|times|div|pm|leq|geq|approx|sim|equiv|" +
        @"subset|supset|in|text|mathbf|mathcal|begin|end)" +
        @"(?:\{[^}]*\}|\[[^\]]*\])?", RegexOptions.Compiled);

    /// <summary>
    /// Determines if a MinerU block is a formula block based on its Type or SourceLabel.
    /// MinerU uses various labels for equations: "equation", "formula", "interline_equation",
    /// "text / interline_equation", etc. The Type is often mapped to "text", so we need
    /// to check SourceLabel for the original MinerU type.
    /// </summary>
    public static bool IsFormulaBlock(MinerUBlock block)
    {
        if (block == null) return false;

        // Check Type first (direct equation type)
        if (block.Type == "equation")
            return true;

        // Check SourceLabel for MinerU-specific equation types
        var sourceLabel = block.SourceLabel?.ToLowerInvariant().Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(sourceLabel))
            return false;

        // MinerU equation type patterns
        var equationPatterns = new[]
        {
            "equation",
            "formula",
            "interline_equation",
            "text / interline_equation",
            "inline_equation",
            "text / inline_equation",
            "display_equation",
            "block_equation"
        };

        // Direct match
        if (equationPatterns.Contains(sourceLabel))
            return true;

        // Contains check for compound types
        if (sourceLabel.Contains("equation") || sourceLabel.Contains("formula"))
            return true;

        return false;
    }

    /// <summary>
    /// Determines if a text block likely contains a formula.
    /// </summary>
    public static bool ContainsFormula(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        if (LatexDisplayRegex.IsMatch(text) || LatexInlineRegex.IsMatch(text))
            return true;
        if (LatexEquationEnvRegex.IsMatch(text))
            return true;
        if (LatexCommandRegex.IsMatch(text))
            return true;
        if (MathSymbolRegex.IsMatch(text))
            return true;
        if (GreekLetterRegex.IsMatch(text))
            return true;
        return false;
    }

    /// <summary>
    /// Determines if a text block is entirely a display formula (block equation).
    /// </summary>
    public static bool IsDisplayFormula(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var trimmed = text.Trim();
        if (trimmed.StartsWith("\\[") && trimmed.EndsWith("\\]"))
            return true;
        if (trimmed.StartsWith("\\begin{equation}"))
            return true;
        if (IsLikelyStandaloneMath(trimmed))
            return true;
        return false;
    }

    /// <summary>
    /// Checks if text is likely a standalone math expression.
    /// Uses multiple heuristics to distinguish formulas from prose.
    /// </summary>
    private static bool IsLikelyStandaloneMath(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        if (text.Length > 200) return false;
        if (IsLikelyNonFormula(text)) return false;

        // If text contains Chinese characters, it's prose with inline math, not a standalone formula
        if (text.Any(c => c >= '\u4e00' && c <= '\u9fff'))
            return false;

        int totalChars = 0;
        int mathChars = 0;
        int latexCommands = 0;
        int greekLetters = 0;
        int mathSymbols = 0;
        bool prevWasBackslash = false;
        bool inCommandName = false; // Track if we're inside a \command name (count once per command)

        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c)) { prevWasBackslash = false; inCommandName = false; continue; }
            totalChars++;

            if ((c >= '\u0391' && c <= '\u03C9') || (c >= '\u03B1' && c <= '\u03BF'))
            { mathChars++; greekLetters++; prevWasBackslash = false; inCommandName = false; }
            else if (c >= '\u2200' && c <= '\u22FF')
            { mathChars++; mathSymbols++; prevWasBackslash = false; inCommandName = false; }
            else if (c >= '\u2190' && c <= '\u21FF')
            { mathChars++; mathSymbols++; prevWasBackslash = false; inCommandName = false; }
            else if (c == '\\')
            { mathChars++; prevWasBackslash = true; inCommandName = false; }
            else if (prevWasBackslash && char.IsLetter(c))
            {
                mathChars++;
                if (!inCommandName) { latexCommands++; inCommandName = true; }
                prevWasBackslash = true;
            }
            else if ("()+-*/=<^_{}[]".Contains(c))
            { mathChars++; prevWasBackslash = false; inCommandName = false; }
            else if (char.IsDigit(c) && (latexCommands > 0 || greekLetters > 0 || mathSymbols > 0))
            { mathChars++; prevWasBackslash = false; inCommandName = false; }
            else if (char.IsLetter(c) && c < 128 && (latexCommands > 0 || greekLetters > 0))
            { mathChars++; prevWasBackslash = false; inCommandName = false; }
            else { prevWasBackslash = false; inCommandName = false; }
        }

        // Check for common English words
        var lowerText = text.ToLower();
        var words = lowerText.Split(new[] { ' ', '\t', '\n', '\r', ',', '.', ';', ':', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
        int englishWordCount = 0;
        foreach (var word in words)
        {
            var cleanWord = Regex.Replace(word, @"[\\{}()\[\]^_]", "");
            if (IsCommonEnglishWord(cleanWord)) englishWordCount++;
        }
        if (englishWordCount >= 2) return false;

        if (totalChars == 0) return false;

        // Decision: 2+ distinct LaTeX commands = likely formula
        if (latexCommands >= 2) return true;
        // Greek/math symbols with reasonable ratio
        if ((greekLetters >= 1 || mathSymbols >= 1) && (double)mathChars / totalChars > 0.5) return true;
        // 1 LaTeX command with high ratio
        if (latexCommands >= 1 && (double)mathChars / totalChars > 0.6) return true;

        return false;
    }

    /// <summary>
    /// Checks if a word is a common English word (not a math variable or LaTeX command).
    /// </summary>
    private static bool IsCommonEnglishWord(string word)
    {
        if (string.IsNullOrEmpty(word) || word.Length < 2) return false;

        var commonWords = new HashSet<string>
        {
            "the", "and", "or", "for", "with", "this", "that", "from", "by", "to",
            "is", "are", "was", "were", "be", "been", "have", "has", "had", "do",
            "in", "on", "at", "of", "a", "an", "it", "its", "we", "our", "they",
            "their", "which", "when", "where", "how", "what", "if", "then", "but",
            "not", "no", "yes", "can", "will", "would", "could", "should", "may",
            "must", "shall", "all", "each", "every", "both", "few", "more", "most",
            "some", "any", "much", "many", "other", "another", "such", "only",
            "same", "so", "than", "too", "very", "just", "about", "above", "after",
            "again", "also", "back", "before", "below", "between", "both", "down",
            "during", "except", "first", "here", "into", "near", "once", "over",
            "part", "since", "still", "take", "there", "through", "under", "until",
            "upon", "while", "within", "without", "value", "result", "function",
            "algorithm", "method", "approach", "system", "process", "data",
            "isbn", "page", "figure", "table", "chapter", "section"
        };

        return commonWords.Contains(word.ToLower());
    }

    /// <summary>
    /// Checks if text is likely NOT a formula (ISBN, phone number, figure reference, etc.).
    /// </summary>
    private static bool IsLikelyNonFormula(string text)
    {
        var trimmed = text.Trim();

        // ISBN pattern: ISBN 978-x-xxx-xxxxx-x or ISBN x-xxx-xxxxx-x
        if (trimmed.ToUpper().StartsWith("ISBN"))
            return true;

        // Figure/Table reference: "图 1-1", "表 2-3", "Figure 1.1", "Table 3.2"
        if (trimmed.StartsWith("图") || trimmed.StartsWith("表") ||
            trimmed.StartsWith("图") || trimmed.StartsWith("Table") ||
            trimmed.StartsWith("Figure"))
            return true;

        // Page number only: "第 1 页", "Page 1"
        if (trimmed.StartsWith("第") && trimmed.EndsWith("页"))
            return true;
        if (trimmed.ToUpper().StartsWith("PAGE") && trimmed.Length < 10)
            return true;

        // Single letter with parenthesis: "a)", "b)", "c)"
        if (trimmed.Length <= 3 && trimmed.EndsWith(")") && char.IsLetter(trimmed[0]))
            return true;

        // Short alphanumeric codes: "A-1", "B-2", "X.1"
        if (trimmed.Length <= 5 && Regex.IsMatch(trimmed, @"^[A-Z]\.?\d+$"))
            return true;

        return false;
    }

    /// <summary>
    /// Extracts the equation number from text (e.g., "(1)", "(2.1)").
    /// </summary>
    public static string? ExtractEquationNumber(string text)
    {
        var match = EquationNumberRegex.Match(text);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Extracts all formulas from a document's blocks and builds a FormulaEntry list.
    /// </summary>
    public static List<FormulaEntry> ExtractFromDocument(
        StructureDocument doc,
        Dictionary<int, List<MinerUBlock>> pagesBlocks)
    {
        var formulas = new List<FormulaEntry>();
        int formulaId = 0;
        int currentPage = 0;
        int formulasOnPage = 0;
        
        System.Diagnostics.Debug.WriteLine($"[FormulaExtractor] ExtractFromDocument called with pagesBlocks count: {pagesBlocks?.Count ?? -1}");
        if (pagesBlocks != null)
        {
            int formulaBlockCount = 0;
            int textBlockCount = 0;
            int displayFormulaCount = 0;
            int firstFewBlocks = 0;
            foreach (var page in pagesBlocks.Keys.OrderBy(p => p))
            {
                foreach (var block in pagesBlocks[page])
                {
                    // Use IsFormulaBlock to detect equation blocks via SourceLabel
                    bool isFormulaBlock = IsFormulaBlock(block);
                    if (isFormulaBlock)
                    {
                        formulaBlockCount++;
                        if (firstFewBlocks < 5)
                        {
                            var preview = block.Content?.Length > 80 ? block.Content.Substring(0, 80) + "..." : block.Content;
                            System.Diagnostics.Debug.WriteLine($"[FormulaExtractor]   Formula block Page {page}, Id {block.Id}, Type={block.Type}, SourceLabel={block.SourceLabel}: \"{preview}\"");
                            firstFewBlocks++;
                        }
                    }
                    else if (block.Type == "text")
                    {
                        textBlockCount++;
                        if (firstFewBlocks < 5)
                        {
                            var preview = block.Content?.Length > 80 ? block.Content.Substring(0, 80) + "..." : block.Content;
                            var isDisplay = IsDisplayFormula(block.Content);
                            System.Diagnostics.Debug.WriteLine($"[FormulaExtractor]   Text block Page {page}, Id {block.Id}, SourceLabel={block.SourceLabel}, IsDisplayFormula={isDisplay}: \"{preview}\"");
                            if (isDisplay) displayFormulaCount++;
                            firstFewBlocks++;
                        }
                        else if (IsDisplayFormula(block.Content))
                        {
                            displayFormulaCount++;
                        }
                    }
                    else
                    {
                        firstFewBlocks++;
                    }
                }
            }
            System.Diagnostics.Debug.WriteLine($"[FormulaExtractor] Block types: formulaBlocks={formulaBlockCount}, text={textBlockCount}, displayFormulas={displayFormulaCount}");
        }

        foreach (var page in pagesBlocks.Keys.OrderBy(p => p))
        {
            currentPage = page;
            formulasOnPage = 0;
            var blocks = pagesBlocks[page];
            foreach (var block in blocks)
            {
                // Check if this is a formula block (via SourceLabel) OR a text block with display formula content
                bool isFormula = IsFormulaBlock(block) || (block.Type == "text" && IsDisplayFormula(block.Content));
                if (isFormula)
                {
                    formulaId++;
                    formulasOnPage++;
                    var eqNumber = ExtractEquationNumber(block.Content);
                    var formulaCode = string.IsNullOrEmpty(eqNumber) ? $"({formulaId})" : $"({eqNumber})";
                    var formulaIdStr = $"Eq.p{currentPage}.f{formulasOnPage}";

                    var entry = new FormulaEntry
                    {
                        FormulaId = formulaIdStr,
                        Id = formulaId,
                        FormulaCode = formulaCode,
                        RawContent = block.Content,
                        FormulaType = "display",
                        Page = page,
                        BlockIds = { block.Id },
                        SourceBlockIds = new List<string>(block.SourceBlockIds ?? new List<string>())
                    };
                    entry.EnsurePlaceholder();
                    formulas.Add(entry);
                }
            }
        }

        // Also scan inference blocks
        foreach (var block in doc.InferenceBlocks)
        {
            if (formulas.Any(f => f.BlockIds.Contains(block.Id)))
                continue;
            // Check if this is a formula block (via SourceLabel) OR a text block with display formula content
            bool isFormula = IsFormulaBlock(block) || (block.Type == "text" && IsDisplayFormula(block.Content));
            if (isFormula)
            {
                formulaId++;
                var page = block.Page;
                var formulasOnThisPage = formulas.Count(f => f.Page == page) + 1;
                var eqNumber = ExtractEquationNumber(block.Content);
                var formulaCode = string.IsNullOrEmpty(eqNumber) ? $"({formulaId})" : $"({eqNumber})";
                var formulaIdStr = $"Eq.p{page}.f{formulasOnThisPage}";

                var entry = new FormulaEntry
                {
                    FormulaId = formulaIdStr,
                    Id = formulaId,
                    FormulaCode = formulaCode,
                    RawContent = block.Content,
                    FormulaType = "display",
                    Page = block.Page,
                    BlockIds = { block.Id },
                    SourceBlockIds = new List<string>(block.SourceBlockIds ?? new List<string>())
                };
                entry.EnsurePlaceholder();
                formulas.Add(entry);
            }
        }

        return formulas;
    }

    /// <summary>
    /// Extracts formulas from a single block/node and replaces them with placeholders.
    /// Returns the modified text and the list of extracted formulas.
    /// </summary>
    public static ExtractedFormulas ExtractAndReplace(
        string content,
        int page,
        List<int> blockIds,
        int startFormulaId = 1)
    {
        var result = new ExtractedFormulas { Text = content };
        var formulas = new List<FormulaEntry>();
        var placeholderMap = new Dictionary<string, int>();

        if (string.IsNullOrEmpty(content))
            return result;

        int formulaId = startFormulaId;

        // 1. Extract LaTeX display math: \[ ... \]
        content = LatexDisplayRegex.Replace(content, match =>
        {
            var placeholder = $"[Eq.{formulaId}]";
            formulas.Add(new FormulaEntry
            {
                Id = formulaId,
                FormulaCode = $"({formulaId})",
                RawContent = match.Value,
                FormulaType = "display",
                Page = page,
                BlockIds = new List<int>(blockIds),
                Placeholder = placeholder
            });
            placeholderMap[placeholder] = formulaId - 1;
            formulaId++;
            return placeholder;
        });

        // 2. Extract LaTeX equation environment
        content = LatexEquationEnvRegex.Replace(content, match =>
        {
            var placeholder = $"[Eq.{formulaId}]";
            formulas.Add(new FormulaEntry
            {
                Id = formulaId,
                FormulaCode = $"({formulaId})",
                RawContent = match.Value,
                FormulaType = "display",
                Page = page,
                BlockIds = new List<int>(blockIds),
                Placeholder = placeholder
            });
            placeholderMap[placeholder] = formulaId - 1;
            formulaId++;
            return placeholder;
        });

        // 3. Extract LaTeX inline math: \( ... \)
        content = LatexInlineRegex.Replace(content, match =>
        {
            var placeholder = $"[Eq.{formulaId}]";
            formulas.Add(new FormulaEntry
            {
                Id = formulaId,
                FormulaCode = $"({formulaId})",
                RawContent = match.Value,
                FormulaType = "inline",
                Page = page,
                BlockIds = new List<int>(blockIds),
                Placeholder = placeholder
            });
            placeholderMap[placeholder] = formulaId - 1;
            formulaId++;
            return placeholder;
        });

        // 4. Extract $ ... $ inline math
        content = Regex.Replace(content, @"\$(.+?)\$", match =>
        {
            var placeholder = $"[Eq.{formulaId}]";
            formulas.Add(new FormulaEntry
            {
                Id = formulaId,
                FormulaCode = $"({formulaId})",
                RawContent = match.Value,
                FormulaType = "inline",
                Page = page,
                BlockIds = new List<int>(blockIds),
                Placeholder = placeholder
            });
            placeholderMap[placeholder] = formulaId - 1;
            formulaId++;
            return placeholder;
        });

        result.Text = content;
        result.Formulas = formulas;
        result.PlaceholderMap = placeholderMap;

        return result;
    }

    /// <summary>
    /// Extracts all formulas from text using regex patterns (without replacing with placeholders).
    /// Used for standalone formula extraction from document blocks.
    /// </summary>
    public static List<FormulaEntry> ExtractFormulasRegex(
        string content,
        int page,
        List<int> blockIds,
        int startFormulaId = 1)
    {
        var formulas = new List<FormulaEntry>();

        if (string.IsNullOrEmpty(content))
            return formulas;

        int formulaId = startFormulaId;

        // 1. Extract LaTeX display math: \[ ... \]
        LatexDisplayRegex.Replace(content, match =>
        {
            formulas.Add(new FormulaEntry
            {
                Id = formulaId,
                FormulaCode = $"({formulaId})",
                RawContent = match.Value,
                FormulaType = "display",
                Page = page,
                BlockIds = new List<int>(blockIds)
            });
            formulaId++;
            return match.Value; // Don't replace, just extract
        });

        // 2. Extract LaTeX equation environment
        LatexEquationEnvRegex.Replace(content, match =>
        {
            formulas.Add(new FormulaEntry
            {
                Id = formulaId,
                FormulaCode = $"({formulaId})",
                RawContent = match.Value,
                FormulaType = "display",
                Page = page,
                BlockIds = new List<int>(blockIds)
            });
            formulaId++;
            return match.Value;
        });

        // 3. Extract LaTeX inline math: \( ... \)
        Regex.Replace(content, @"\\\((.*?)\\\)", match =>
        {
            formulas.Add(new FormulaEntry
            {
                Id = formulaId,
                FormulaCode = $"({formulaId})",
                RawContent = match.Value,
                FormulaType = "inline",
                Page = page,
                BlockIds = new List<int>(blockIds)
            });
            formulaId++;
            return match.Value;
        });

        // 4. Extract $ ... $ inline math
        Regex.Replace(content, @"\\$(.+?)\\$", match =>
        {
            formulas.Add(new FormulaEntry
            {
                Id = formulaId,
                FormulaCode = $"({formulaId})",
                RawContent = match.Value,
                FormulaType = "inline",
                Page = page,
                BlockIds = new List<int>(blockIds)
            });
            formulaId++;
            return match.Value;
        });

        return formulas;
    }

    /// <summary>
    /// Restores formulas from placeholders in text.
    /// </summary>
    public static string RestoreFormulas(string text, ExtractedFormulas extracted)
    {
        if (string.IsNullOrEmpty(text) || extracted.Formulas.Count == 0)
            return text;

        foreach (var formula in extracted.Formulas.OrderByDescending(f => f.Id))
        {
            if (!string.IsNullOrEmpty(formula.Placeholder))
            {
                text = text.Replace(formula.Placeholder, formula.RawContent);
            }
        }

        return text;
    }

    /// <summary>
    /// Restores formulas from placeholders using a list of FormulaEntry.
    /// </summary>
    public static string RestoreFormulas(string text, List<FormulaEntry> formulas)
    {
        if (string.IsNullOrEmpty(text) || formulas.Count == 0)
            return text;

        foreach (var formula in formulas.OrderByDescending(f => f.Id))
        {
            if (!string.IsNullOrEmpty(formula.Placeholder))
            {
                text = text.Replace(formula.Placeholder, formula.RawContent);
            }
        }

        return text;
    }

    /// <summary>
    /// Determines if a block is a formula block based on type and source label strings.
    /// This is a string-based version for use with raw JSON data.
    /// </summary>
    public static bool IsFormulaBlock(string type, string sourceLabel)
    {
        // Check Type first (direct equation type)
        if (type == "equation")
            return true;

        // Check SourceLabel for MinerU-specific equation types
        var label = sourceLabel?.ToLowerInvariant().Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(label))
            return false;

        // MinerU equation type patterns
        var equationPatterns = new[]
        {
            "equation",
            "formula",
            "interline_equation",
            "text / interline_equation",
            "inline_equation",
            "text / inline_equation",
            "display_equation",
            "block_equation"
        };

        // Direct match
        if (equationPatterns.Contains(label))
            return true;

        // Contains check for compound types
        if (label.Contains("equation") || label.Contains("formula"))
            return true;

        return false;
    }

    /// <summary>
    /// Extracts formulas from a MinerU middle.json file directly, bypassing lazy-loaded ViewModel blocks.
    /// This ensures all pages are processed regardless of scroll/view state.
    /// Supports both "pages" dictionary format and "pdf_info" array format.
    /// </summary>
    /// <param name="jsonPath">Path to the MinerU middle.json file.</param>
    /// <returns>List of extracted FormulaEntry objects.</returns>
    public static List<FormulaEntry> ExtractFormulasFromMiddleJson(string jsonPath)
    {
        var formulas = new List<FormulaEntry>();
        if (!File.Exists(jsonPath))
        {
            System.Diagnostics.Debug.WriteLine($"[FormulaExtractor] middle.json not found: {jsonPath}");
            return formulas;
        }

        var json = File.ReadAllText(jsonPath);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Try "pages" format first: {"1": [...blocks...], "2": [...blocks...]}
        if (root.TryGetProperty("pages", out var pagesProp) && pagesProp.ValueKind == JsonValueKind.Object)
        {
            System.Diagnostics.Debug.WriteLine($"[FormulaExtractor] Parsing middle.json in 'pages' format");
            ExtractFromPagesFormat(root, ref formulas);
            System.Diagnostics.Debug.WriteLine($"[FormulaExtractor] Found {formulas.Count} formulas in 'pages' format");
            return formulas;
        }

        // Try "pdf_info" array format: [{"page_idx": 0, "para_blocks": [...]}, ...]
        if (root.TryGetProperty("pdf_info", out var pdfInfoProp) && pdfInfoProp.ValueKind == JsonValueKind.Array)
        {
            System.Diagnostics.Debug.WriteLine($"[FormulaExtractor] Parsing middle.json in 'pdf_info' format");
            ExtractFromPdfInfoFormat(pdfInfoProp, ref formulas);
            System.Diagnostics.Debug.WriteLine($"[FormulaExtractor] Found {formulas.Count} formulas in 'pdf_info' format");
            return formulas;
        }

        System.Diagnostics.Debug.WriteLine($"[FormulaExtractor] middle.json has no 'pages' or 'pdf_info' property");
        return formulas;
    }

    /// <summary>
    /// Extracts formulas from the "pages" dictionary format.
    /// </summary>
    private static void ExtractFromPagesFormat(JsonElement root, ref List<FormulaEntry> formulas)
    {
        if (!root.TryGetProperty("pages", out var pagesProp))
            return;

        int formulaId = 1;

        foreach (var pageEntry in pagesProp.EnumerateObject())
        {
            int page = int.TryParse(pageEntry.Name, out var p) ? p : 0;
            if (pageEntry.Value.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var blockElem in pageEntry.Value.EnumerateArray())
            {
                int blockNumId = blockElem.TryGetProperty("id", out var idProp) ? idProp.GetInt32() : 0;
                string type = blockElem.TryGetProperty("type", out var typeProp) ? typeProp.GetString() ?? string.Empty : string.Empty;
                string sourceLabel = blockElem.TryGetProperty("source_label", out var slProp) ? slProp.GetString() ?? string.Empty : string.Empty;
                string content = blockElem.TryGetProperty("content", out var contentProp) ? contentProp.GetString() ?? string.Empty : string.Empty;
                string blockId = blockElem.TryGetProperty("block_id", out var bidProp) ? bidProp.GetString() ?? string.Empty : string.Empty;

                if (IsFormulaBlock(type, sourceLabel))
                {
                    // The content of an equation block IS the formula itself (no delimiters needed)
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        var eqNumber = ExtractEquationNumber(content);
                        var entry = new FormulaEntry
                        {
                            FormulaId = $"Eq.p{page}.f{formulas.Count(f => f.Page == page) + 1}",
                            Id = formulaId,
                            FormulaCode = string.IsNullOrEmpty(eqNumber) ? $"({formulaId})" : $"({eqNumber})",
                            RawContent = content.Trim(),
                            FormulaType = "display",
                            Page = page,
                            BlockIds = new List<int> { blockNumId },
                            SourceBlockIds = new List<string> { blockId }
                        };
                        entry.EnsurePlaceholder();
                        formulas.Add(entry);
                        formulaId++;
                    }
                    continue;
                }

                if (type == "text" && IsDisplayFormula(content))
                {
                    var formulasOnPage = formulas.Count(f => f.Page == page) + 1;
                    var entry = new FormulaEntry
                    {
                        FormulaId = $"Eq.p{page}.f{formulasOnPage}",
                        Id = formulaId,
                        FormulaCode = $"({formulaId})",
                        RawContent = content.Trim(),
                        FormulaType = "display",
                        Page = page,
                        BlockIds = new List<int> { blockNumId },
                        SourceBlockIds = new List<string> { blockId }
                    };
                    entry.EnsurePlaceholder();
                    formulas.Add(entry);
                    formulaId++;
                }
                else if (type == "text" && !string.IsNullOrEmpty(content))
                {
                    var extracted = ExtractFormulasRegex(content, page, new List<int> { blockNumId }, formulaId);
                    foreach (var f in extracted)
                    {
                        f.FormulaId = $"Eq.p{page}.f{formulas.Count(ff => ff.Page == page) + 1}";
                        f.SourceBlockIds = new List<string> { blockId };
                        f.EnsurePlaceholder();
                        formulas.Add(f);
                    }
                    formulaId = formulas.Count + 1;
                }
            }
        }
    }

    /// <summary>
    /// Extracts formulas from the "pdf_info" array format.
    /// Each page has "page_idx" (0-based), "para_blocks", "preproc_blocks", etc.
    /// Content is extracted from "lines[].spans[].content" structure.
    /// </summary>
    private static void ExtractFromPdfInfoFormat(JsonElement pdfInfoArray, ref List<FormulaEntry> formulas)
    {
        int formulaId = 1;

        foreach (var pageInfo in pdfInfoArray.EnumerateArray())
        {
            int pageIdx = pageInfo.TryGetProperty("page_idx", out var idxProp) ? idxProp.GetInt32() : -1;
            int page = pageIdx + 1; // Convert to 1-based

            // Parse para_blocks
            if (pageInfo.TryGetProperty("para_blocks", out var paraBlocks) && paraBlocks.ValueKind == JsonValueKind.Array)
            {
                foreach (var blockElem in paraBlocks.EnumerateArray())
                {
                    ExtractFormulasFromPdfInfoBlock(blockElem, page, ref formulaId, ref formulas);
                }
            }

            // Also check preproc_blocks for any formulas
            if (pageInfo.TryGetProperty("preproc_blocks", out var preprocBlocks) && preprocBlocks.ValueKind == JsonValueKind.Array)
            {
                foreach (var blockElem in preprocBlocks.EnumerateArray())
                {
                    ExtractFormulasFromPdfInfoBlock(blockElem, page, ref formulaId, ref formulas);
                }
            }
        }
    }

    /// <summary>
    /// Extracts formulas from a single block in pdf_info format.
    /// Content is extracted from "lines[].spans[].content" structure.
    /// </summary>
    private static void ExtractFormulasFromPdfInfoBlock(
        JsonElement blockElem, int page, ref int formulaId, ref List<FormulaEntry> formulas)
    {
        string type = blockElem.TryGetProperty("type", out var typeProp) ? typeProp.GetString() ?? string.Empty : string.Empty;
        string blockId = blockElem.TryGetProperty("block_id", out var bidProp) ? bidProp.GetString() ?? string.Empty : string.Empty;

        // Extract content from lines[].spans[].content structure
        var contentParts = new List<string>();
        if (blockElem.TryGetProperty("lines", out var linesElem) && linesElem.ValueKind == JsonValueKind.Array)
        {
            foreach (var line in linesElem.EnumerateArray())
            {
                var lineParts = new List<string>();
                if (line.TryGetProperty("spans", out var spansElem) && spansElem.ValueKind == JsonValueKind.Array)
                {
                    foreach (var span in spansElem.EnumerateArray())
                    {
                        if (span.TryGetProperty("content", out var contentProp) &&
                            contentProp.ValueKind == JsonValueKind.String)
                        {
                            var spanContent = contentProp.GetString();
                            if (!string.IsNullOrEmpty(spanContent))
                                lineParts.Add(spanContent);
                        }
                    }
                }
                if (lineParts.Count > 0)
                    contentParts.Add(string.Join("", lineParts));
            }
        }

        string content = string.Join("\n", contentParts);

        // Check if this is a formula block (equation type)
        if (IsFormulaBlock(type, type))
        {
            // The content of an equation block IS the formula itself (no delimiters needed)
            if (!string.IsNullOrWhiteSpace(content))
            {
                var eqNumber = ExtractEquationNumber(content);
                var entry = new FormulaEntry
                {
                    FormulaId = $"Eq.p{page}.f{formulas.Count(f => f.Page == page) + 1}",
                    Id = formulaId,
                    FormulaCode = string.IsNullOrEmpty(eqNumber) ? $"({formulaId})" : $"({eqNumber})",
                    RawContent = content.Trim(),
                    FormulaType = "display",
                    Page = page,
                    BlockIds = new List<int>(),
                    SourceBlockIds = new List<string> { blockId }
                };
                entry.EnsurePlaceholder();
                formulas.Add(entry);
                formulaId++;
            }
            return;
        }

        // For text blocks, check if the content is a display formula
        if (type == "text" && IsDisplayFormula(content))
        {
            var formulasOnPage = formulas.Count(f => f.Page == page) + 1;
            var entry = new FormulaEntry
            {
                FormulaId = $"Eq.p{page}.f{formulasOnPage}",
                Id = formulaId,
                FormulaCode = $"({formulaId})",
                RawContent = content.Trim(),
                FormulaType = "display",
                Page = page,
                BlockIds = new List<int>(),
                SourceBlockIds = new List<string> { blockId }
            };
            entry.EnsurePlaceholder();
            formulas.Add(entry);
            formulaId++;
        }
        else if (type == "text" && !string.IsNullOrEmpty(content))
        {
            var extracted = ExtractFormulasRegex(content, page, new List<int>(), formulaId);
            foreach (var f in extracted)
            {
                f.FormulaId = $"Eq.p{page}.f{formulas.Count(ff => ff.Page == page) + 1}";
                f.SourceBlockIds = new List<string> { blockId };
                f.EnsurePlaceholder();
                formulas.Add(f);
            }
            formulaId = formulas.Count + 1;
        }
    }
}