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

using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Caly.Core.Models;
using Caly.Core.Services;
using Caly.Core.Services.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Caly.Core.ViewModels;

/// <summary>
/// View model that represent a PDF document.
/// </summary>
[DebuggerDisplay("[{_pdfService?.FileName}]")]
public sealed partial class DocumentViewModel : ViewModelBase
{
    public override string ToString()
    {
        return _pdfService?.FileName ?? "FileName NOT SET";
    }

    private readonly IPdfDocumentService _pdfService;
    private readonly PdfPageService _pdfPageService;
    private readonly ISettingsService _settingsService;
    private readonly IFilesService _filesService;

    private readonly CancellationTokenSource _mainCts = new();
    private readonly CancellationToken _mainToken;

    internal string? LocalPath { get; private set; }

    /// <summary>
    /// The project directory path for this document.
    /// Contains MinerU and Popo output files.
    /// </summary>
    internal string? ProjectPath { get; private set; }

    public bool IsActive => _pdfService.IsActive;

    [ObservableProperty] private ObservableCollection<PageViewModel> _pages = [];

    [ObservableProperty] private int _selectedTabIndex;

    [ObservableProperty] private bool _isPasswordProtected;

    [ObservableProperty] private TextSelection? _textSelection;

    [ObservableProperty] private Range? _visiblePages;

    [ObservableProperty] private Range? _realisedPages;

    [ObservableProperty] private Range? _visibleThumbnails;

    [ObservableProperty] private Range? _realisedThumbnails;

    [ObservableProperty] private string? _interactiveActionOver;

    [ObservableProperty] private bool _isPagesLoading = true; // Start state is true, even if pages have not started loading just yet

    /// <summary>
    /// Guard flag: prevents OnShowXxxColumnChanged partial methods from auto-loading data
    /// during the initial LoadAllColumnsAsync() Phase 1, ensuring all columns load in Phase 3.
    /// </summary>
    private bool _isInitialLoading;

    /// <summary>
    /// Starts at <c>1</c>, ends at <see cref="PageCount"/>.
    /// <para><c>null</c> if not selected.</para>
    /// </summary>
    public int? SelectedPageNumber
    {
        get;
        set
        {
            if (value.HasValue)
            {
                if (value.Value <= 0)
                {
                    throw new ArgumentException("Selected page should exist in the document.", nameof(SelectedPageNumber));
                }

                if (value.Value > PageCount)
                {
                    throw new ArgumentException("Selected page should exist in the document.", nameof(SelectedPageNumber));
                }
            }

            if (!SetProperty(ref field, value))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedPageIndex));
            GoToPreviousPageCommand.NotifyCanExecuteChanged();
            GoToNextPageCommand.NotifyCanExecuteChanged();

            // Lazy load TextLayer for the newly selected page
            if (value.HasValue)
            {
                _ = LoadTextLayerForPage(value.Value);
            }
        }
    } = 1;

    /// <summary>
    /// Starts at <c>0</c>, ends at <see cref="PageCount"/> <c>- 1</c>.
    /// <para><c>-1</c> if not selected.</para>
    /// </summary>
    public int SelectedPageIndex
    {
        get
        {
            if (SelectedPageNumber.HasValue)
            {
                return SelectedPageNumber.Value - 1;
            }

            return -1;
        }
        set
        {
            if (value != -1)
            {
                SelectedPageNumber = value + 1;
                return;
            }

            SelectedPageNumber = null;
        }
    }

    /// <summary>
    /// Lazily loads the TextLayer for a specific page.
    /// Called when the user navigates to a new page to enable text selection/search on that page.
    /// </summary>
    private async Task LoadTextLayerForPage(int pageNumber)
    {
        var page = Pages.FirstOrDefault(p => p.PageNumber == pageNumber);
        if (page is null || page.PdfTextLayer is not null)
        {
            return; // Already loaded or page doesn't exist
        }

        try
        {
            // Trigger the PdfPageService to render the text layer for this page
            await _pdfPageService.LoadTextLayerForPage(this, pageNumber, _mainToken);
        }
        catch (OperationCanceledException)
        {
            // Ignored
        }
        catch (Exception ex)
        {
            Debug.WriteExceptionToFile(ex);
        }
    }

    [ObservableProperty] private int _pageCount;

    [ObservableProperty] private string? _fileName;
    
    private readonly Lazy<Task> _loadPagesTask;
    public Task LoadPagesTask => _loadPagesTask.Value;

    /// <summary>
    /// The task that opens the document. Can be awaited to make sure the document is done opening.
    /// </summary>
    public Task<int>? WaitOpenAsync { get; private set; }

    private readonly IDisposable _searchResultsDisposable;

    private readonly ITextSearchService _textSearchService;

#if DEBUG
    public DocumentViewModel()
    {
        if (!Design.IsDesignMode)
        {
            throw new InvalidOperationException("Should only be called in Design mode.");
        }

        _mainToken = _mainCts.Token;
        _loadPagesTask = null!;
        _searchResultsDisposable = null!;
        _propertiesTask = null!;
        _bookmarksTask = null!;
        _buildSearchIndex = null!;
        _searchResultsSource = null!;

        _pdfService = new PdfPigDocumentService(new JsonSettingsService(null!));

        IsPasswordProtected = _pdfService.IsPasswordProtected;
        FileName = _pdfService.FileName;
        LocalPath = _pdfService.LocalPath;
        PageCount = _pdfService.NumberOfPages;
        TextSelection = new TextSelection(PageCount);
    }
#endif

    public DocumentViewModel(IPdfDocumentService pdfService, PdfPageService pdfPageService, ITextSearchService textSearchService, ISettingsService settingsService, IFilesService filesService)
    {
        ArgumentNullException.ThrowIfNull(pdfService, nameof(pdfService));

        System.Diagnostics.Debug.Assert(pdfService.NumberOfPages == 0);

        _mainToken = _mainCts.Token;
        _pdfService = pdfService;
        _pdfPageService = pdfPageService;
        _textSearchService = textSearchService;
        _settingsService = settingsService;
        _filesService = filesService;

        _loadPagesTask = new Lazy<Task>(LoadPages);
        
        _buildSearchIndex = new Lazy<Task>(BuildSearchIndex);

        _bookmarksTask = new Lazy<Task<HierarchicalTreeDataGridSource<PdfBookmarkNode>?>>(GetBookmarks);
        _propertiesTask = new Lazy<Task<DocumentPropertiesViewModel?>>(GetProperties);
        _embeddedFilesTask = new Lazy<Task<IReadOnlyList<PdfEmbeddedFileViewModel>>>(GetEmbeddedFiles);

        _searchResultsDisposable = SearchResults
            .GetWeakCollectionChangedObservable()
            .ObserveOn(Scheduler.Default)
            .Subscribe(e =>
            {
                Debug.ThrowOnUiThread();

                try
                {
                    switch (e.Action)
                    {
                        case NotifyCollectionChangedAction.Reset:
                            // Clear selection highlights
                            foreach (var page in Pages)
                            {
                                page.UpdateSearchResultsRanges(null);
                            }
                            break;

                        case NotifyCollectionChangedAction.Add:
                            if (e.NewItems?.Count > 0)
                            {
                                var searchResults = e.NewItems.OfType<TextSearchResult>().ToArray();

                                if (searchResults.Length == 0 || searchResults[0].PageNumber <= 0)
                                {
                                    // Clear selection highlights
                                    foreach (var page in Pages)
                                    {
                                        if (page.SearchResults is not null)
                                        {
                                            page.UpdateSearchResultsRanges(null);
                                        }
                                    }
                                }
                                else
                                {
                                    foreach (var result in searchResults)
                                    {
                                        System.Diagnostics.Debug.Assert(result.Nodes is not null);

                                        var searchRange = result.Nodes
                                            .Where(x => x is
                                                { ItemType: SearchResultItemType.Word, WordIndex: not null })
                                            .Select(x => new Range(new Index(x.WordIndex!.Value),
                                                new Index(x.WordIndex.Value + x.WordCount!.Value - 1))).ToArray();
                                        
                                        var page = GetPage(result.PageNumber);
                                        if (page is null)
                                        {
                                            continue; // Pages might still be loading
                                        }

                                        page.UpdateSearchResultsRanges(searchRange);
                                    }
                                }
                            }
                            break;

                        case NotifyCollectionChangedAction.Remove:
                        case NotifyCollectionChangedAction.Replace:
                        case NotifyCollectionChangedAction.Move:
                            throw new NotImplementedException($"SearchResults Action '{e.Action}'.");
                    }
                }
                catch (OperationCanceledException)
                {
                    // No op
                }
                catch (Exception ex)
                {
                    Debug.WriteExceptionToFile(ex);
                    Dispatcher.UIThread.Post(() => Exception = new ExceptionViewModel(ex));
                }
            });

        SearchResultsSource = new HierarchicalTreeDataGridSource<TextSearchResult>(SearchResults)
        {
            Columns =
            {
                new HierarchicalExpanderColumn<TextSearchResult>(
                    new TextColumn<TextSearchResult, string>(null, x => x.ToString()),
                    x => x.Nodes)
            }
        };

        Dispatcher.UIThread.Invoke(() =>
        {
            SearchResultsSource.RowSelection!.SingleSelect = true;
            SearchResultsSource.RowSelection.SelectionChanged += TextSearchSelectionChanged;
        }, DispatcherPriority.Send, _mainToken);
    }

    public void SetActive()
    {
        _pdfService.IsActive = true;
    }

    public void SetInactive()
    {
        _pdfService.IsActive = false;
    }

    /// <summary>
    /// Enables or disables per-page white balance correction for this document.
    /// </summary>
    public void SetScanWhiteBalance(bool enabled)
    {
        _pdfPageService.SetScanWhiteBalance(enabled);
    }

    /// <summary>
    /// Sets the project path for this document (called by PdfDocumentsManagerService).
    /// </summary>
    internal void SetProjectPath(string projectPath)
    {
        ProjectPath = projectPath;
        // Notify HasMinerUZip since it depends on ProjectPath
        OnPropertyChanged(nameof(HasMinerUZip));
    }

    /// <summary>
    /// Open the pdf document.
    /// </summary>
    /// <returns>The number of pages in the opened document. <c>0</c> if the document was not opened.</returns>
    public Task<int> OpenDocument(IStorageFile? storageFile, string? password, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(storageFile, nameof(storageFile));
        LocalPath = storageFile.Path.LocalPath;

        WaitOpenAsync = OpenDocumentCore(storageFile, password, token);
        return WaitOpenAsync;
    }

    private async Task<int> OpenDocumentCore(IStorageFile? storageFile, string? password, CancellationToken token)
    {
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(_mainToken, token);

        int pageCount = await _pdfService.OpenDocument(storageFile, password, combinedCts.Token).ConfigureAwait(false);

        System.Diagnostics.Debug.Assert(_pdfService.LocalPath == LocalPath);

        bool isPasswordProtected = _pdfService.IsPasswordProtected;
        string? fileName = _pdfService.FileName;
        int numberOfPages = _pdfService.NumberOfPages;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            IsPasswordProtected = isPasswordProtected;
            FileName = fileName;

            if (pageCount > 0)
            {
                PageCount = numberOfPages;
                TextSelection = new TextSelection(numberOfPages);
            }
        });

        if (pageCount > 0)
        {
            _pdfPageService.Initialise();
        }

        return pageCount;
    }
    
    private async Task LoadPages()
    {
        Debug.ThrowOnUiThread();
        
        await Dispatcher.UIThread.InvokeAsync(() => IsPagesLoading = true);

        try
        {
            if (PageCount == 0)
            {
                if (IsPasswordProtected)
                {
                    throw new Exception("Could not open password protected document.");
                }

                throw new Exception("Cannot load pages because document has 0 pages.");
            }

            System.Diagnostics.Debug.Assert(TextSelection is not null);

            // Use 1st page size as default page size
            var firstPage = new PageViewModel(1, TextSelection, _pdfPageService.TileRenderService, _pdfService.PpiScale);
            var pageSize = await _pdfPageService.GetPageSize(1, _mainToken).ConfigureAwait(false);
            if (pageSize.HasValue)
            {
                // Page is not yet in the collection — no UI observer yet, safe to call from thread pool
                firstPage.SetSize(pageSize.Value);
            }

            var defaultSize = firstPage.Size;

            Dispatcher.UIThread.Invoke(() => Pages.Add(firstPage));

            for (int p = 2; p <= PageCount; ++p)
            {
                _mainToken.ThrowIfCancellationRequested();
                var newPage = new PageViewModel(p, TextSelection, _pdfPageService.TileRenderService, _pdfService.PpiScale)
                {
                    Size = defaultSize
                };
                _pdfPageService.RequestPageSize(newPage);
                Dispatcher.UIThread.Invoke(() => Pages.Add(newPage),
                    DispatcherPriority.Send,
                    _mainToken); // Could do in batches
            }
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsPagesLoading = false);
        }

        // Load all columns asynchronously in order after pages are created
        // This allows users to see the PDF immediately while columns load in background
        _ = LoadAllColumnsAsync();
    }

    /// <summary>
    /// Eagerly checks for data file existence on disk and sets column visibility + loading states.
    /// This is called on the UI thread BEFORE background loading starts, so columns appear immediately
    /// with a spinner/progress indicator rather than hidden until data is fully loaded.
    /// File existence checks (File.Exists/Directory.Exists) are fast metadata operations, not file reads.
    /// </summary>
    private void EagerCheckColumns()
    {
        // MinerU
        if (HasMinerUData)
        {
            ShowMinerUColumn = true;
            MinerUStatus = MinerUParseStatus.Processing;
            IsMinerUParsing = true;
            MinerUStatusText = "Loading MinerU data...";
            System.Diagnostics.Debug.WriteLine("[EagerCheck] MinerU data exists on disk, showing column with loading state");
        }

        // Popo (Analysis column)
        if (HasPopoData)
        {
            ShowAnalysisColumn = true;
            IsPopoProcessing = true;
            PopoStatusText = "Loading Popo data...";
            System.Diagnostics.Debug.WriteLine("[EagerCheck] Popo data exists on disk, showing column with loading state");
        }

        // Semantic
        if (HasSemanticData)
        {
            ShowSemanticColumn = true;
            SemanticStatus = SemanticProcessStatus.Processing;
            IsSemanticProcessing = true;
            SemanticStatusText = "Loading Semantic data...";
            System.Diagnostics.Debug.WriteLine("[EagerCheck] Semantic data exists on disk, showing column with loading state");
        }

        // Geo
        if (HasGeoData)
        {
            ShowGeoColumn = true;
            GeoStatus = GeoProcessStatus.Processing;
            IsGeoProcessing = true;
            GeoStatusText = "Loading Geo data...";
            System.Diagnostics.Debug.WriteLine("[EagerCheck] Geo data exists on disk, showing column with loading state");
        }

        // Enriched
        if (HasEnrichedData)
        {
            ShowEnrichedColumn = true;
            IsEnrichProcessing = true;
            _enrichStatusText = "Loading Enriched data...";
            OnPropertyChanged(nameof(EnrichStatusText));
            System.Diagnostics.Debug.WriteLine("[EagerCheck] Enriched data exists on disk, showing column with loading state");
        }

        // Term
        if (HasTermData)
        {
            ShowTermColumn = true;
            IsTermProcessing = true;
            _termStatusText = "Loading Term data...";
            OnPropertyChanged(nameof(TermStatusText));
            System.Diagnostics.Debug.WriteLine("[EagerCheck] Term data exists on disk, showing column with loading state");
        }
    }

    /// <summary>
    /// Loads all column data asynchronously in three phases:
    /// Phase 1: Pre-check - detect which columns have data files on disk
    /// Phase 2: UI render - yield to UI thread so all columns appear simultaneously with loading spinners
    /// Phase 3: Sequential load - load each column's data one by one
    /// Order: MinerU → Popo → Semantic → Geo → Enriched → Term
    /// After all columns are loaded, performs fit-to-page-width.
    /// Each column's UI state is updated on the UI thread immediately after loading,
    /// ensuring the loading spinner is hidden before the next column starts loading.
    /// </summary>
    private async Task LoadAllColumnsAsync()
    {
        _isInitialLoading = true;
        try
        {
            // ================================================================
            // Phase 1: 预先检查 - detect which columns have data on disk
            // File existence checks are fast metadata operations, not file reads.
            // ================================================================
            System.Diagnostics.Debug.WriteLine("[LoadColumns] Phase 1: Pre-check columns");
            bool hasMinerU = HasMinerUData;
            bool hasPopo = HasPopoData;
            bool hasSemantic = HasSemanticData;
            bool hasGeo = HasGeoData;
            bool hasEnriched = HasEnrichedData;
            bool hasTerm = HasTermData;

            // Phase 1 (cont.): Set column visibility + loading states
            System.Diagnostics.Debug.WriteLine("[LoadColumns] Phase 1: Setting column visibility + loading states");
            if (hasMinerU)
            {
                ShowMinerUColumn = true;
                MinerUStatus = MinerUParseStatus.Processing;
                IsMinerUParsing = true;
                MinerUStatusText = "Loading MinerU data...";
            }
            if (hasPopo)
            {
                ShowAnalysisColumn = true;
                IsPopoProcessing = true;
                PopoStatusText = "Loading Popo data...";
            }
            if (hasSemantic)
            {
                ShowSemanticColumn = true;
                SemanticStatus = SemanticProcessStatus.Processing;
                IsSemanticProcessing = true;
                SemanticStatusText = "Loading Semantic data...";
            }
            if (hasGeo)
            {
                ShowGeoColumn = true;
                GeoStatus = GeoProcessStatus.Processing;
                IsGeoProcessing = true;
                GeoStatusText = "Loading Geo data...";
            }
            if (hasEnriched)
            {
                ShowEnrichedColumn = true;
                IsEnrichProcessing = true;
                _enrichStatusText = "Loading Enriched data...";
                OnPropertyChanged(nameof(EnrichStatusText));
            }
            if (hasTerm)
            {
                ShowTermColumn = true;
                IsTermProcessing = true;
                _termStatusText = "Loading Term data...";
                OnPropertyChanged(nameof(TermStatusText));
            }
            System.Diagnostics.Debug.WriteLine(
                $"[LoadColumns] Phase 1: Columns - MinerU={hasMinerU}, Popo={hasPopo}, " +
                $"Semantic={hasSemantic}, Geo={hasGeo}, Enriched={hasEnriched}, Term={hasTerm}");

            // ================================================================
            // Phase 2: UI render - yield to UI thread so all columns appear
            // simultaneously with loading spinners before any data loading starts.
            // ================================================================
            System.Diagnostics.Debug.WriteLine("[LoadColumns] Phase 2: Yielding to UI thread for rendering + fit to page width");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ZoomToPageWidth();
            });

            // ================================================================
            // Phase 3: Parallel load - MinerU and Popo load sequentially (Popo
            // depends on MinerU's PreprocBlocks/PageTypes). Remaining columns
            // (Semantic, Geo, Enriched, Term) load in parallel for faster startup.
            // ================================================================
            System.Diagnostics.Debug.WriteLine("[LoadColumns] Phase 3: Parallel loading started");

            // MinerU must load first (Popo depends on PreprocBlocks/PageTypes)
            if (hasMinerU)
            {
                await Task.Run(() => TryLoadMinerUData());
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsMinerUParsing = false;
                    MinerUStatus = MinerUParseStatus.Completed;
                    MinerUStatusText = "Loaded";
                });
            }

            // Popo must load after MinerU (depends on StructureDocument.PreprocBlocks/PageTypes)
            if (hasPopo)
            {
                await Task.Run(() => TryLoadPopoData());
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsPopoProcessing = false;
                    PopoStatus = PopoProcessStatus.Completed;
                    PopoProgress = 100;
                    PopoStatusText = $"Loaded ({PopoBlocks.Count} blocks)";
                });
            }

            // Load remaining columns in parallel (no dependencies between them)
            var loadTasks = new List<Task>();

            if (hasSemantic)
            {
                loadTasks.Add(Task.Run(async () =>
                {
                    TryLoadSemanticData();
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        IsSemanticProcessing = false;
                        SemanticStatus = SemanticProcessStatus.Completed;
                        SemanticStatusText = "Loaded";
                    });
                }));
            }

            if (hasGeo)
            {
                loadTasks.Add(Task.Run(async () =>
                {
                    TryLoadGeoData();
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        IsGeoProcessing = false;
                        GeoStatus = GeoProcessStatus.Completed;
                        GeoStatusText = "Loaded";
                    });
                }));
            }

            if (hasEnriched)
            {
                loadTasks.Add(Task.Run(async () =>
                {
                    TryLoadEnrichedData();
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        IsEnrichProcessing = false;
                        _enrichStatusText = "Loaded";
                        OnPropertyChanged(nameof(EnrichStatusText));
                    });
                }));
            }

            if (hasTerm)
            {
                loadTasks.Add(Task.Run(async () =>
                {
                    TryLoadTermData();
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        IsTermProcessing = false;
                        _termStatusText = "Loaded";
                        OnPropertyChanged(nameof(TermStatusText));
                    });
                }));
            }

            // Wait for all remaining columns to finish loading
            if (loadTasks.Count > 0)
            {
                await Task.WhenAll(loadTasks);
            }

            // Initial loading complete — allow toggle buttons to auto-load on user click
            _isInitialLoading = false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LoadColumns] Error: {ex.Message}");
            Debug.WriteExceptionToFile(ex);
        }
    }

    /// <summary>
    /// Retrieves the view model for the specified page number in the document, if available.
    /// </summary>
    /// <param name="pageNumber">The one-based page number to retrieve. Must be greater than zero and less than or equal to the total number of
    /// pages in the document.</param>
    /// <returns>The <see cref="PageViewModel"/> for the specified page number, or <see langword="null"/> if the page has not
    /// been loaded.</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="pageNumber"/> is less than or equal to zero, or greater than the total number of pages
    /// in the document.</exception>
    public PageViewModel? GetPage(int pageNumber)
    {
        if (pageNumber <= 0 || pageNumber > PageCount)
        {
            throw new ArgumentException("Page number should exist in the document.", nameof(pageNumber));
        }

        int pageIndex = pageNumber - 1;
        if (pageIndex > Pages.Count - 1)
        {
            System.Diagnostics.Debug.WriteLine($"Page {pageNumber} is not loaded yet.");
            return null;
        }

        return Pages[pageIndex];
    }

    [RelayCommand(CanExecute = nameof(CanGoToPreviousPage))]
    private void GoToPreviousPage()
    {
        if (!SelectedPageNumber.HasValue)
        {
            return;
        }

        SelectedPageNumber = Math.Max(1, SelectedPageNumber.Value - 1);
    }

    private bool CanGoToPreviousPage()
    {
        if (!SelectedPageNumber.HasValue)
        {
            return false;
        }

        return SelectedPageNumber.Value > 1;
    }

    [RelayCommand(CanExecute = nameof(CanGoToNextPage))]
    private void GoToNextPage()
    {
        if (!SelectedPageNumber.HasValue)
        {
            return;
        }

        SelectedPageNumber = Math.Min(PageCount, SelectedPageNumber.Value + 1);
    }

    private bool CanGoToNextPage()
    {
        if (!SelectedPageNumber.HasValue)
        {
            return false;
        }

        return SelectedPageNumber.Value < PageCount;
    }
    
    [RelayCommand]
    private async Task CloseDocument(CancellationToken token)
    {
        var pdfDocumentsService = App.Current?.Services?.GetRequiredService<IPdfDocumentsManagerService>()!;
        await Task.Run(() => pdfDocumentsService.CloseUnloadDocument(this), token);
    }

    [RelayCommand]
    private async Task RefreshPages()
    {
        try
        {
            await _pdfPageService.RefreshPages(new RefreshPagesRequestMessage()
            {
                Document = this,
                VisiblePages = VisiblePages,
                RealisedPages = RealisedPages,
                VisibleThumbnails = VisibleThumbnails,
                RealisedThumbnails = RealisedThumbnails
            });
        }
        catch (OperationCanceledException)
        { }
        catch (Exception ex)
        {
            Debug.WriteExceptionToFile(ex);
        }
    }

    [RelayCommand]
    private async Task RefreshThumbnails()
    {
        try
        {
            await _pdfPageService.RefreshThumbnails(new RefreshPagesRequestMessage()
            {
                Document = this,
                VisiblePages = VisiblePages,
                RealisedPages = RealisedPages,
                VisibleThumbnails = VisibleThumbnails,
                RealisedThumbnails = RealisedThumbnails
            });
        }
        catch (OperationCanceledException)
        { }
        catch (Exception ex)
        {
            Debug.WriteExceptionToFile(ex);
        }
    }

    [RelayCommand]
    private void ClearSelection()
    {
        Debug.ThrowNotOnUiThread();

        if (TextSelection is null)
        {
            return;
        }

        System.Diagnostics.Debug.Assert(TextSelection.GetStartPageIndex() <= TextSelection.GetEndPageIndex());

        TextSelection.ResetSelection();
    }

    [RelayCommand]
    private async Task Clear()
    {
        // Capture pictures/thumbnails and clear all UI-bound page properties on the UI thread
        // in one batch, then dispose the captured resources off the UI thread.
        var toDispose = new List<IDisposable?>();

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            foreach (var page in Pages)
            {
                toDispose.Add(page.PdfPicture);
                toDispose.Add(page.Thumbnail);
                page.PdfTextLayer = null;
                page.PdfPicture = null;
                page.Thumbnail = null;
            }
        });

        foreach (var item in toDispose)
        {
            item?.Dispose();
        }

        await _pdfPageService.CancelAndClear().ConfigureAwait(false);

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, false);
    }

    [RelayCommand]
    private void Activated()
    {
        App.Messenger.Send(new SelectedDocumentChangedMessage(this));
    }

    /// <summary>
    /// Shows a window listing the color spaces used by the given page (or the currently
    /// selected page when no page is supplied) and whether each is rendered faithfully
    /// or only approximately (e.g. ICCBased).
    /// </summary>
    [RelayCommand]
    private async Task ViewPageColorSpacesAsync(object? parameter)
    {
        int pageNumber = parameter is int p ? p : SelectedPageNumber ?? 0;
        if (pageNumber < 1)
        {
            return;
        }

        var dialogService = App.Current?.Services?.GetService<IDialogService>();
        if (dialogService is null)
        {
            return;
        }

        await dialogService.ShowPageColorSpacesAsync(_pdfService, pageNumber, _mainToken);
    }
}
