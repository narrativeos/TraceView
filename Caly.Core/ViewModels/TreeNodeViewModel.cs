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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Collections;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Caly.Core.Models;
using Caly.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Caly.Core.ViewModels;

/// <summary>
/// View model for displaying a tree node in the tree view.
/// </summary>
public partial class TreeNodeViewModel : ObservableObject
{
    /// <summary>
    /// Supported image file extensions for lookup.
    /// </summary>
    private static readonly string[] s_imageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };

    /// <summary>
    /// Cache for artifacts directory file listings.
    /// Key: artifacts directory path (lowercased), Value: dictionary mapping filename (lowercased) → full path.
    /// This avoids repeated Directory.GetFiles calls across multiple TreeNodeViewModel instances.
    /// </summary>
    private static readonly ConcurrentDictionary<string, Dictionary<string, string>> s_artifactsFileCache = new();

    private readonly AnalysisTreeNode _node;
    private readonly string? _artifactsDirectory;
    private readonly System.Collections.Generic.Dictionary<int, MinerUBlock?>? _blockLookup;

    /// <summary>
    /// Callback invoked when this tree node is selected.
    /// Passes the first original block ID for page overlay highlighting.
    /// Set by AnalysisViewModel during tree construction.
    /// </summary>
    public Action<int?>? OnBlockSelected { get; set; }

    public TreeNodeViewModel(AnalysisTreeNode node, string? artifactsDirectory = null, StructureDocument? structureDocument = null)
    {
        _node = node;
        _artifactsDirectory = artifactsDirectory;

        // Build a lookup dictionary from block ID to MinerUBlock for accurate image matching
        if (structureDocument is not null)
        {
            _blockLookup = new System.Collections.Generic.Dictionary<int, MinerUBlock?>();
            foreach (var block in structureDocument.GetAllBlocks())
            {
                _blockLookup[block.Id] = block;
            }
        }

        // Sort children by their first page number to ensure correct display order.
        // The Popo API sometimes places nodes out of page order (e.g., P1 images after P2/P3 text),
        // so we sort by the first page in each child's location to restore logical reading order.
        var sortedChildren = node.Children
            .OrderBy(child =>
            {
                if (child.Location.Count > 0)
                    return child.Location[0].Page;
                // Nodes without location (e.g., root) go first
                return int.MaxValue;
            })
            .ToList();

        foreach (var child in sortedChildren)
        {
            Children.Add(new TreeNodeViewModel(child, artifactsDirectory, structureDocument));
        }
    }

    /// <summary>
    /// Builds a filename → full-path lookup dictionary for the given artifacts directory.
    /// Results are cached statically to avoid repeated Directory.GetFiles calls.
    /// </summary>
    private static Dictionary<string, string> BuildArtifactsFileMap(string artifactsDir)
    {
        var cacheKey = artifactsDir.ToLowerInvariant();
        return s_artifactsFileCache.GetOrAdd(cacheKey, _ =>
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var file in Directory.GetFiles(artifactsDir, "*.*", SearchOption.AllDirectories))
                {
                    if (s_imageExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                    {
                        var name = Path.GetFileName(file).ToLowerInvariant();
                        if (!map.ContainsKey(name))
                        {
                            map[name] = file;
                        }
                    }
                }
            }
            catch
            {
                // Directory access failed; return empty map
            }
            return map;
        });
    }

    /// <summary>
    /// Searches for an image file by filename using the cached artifacts directory file map.
    /// Returns the full path if found, null otherwise.
    /// </summary>
    private static string? FindImageInArtifacts(string? artifactsDir, string fileName)
    {
        if (artifactsDir is null || string.IsNullOrEmpty(fileName))
            return null;

        var fileMap = BuildArtifactsFileMap(artifactsDir);
        if (fileMap.TryGetValue(fileName, out var fullPath))
        {
            return fullPath;
        }
        return null;
    }

    public string Type => _node.Type;
    public string Title => _node.Title;
    public string Metadata => _node.Metadata;
    public string Content => _node.Content;
    public int Level => _node.Level;
    public List<int> BlockIds => _node.BlockIds;

    /// <summary>
    /// Source block IDs (UUID strings) from MinerU middle.json that compose this node.
    /// Used for tracing back to the original MinerU blocks for cross-referencing.
    /// </summary>
    public List<string> SourceBlockIds => _node.SourceBlockIds;

    /// <summary>
    /// Whether this node represents an image or image-related content.
    /// </summary>
    public bool IsImage => Type == "image" || Type == "image_footnote";

    /// <summary>
    /// Cached bitmap to avoid reloading on every access.
    /// </summary>
    private Bitmap? _cachedBitmap;

    /// <summary>
    /// Bitmap image for display (only for image nodes).
    /// Loads the image using:
    ///   1. _node.ImgPath (from popo_result.json) - resolved relative to _artifactsDirectory
    ///   2. Falls back to block-lookup matching (legacy MinerU format)
    /// </summary>
    public Bitmap? ImageBitmap
    {
        get
        {
            // Fast path: return cached bitmap immediately (no logging on cache hit)
            if (_cachedBitmap is not null)
                return _cachedBitmap;

            // Only load for image-type nodes
            if (!IsImage)
                return null;

            // Strategy 1: Use ImgPath from popo_result.json (most reliable)
            if (!string.IsNullOrEmpty(_node.ImgPath))
            {
                var imgFileName = Path.GetFileName(_node.ImgPath);
                if (!string.IsNullOrEmpty(imgFileName) && _artifactsDirectory is not null)
                {
                    var ext = Path.GetExtension(imgFileName).ToLowerInvariant();
                    if (s_imageExtensions.Contains(ext))
                    {
                        // Try multiple possible paths in order of likelihood:
                        // 1. {artifactsDir}/images/{fileName} — standard when artifactsDir is hybrid_auto
                        // 2. {artifactsDir}/{fileName} — when artifactsDir is already the images directory
                        var possiblePaths = new[]
                        {
                            Path.Combine(_artifactsDirectory, "images", imgFileName),
                            Path.Combine(_artifactsDirectory, imgFileName),
                        };

                        foreach (var imagePath in possiblePaths)
                        {
                            if (File.Exists(imagePath))
                            {
                                try
                                {
                                    _cachedBitmap = new Bitmap(imagePath);
                                    OnPropertyChanged(nameof(HasImageBitmap));
                                    return _cachedBitmap;
                                }
                                catch
                                {
                                    // Image format not supported or corrupted
                                }
                            }
                        }

                        // Last resort: use cached file map (avoids repeated Directory.GetFiles)
                        if (FindImageInArtifacts(_artifactsDirectory, imgFileName) is var fallbackPath && fallbackPath is not null)
                        {
                            try
                            {
                                _cachedBitmap = new Bitmap(fallbackPath);
                                OnPropertyChanged(nameof(HasImageBitmap));
                                return _cachedBitmap;
                            }
                            catch { }
                        }
                    }
                }
            }

            if (_artifactsDirectory is null)
                return null;

            var imagesDir = Path.Combine(_artifactsDirectory, "images");

            // Strategy 2: Use BlockIds to look up MinerUBlock and get image filename
            if (_blockLookup is not null && BlockIds.Count > 0)
            {
                foreach (var blockId in BlockIds)
                {
                    if (_blockLookup.TryGetValue(blockId, out var block) && block is not null)
                    {
                        var imageContent = block.Content;
                        if (!string.IsNullOrEmpty(imageContent))
                        {
                            var contentName = Path.GetFileName(imageContent);
                            // Try direct path first
                            var directPath = Path.Combine(imagesDir, contentName);
                            if (File.Exists(directPath))
                            {
                                try { _cachedBitmap = new Bitmap(directPath); OnPropertyChanged(nameof(HasImageBitmap)); return _cachedBitmap; }
                                catch { }
                            }
                            // Fallback: use cached file map
                            if (FindImageInArtifacts(_artifactsDirectory, contentName) is var fallbackPath2 && fallbackPath2 is not null)
                            {
                                try { _cachedBitmap = new Bitmap(fallbackPath2); OnPropertyChanged(nameof(HasImageBitmap)); return _cachedBitmap; }
                                catch { }
                            }
                            // Fallback: search by path substring match
                            var fileMap = BuildArtifactsFileMap(_artifactsDirectory);
                            foreach (var filePath in fileMap.Values)
                            {
                                if (filePath.Contains(imageContent))
                                {
                                    try { _cachedBitmap = new Bitmap(filePath); OnPropertyChanged(nameof(HasImageBitmap)); return _cachedBitmap; }
                                    catch { }
                                }
                            }
                        }
                    }
                }
            }

            // Strategy 3: Match by Content
            if (!string.IsNullOrEmpty(Content))
            {
                var contentName = Path.GetFileName(Content);
                // Try direct path first
                var directPath = Path.Combine(imagesDir, contentName);
                if (File.Exists(directPath))
                {
                    try { _cachedBitmap = new Bitmap(directPath); OnPropertyChanged(nameof(HasImageBitmap)); return _cachedBitmap; }
                    catch { }
                }
                // Fallback: use cached file map
                if (FindImageInArtifacts(_artifactsDirectory, contentName) is var fallbackPath3 && fallbackPath3 is not null)
                {
                    try { _cachedBitmap = new Bitmap(fallbackPath3); OnPropertyChanged(nameof(HasImageBitmap)); return _cachedBitmap; }
                    catch { }
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Whether a valid image is available for display.
    /// </summary>
    public bool HasImage => Type == "image" && (!string.IsNullOrEmpty(_node.ImgPath) || _artifactsDirectory is not null);

    /// <summary>
    /// Whether the image bitmap was successfully loaded.
    /// </summary>
    public bool HasImageBitmap => _cachedBitmap is not null;

    /// <summary>
    /// Image path text for display when image cannot be loaded.
    /// Always shows the image path from any available source, even if the image file doesn't exist.
    /// Only shows "[图片未找到]" when no path information is available at all.
    /// </summary>
    public string ImagePathText
    {
        get
        {
            if (Type != "image")
                return string.Empty;

            // Strategy 1: Use ImgPath from popo_result.json (always show if available)
            if (!string.IsNullOrEmpty(_node.ImgPath))
            {
                return _node.ImgPath;
            }

            // Strategy 2: Try to get path from MinerUBlock by BlockIds
            if (_blockLookup is not null && BlockIds.Count > 0)
            {
                foreach (var blockId in BlockIds)
                {
                    if (_blockLookup.TryGetValue(blockId, out var block) && block is not null
                        && !string.IsNullOrEmpty(block.Content))
                    {
                        return block.Content;
                    }
                }
            }

            // Strategy 3: Try Content field (may contain image path for some formats)
            if (!string.IsNullOrEmpty(Content))
            {
                return Content;
            }

            // Strategy 4: Try Metadata field (may contain image_footnote with path info)
            if (!string.IsNullOrEmpty(Metadata))
            {
                return Metadata;
            }

            // Strategy 5: Try Title (some Popo responses put path info in title)
            if (!string.IsNullOrEmpty(Title))
            {
                return Title;
            }

            // No path information available from any source
            return "[图片未找到]";
        }
    }

    /// <summary>
    /// Whether this node is a pure image (not image_footnote).
    /// Pure images show bitmap + metadata, image_footnotes show content text.
    /// </summary>
    public bool IsPureImage => Type == "image";

    /// <summary>
    /// Whether this node is an image footnote (shows content text, not bitmap).
    /// </summary>
    public bool IsImageFootnote => Type == "image_footnote";

    /// <summary>
    /// Whether this node carries structured table data (table_data or table_key_value).
    /// Such nodes are rendered with a DataGrid instead of plain text content.
    /// </summary>
    public bool IsStructuredTable => Type is "table_data" or "table_key_value";

    /// <summary>
    /// Whether this node is a legacy table whose HTML content was parsed into a grid.
    /// </summary>
    public bool IsHtmlTable => _node.GridRows is not null;

    /// <summary>
    /// Whether this node actually carries renderable table data:
    /// table_data with headers + rows, table_key_value with pairs,
    /// or a legacy table with a parsed HTML grid.
    /// Older Popo files may carry table_data / table_key_value nodes whose
    /// content is plain markdown without structured fields — those fall back
    /// to plain text rendering instead of an empty DataGrid.
    /// </summary>
    public bool HasStructuredData =>
        (Type == "table_data" && _node.Headers is { Count: > 0 } && _node.Rows is not null) ||
        (Type == "table_key_value" && _node.Pairs is { Count: > 0 }) ||
        IsHtmlTable;

    /// <summary>
    /// Whether this node should be rendered with a DataGrid
    /// (structured table sub-nodes or legacy HTML tables).
    /// Nodes without structured data fall back to plain text content.
    /// A parent table node that has structured children (table_data / table_key_value)
    /// does NOT render its own HTML-parsed grid, to avoid showing the same table twice
    /// (the children already render the structured DataGrids).
    /// </summary>
    public bool ShowTableGrid => HasStructuredData && !HasStructuredTableChildren;

    /// <summary>
    /// Whether this table node has structured children (table_data / table_key_value).
    /// The raw HTML content preview is redundant in that case.
    /// </summary>
    public bool HasStructuredTableChildren =>
        Type == "table" && _node.Children.Any(c => c.Type is "table_data" or "table_key_value");

    /// <summary>
    /// DataGrid headers visibility: column headers for structured tables,
    /// none for legacy HTML tables (faithful to the source PDF layout).
    /// </summary>
    public DataGridHeadersVisibility TableHeadersVisibility =>
        IsHtmlTable ? DataGridHeadersVisibility.None : DataGridHeadersVisibility.Column;

    /// <summary>
    /// Whether the copy-markdown button should be shown for this node.
    /// </summary>
    public bool CanCopyTable => ShowTableGrid;

    /// <summary>
    /// Column headers for table_data nodes (null for other node types).
    /// </summary>
    public List<TableHeader>? Headers => _node.Headers;

    /// <summary>
    /// Structured data rows for table_data nodes (null for other node types).
    /// </summary>
    public List<TableDataRow>? Rows => _node.Rows;

    /// <summary>
    /// Key-value pairs for table_key_value nodes (null for other node types).
    /// </summary>
    public List<TableKeyValuePair>? Pairs => _node.Pairs;

    private ObservableCollection<DataGridColumn>? _tableColumns;

    /// <summary>
    /// Cell template for numeric columns: right-aligned text so values line up
    /// and numeric columns are visually distinguishable from text columns.
    /// (Avalonia 12's DataGrid has no per-column ElementStyle, so numeric
    /// columns use a template column with a right-aligned TextBlock.)
    /// </summary>
    internal static Avalonia.Controls.Templates.IDataTemplate BuildNumericCellTemplate(int index) =>
        new Avalonia.Controls.Templates.FuncDataTemplate<object>((dataItem, _) =>
        {
            var textBlock = new TextBlock
            {
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right
            };
            textBlock.Bind(TextBlock.TextProperty, new Binding($"[{index}]"));
            return textBlock;
        });

    /// <summary>
    /// Whether the table_data column at the given index holds numeric values.
    /// A column is numeric when at least one non-empty cell carries a typed
    /// numeric value (from the newer Popo format) and no non-numeric value
    /// outnumbers the numeric ones.
    /// </summary>
    private bool IsNumericColumn(int index)
    {
        if (_node.Headers is not { } headers || index >= headers.Count)
            return false;
        if (_node.Rows is not { Count: > 0 } rows)
            return false;

        var name = headers[index].Name;
        int numeric = 0, nonNumeric = 0;
        foreach (var row in rows)
        {
            if (row.Cells.TryGetValue(name, out var cell) && !string.IsNullOrWhiteSpace(cell.Raw))
            {
                // IsNumeric is captured at parse time — never touch cell.Value here,
                // whose backing JsonDocument may have been disposed by then.
                if (cell.IsNumeric)
                    numeric++;
                else
                    nonNumeric++;
            }
        }
        return numeric > 0 && numeric >= nonNumeric;
    }

    /// <summary>
    /// DataGrid columns for structured table nodes.
    /// table_data: one column per header, bound to the row indexer.
    /// table_key_value: fixed two columns (key / value).
    /// </summary>
    public ObservableCollection<DataGridColumn>? TableColumns
    {
        get
        {
            if (_tableColumns is not null)
                return _tableColumns;

            var columns = new ObservableCollection<DataGridColumn>();
            if (Type == "table_data" && _node.Headers is { Count: > 0 } headers)
            {
                for (int i = 0; i < headers.Count; i++)
                {
                    // DisplayName flattens multi-tier headers: "group·name (unit)"
                    if (IsNumericColumn(i))
                    {
                        // Numeric columns use a right-aligned template so values
                        // line up and are visually distinguishable from text.
                        columns.Add(new DataGridTemplateColumn
                        {
                            Header = headers[i].DisplayName,
                            CellTemplate = BuildNumericCellTemplate(i)
                        });
                    }
                    else
                    {
                        columns.Add(new DataGridTextColumn
                        {
                            Header = headers[i].DisplayName,
                            Binding = new Binding($"[{i}]")
                        });
                    }
                }
            }
            else if (Type == "table_key_value")
            {
                columns.Add(new DataGridTextColumn { Header = "键", Binding = new Binding("[0]") });
                columns.Add(new DataGridTextColumn { Header = "值", Binding = new Binding("[1]") });
            }
            else if (IsHtmlTable && _node.GridRows is { } gridRows)
            {
                var colCount = gridRows.Max(row => row.Count);
                for (int i = 0; i < colCount; i++)
                {
                    columns.Add(new DataGridTextColumn
                    {
                        Header = string.Empty,
                        Binding = new Binding($"[{i}]")
                    });
                }
            }

            _tableColumns = columns;
            return columns;
        }
    }

    /// <summary>
    /// Applies the table columns to a DataGrid. In Avalonia 12, DataGrid.Columns
    /// is a read-only collection, so columns must be added in code.
    /// </summary>
    public static void ApplyTableColumns(DataGrid dataGrid)
    {
        if (dataGrid.DataContext is TreeNodeViewModel vm && vm.TableColumns is { } columns)
        {
            dataGrid.Columns.Clear();
            foreach (var column in columns)
                dataGrid.Columns.Add(column);
        }
    }

    /// <summary>
    /// Copies the node's table Markdown to the system clipboard.
    /// Returns true if the copy succeeded.
    /// </summary>
    public static async Task<bool> CopyTableMarkdownAsync(Control control)
    {
        if (control.DataContext is not TreeNodeViewModel vm)
            return false;

        var markdown = PopoJsonService.GenerateTableMarkdown(vm.GetSourceNode());
        if (string.IsNullOrEmpty(markdown))
            return false;

        var clipboard = TopLevel.GetTopLevel(control)?.Clipboard;
        if (clipboard is null)
            return false;

        await clipboard.SetTextAsync(markdown);
        return true;
    }

    /// <summary>
    /// Shows transient "copied" feedback on a copy button (icon swap for ~1.2s).
    /// </summary>
    public static void ShowCopyFeedback(Control control)
    {
        if (control is not Button button)
            return;

        var original = button.Content;
        button.Content = "✅";

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            button.Content = original;
        };
        timer.Start();
    }

    private List<DynamicTableRow>? _tableRows;

    /// <summary>
    /// DataGrid rows for structured table nodes.
    /// table_data: one row per TableDataRow, values ordered by Headers.
    /// table_key_value: one row per pair (key, "raw unit").
    /// </summary>
    public List<DynamicTableRow>? TableRows
    {
        get
        {
            if (_tableRows is not null)
                return _tableRows;

            var rows = new List<DynamicTableRow>();
            if (Type == "table_data" && _node.Headers is { Count: > 0 } headers && _node.Rows is not null)
            {
                foreach (var row in _node.Rows)
                {
                    var values = new List<string>(headers.Count);
                    foreach (var header in headers)
                    {
                        values.Add(row.Cells.TryGetValue(header.Name, out var cell) ? cell.Raw : string.Empty);
                    }
                    rows.Add(new DynamicTableRow(values));
                }
            }
            else if (Type == "table_key_value" && _node.Pairs is not null)
            {
                foreach (var pair in _node.Pairs)
                {
                    var display = string.IsNullOrEmpty(pair.Value.Unit)
                        ? pair.Value.Raw
                        : $"{pair.Value.Raw} {pair.Value.Unit}";
                    rows.Add(new DynamicTableRow(new List<string> { pair.Key, display }));
                }
            }
            else if (IsHtmlTable && _node.GridRows is not null)
            {
                foreach (var row in _node.GridRows)
                {
                    rows.Add(new DynamicTableRow(row));
                }
            }

            _tableRows = rows;
            return rows;
        }
    }

    /// <summary>
    /// Whether to show the content preview (collapsed, non-image nodes + image_footnote).
    /// Table nodes show a DataGrid instead of text content; parent table nodes with
    /// structured children hide the redundant raw HTML preview. Nodes with ordered
    /// lists render structured content instead of the plain text preview.
    /// </summary>
    public bool ShowContentPreview => !IsContentExpanded && !IsPureImage && !ShowTableGrid && !HasStructuredTableChildren && !HasStructuredContent;

    /// <summary>
    /// Whether to show the full content (expanded, non-image nodes + image_footnote).
    /// Table nodes show a DataGrid instead of text content; parent table nodes with
    /// structured children hide the redundant raw HTML preview. Nodes with ordered
    /// lists render structured content instead of the plain text.
    /// </summary>
    public bool ShowFullContent => IsContentExpanded && !IsPureImage && !ShowTableGrid && !HasStructuredTableChildren && !HasStructuredContent;

    // === Structured content (nodes with ordered lists) ===

    /// <summary>
    /// Whether the node carries ordered lists detected by the Popo API.
    /// </summary>
    public bool HasOrderedLists => _node.OrderedLists is { Count: > 0 };

    private List<ContentDisplayLine>? _structuredContentLines;

    /// <summary>
    /// Structured display lines of the node's content: content segments mapped
    /// against the ordered lists' segment ranges. List segments become numbered
    /// lines; other segments become plain paragraphs. Cached after first build.
    /// </summary>
    public List<ContentDisplayLine>? StructuredContentLines
    {
        get
        {
            if (_structuredContentLines is not null)
                return _structuredContentLines;
            _structuredContentLines = PopoJsonService.BuildContentLines(_node);
            return _structuredContentLines;
        }
    }

    /// <summary>
    /// Whether the node's content should be rendered in structured mode
    /// (ordered lists resolved into numbered lines).
    /// </summary>
    public bool HasStructuredContent =>
        HasOrderedLists && (StructuredContentLines?.Count ?? 0) > 0;

    /// <summary>
    /// Whether to show the structured content preview (collapsed).
    /// </summary>
    public bool ShowStructuredPreview => HasStructuredContent && !IsContentExpanded;

    /// <summary>
    /// Whether to show the full structured content (expanded).
    /// </summary>
    public bool ShowStructuredFull => HasStructuredContent && IsContentExpanded;

    /// <summary>
    /// Collapsed preview of the structured content: the first few lines joined
    /// with newlines, with an ellipsis when truncated.
    /// </summary>
    public string StructuredContentPreview
    {
        get
        {
            var lines = StructuredContentLines;
            if (lines is null || lines.Count == 0)
                return string.Empty;

            var preview = lines.Take(3)
                .Select(l => l.IsListItem ? $"{l.Number} {l.Text}" : l.Text)
                .ToList();
            var text = string.Join("\n", preview);
            return lines.Count > 3 ? text + "\n..." : text;
        }
    }

    /// <summary>
    /// Whether to show the metadata/caption (pure image nodes with non-empty metadata).
    /// </summary>
    public bool ShowMetadata => IsPureImage && !string.IsNullOrEmpty(Metadata);

    /// <summary>
    /// Display metadata for image nodes (caption/footnote text).
    /// Shows the metadata content which often contains the image_footnote text
    /// merged by the Popo API.
    /// </summary>
    public string DisplayMetadata
    {
        get
        {
            if (string.IsNullOrEmpty(Metadata))
                return string.Empty;
            return Metadata;
        }
    }

    [ObservableProperty]
    private ObservableCollection<TreeNodeViewModel> _children = new();

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isContentExpanded = true;

    /// <summary>
    /// Actual rendered height of this tree node item in the UI.
    /// Set by the view after layout to enable accurate connection line positioning.
    /// </summary>
    [ObservableProperty]
    private double _actualHeight = 80.0;  // Default fallback height

    /// <summary>
    /// Display title (truncated to 50 chars).
    /// </summary>
    public string DisplayTitle
    {
        get
        {
            // Filter out API placeholder titles
            var rawTitle = Title;
            if (string.IsNullOrEmpty(rawTitle) ||
                rawTitle == "Default Title")
            {
                rawTitle = $"[{Type}]";
            }
            return rawTitle.Length > 50 ? rawTitle.Substring(0, 50) + "..." : rawTitle;
        }
    }

    /// <summary>
    /// Whether the node has a non-empty title.
    /// </summary>
    public bool HasTitle => !string.IsNullOrEmpty(Title);

    /// <summary>
    /// Type icon character for display.
    /// </summary>
    public string TypeIcon
    {
        get
        {
            return Type switch
            {
                "text" => "\uD83D\uDCD4",       // 📄
                "page_number" => "\uD83D\uDD22", // 🔢
                "image" => "\uD83D\uDD92",      // 🖼️
                "image_footnote" => "\uD83D\uDD92", // 🖼️
                "table" => "\uD83D\uDCCA",      // 📊
                "table_data" => "\uD83D\uDCCA", // 📊
                "table_key_value" => "\uD83D\uDD11", // 🔑
                "title" => "\uD83D\uDCCC",      // 📌
                "root" => "\uD83C\uDF33",       // 🌳
                _ => "\uD83D\uDCC1",            // 📁
            };
        }
    }

    /// <summary>
    /// Type color for card border/accent.
    /// </summary>
    public string TypeColorHex
    {
        get
        {
            return Type switch
            {
                "text" => "#4CAF50",      // Green
                "page_number" => "#9E9E9E", // Gray
                "image" => "#2196F3",     // Blue
                "image_footnote" => "#2196F3", // Blue
                "table" => "#FF9800",     // Orange
                "table_data" => "#FF9800", // Orange
                "table_key_value" => "#FF9800", // Orange
                "title" => "#9C27B0",     // Purple
                "root" => "#795548",      // Brown
                _ => "#607D8B",           // Blue Gray
            };
        }
    }

    /// <summary>
    /// Block count shorthand.
    /// </summary>
    public int BlockCount => BlockIds.Count;

    /// <summary>
    /// Page range string like "P1-4" or "P2".
    /// </summary>
    public string PageRange
    {
        get
        {
            var pages = _node.Location.Select(l => l.Page).Distinct().OrderBy(p => p).ToList();
            if (pages.Count == 0) return "";
            if (pages.Count == 1) return $"P{pages[0]}";
            return $"P{pages.First()}-{pages.Last()}";
        }
    }

    /// <summary>
    /// Unique page count.
    /// </summary>
    public int UniquePageCount
    {
        get
        {
            return _node.Location.Select(l => l.Page).Distinct().Count();
        }
    }

    /// <summary>
    /// Content preview (truncated to 120 chars).
    /// </summary>
    public string ContentPreview
    {
        get
        {
            if (string.IsNullOrEmpty(Content))
                return string.Empty;

            // Remove <|txt_split|> markers for cleaner preview
            var clean = Content.Replace("<|txt_split|>", "\n");
            return clean.Length > 120 ? clean[..120] + "..." : clean;
        }
    }

    /// <summary>
    /// Clean content for display (without <|txt_split|> markers).
    /// For structured table nodes, renders the structured data as plain text
    /// (fallback for contexts without a DataGrid, e.g., the block list).
    /// </summary>
    public string DisplayContent
    {
        get
        {
            if (IsStructuredTable)
            {
                var structured = BuildStructuredTableText();
                if (structured.Length > 0)
                    return structured;
                // No structured data (older Popo format): fall through to the
                // raw content (e.g., a markdown table) so the node is not empty.
            }

            if (string.IsNullOrEmpty(Content))
                return string.Empty;
            return Content.Replace("<|txt_split|>", "\n");
        }
    }

    /// <summary>
    /// Renders structured table data (table_data / table_key_value) as plain text.
    /// </summary>
    private string BuildStructuredTableText()
    {
        var sb = new System.Text.StringBuilder();

        if (Type == "table_data" && _node.Headers is { Count: > 0 } headers && _node.Rows is not null)
        {
            sb.AppendLine(string.Join(" | ", headers.Select(h => h.Name)));
            foreach (var row in _node.Rows)
            {
                var cells = new System.Text.StringBuilder();
                for (int i = 0; i < headers.Count; i++)
                {
                    if (i > 0)
                        cells.Append(" | ");
                    cells.Append(row.Cells.TryGetValue(headers[i].Name, out var cell) ? cell.Raw : string.Empty);
                }
                sb.AppendLine(cells.ToString());
            }
        }
        else if (Type == "table_key_value" && _node.Pairs is not null)
        {
            foreach (var pair in _node.Pairs)
            {
                var display = string.IsNullOrEmpty(pair.Value.Unit)
                    ? pair.Value.Raw
                    : $"{pair.Value.Raw} {pair.Value.Unit}";
                sb.AppendLine($"{pair.Key}: {display}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Whether content is long and needs collapsible display.
    /// </summary>
    public bool IsContentLong
    {
        get
        {
            var clean = DisplayContent;
            return clean.Length > 120;
        }
    }

    /// <summary>
    /// Whether the expand/collapse button should be shown.
    /// Table nodes render a DataGrid (no text to expand); parent table nodes with
    /// structured children hide the redundant raw HTML preview entirely.
    /// </summary>
    public bool CanToggleContentExpand => IsContentLong && !ShowTableGrid && !HasStructuredTableChildren;

    /// <summary>
    /// Toggle content expand/collapse.
    /// </summary>
    [RelayCommand]
    private void ToggleContentExpand()
    {
        IsContentExpanded = !IsContentExpanded;
    }

    /// <summary>
    /// Display info string with level and block count.
    /// </summary>
    public string DisplayInfo => $"level:{Level} blocks:[{string.Join(",", BlockIds)}]";

    /// <summary>
    /// Block ids display (first 5 + count).
    /// </summary>
    public string BlockIdsDisplay
    {
        get
        {
            if (BlockIds.Count <= 5)
                return string.Join(",", BlockIds);
            return string.Join(",", BlockIds.Take(5)) + $"... ({BlockIds.Count} total)";
        }
    }

    /// <summary>
    /// Total block count including descendants.
    /// </summary>
    public int TotalBlockCount
    {
        get
        {
            int count = BlockIds.Count;
            foreach (var child in Children)
            {
                count += child.TotalBlockCount;
            }
            return count;
        }
    }

    [RelayCommand]
    private void ToggleExpand()
    {
        IsExpanded = !IsExpanded;
    }

    [RelayCommand]
    private void ToggleSelect()
    {
        IsSelected = !IsSelected;

        // Notify AnalysisViewModel to highlight corresponding page blocks
        if (IsSelected)
        {
            var firstId = BlockIds.Count > 0 ? BlockIds[0] : (int?)null;
            OnBlockSelected?.Invoke(firstId);
        }
        else
        {
            OnBlockSelected?.Invoke(null);
        }
    }

    #region Semantic Analysis

    private SemanticBlockResult? _semanticResult;

    /// <summary>
    /// Gets the semantic analysis result for this node (populated after NLP processing).
    /// </summary>
    public SemanticBlockResult? SemanticResult => _semanticResult;

    /// <summary>
    /// Whether this node has semantic analysis results.
    /// </summary>
    public bool HasSemanticResult => _semanticResult is not null;

    /// <summary>
    /// Sets the semantic analysis result for this node.
    /// </summary>
    public void SetSemanticResult(SemanticBlockResult result)
    {
        _semanticResult = result;
    }

    /// <summary>
    /// Gets the underlying source AnalysisTreeNode.
    /// </summary>
    public AnalysisTreeNode GetSourceNode() => _node;

    /// <summary>
    /// Display string for entities: "MATERIAL:碳钢, LOCATION:海淀区"
    /// </summary>
    public string EntitiesDisplay
    {
        get
        {
            if (_semanticResult is null || _semanticResult.Entities.Count == 0)
                return string.Empty;
            return string.Join(", ", _semanticResult.Entities.Select(e => $"{e.Category}:{e.Text}"));
        }
    }

    /// <summary>
    /// Display string for relations: "碳钢->COMPOSED_OF->钢"
    /// </summary>
    public string RelationsDisplay
    {
        get
        {
            if (_semanticResult is null || _semanticResult.Relations.Count == 0)
                return string.Empty;
            return string.Join("; ", _semanticResult.Relations.Select(r => $"{r.Subject}->{r.Predicate}->{r.ObjectText}"));
        }
    }

    /// <summary>
    /// Entity count for this node.
    /// </summary>
    public int EntityCount => _semanticResult?.Entities.Count ?? 0;

    /// <summary>
    /// Relation count for this node.
    /// </summary>
    public int RelationCount => _semanticResult?.Relations.Count ?? 0;

    /// <summary>
    /// Token count for this node.
    /// </summary>
    public int TokenCount => _semanticResult?.Tokens.Count ?? 0;

    #endregion
}

/// <summary>
/// A dynamic row for DataGrid display of structured Popo table data.
/// Implements <see cref="System.Collections.IList"/> so DataGrid columns can bind
/// to positional values via indexer bindings (e.g., "{Binding [0]}").
/// </summary>
public sealed class DynamicTableRow : System.Collections.IList
{
    private readonly List<object?> _values;

    public DynamicTableRow(List<string> values)
    {
        _values = values.ConvertAll(v => (object?)v);
    }

    public object? this[int index]
    {
        get => _values[index];
        set => _values[index] = value;
    }

    public int Count => _values.Count;

    public bool IsReadOnly => false;
    public bool IsFixedSize => false;
    public bool IsSynchronized => false;
    public object SyncRoot => this;

    public int Add(object? value)
    {
        // Note: List<T>.Add returns void in .NET 10 (int in .NET 9),
        // so capture the index before adding to stay compatible with both.
        int index = _values.Count;
        _values.Add(value);
        return index;
    }

    public void Clear() => _values.Clear();

    public bool Contains(object? value) => _values.Contains(value);

    public void CopyTo(Array array, int index)
    {
        ((System.Collections.ICollection)_values).CopyTo(array, index);
    }

    public System.Collections.IEnumerator GetEnumerator() => _values.GetEnumerator();

    public int IndexOf(object? value) => _values.IndexOf(value);

    public void Insert(int index, object? value) => _values.Insert(index, value);

    public void Remove(object? value) => _values.Remove(value);

    public void RemoveAt(int index) => _values.RemoveAt(index);
}
