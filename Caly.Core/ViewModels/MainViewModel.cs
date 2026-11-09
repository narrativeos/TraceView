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
using Caly.Core.Models;
using Caly.Core.Services.Interfaces;
using Caly.Core.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Tabalonia.Controls;

namespace Caly.Core.ViewModels;

public sealed partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly IDisposable _documentCollectionDisposable;

    public ObservableCollection<DocumentViewModel> PdfDocuments { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedDocument))]
    private int _selectedDocumentIndex;

    [ObservableProperty] private bool _isSettingsPaneOpen;

    /// <summary>
    /// Whether the document side pane is open. App-level value shared across all documents:
    /// closing the pane on one document keeps it closed for every document.
    /// </summary>
    [ObservableProperty]
    public partial bool IsDocumentPaneOpen { get; set; } = !CalyExtensions.IsMobilePlatform();

    /// <summary>
    /// Debug: show PDF layout analysis overlay (colored boxes for text blocks, lines, words).
    /// </summary>
    [ObservableProperty]
    public partial bool ShowLayoutAnalysis { get; set; }

    partial void OnShowLayoutAnalysisChanged(bool value)
    {
#if DEBUG
        Caly.Core.Controls.PageInteractiveLayerControl.ShowLayoutAnalysisDebug = value;
#endif
        // Persist to settings
        var settings = App.Current?.Services?.GetService<ISettingsService>()?.GetSettings();
        if (settings?.Debug is null)
        {
            settings = settings ?? new CalySettings();
            settings.Debug = new CalySettings.CalySettingsDebug { LayoutAnalysis = value };
        }
        else
        {
            settings.Debug.LayoutAnalysis = value;
        }
    }

    /// <summary>
    /// Width of the document side pane. App-level value (shared across all documents) persisted to settings.
    /// </summary>
    [ObservableProperty]
    public partial double PaneSize { get; set; }

    public DocumentViewModel? SelectedDocument
    {
        get
        {
            try
            {
                return (SelectedDocumentIndex < 0 || PdfDocuments.Count == 0) ? null : PdfDocuments[SelectedDocumentIndex];
            }
            catch (Exception e)
            {
                Debug.WriteExceptionToFile(e);
                return null;
            }
        }
    }

    public string Version => CalyExtensions.CalyVersion;

    /// <summary>
    /// When true, scanned pages with a warm/pink paper cast are color-corrected
    /// (per-page white balance) so their background reads as neutral white.
    /// </summary>
    [ObservableProperty]
    private bool _scanWhiteBalance;

    /// <summary>
    /// Gets the current application settings for binding in SettingsControl.
    /// </summary>
    public CalySettings Settings
    {
        get => App.Current?.Services?.GetService<ISettingsService>()?.GetSettings() ?? CalySettings.Default;
    }

    partial void OnPaneSizeChanged(double oldValue, double newValue)
    {
        App.Current?.Services?.GetService<ISettingsService>()?
            .SetProperty(CalySettings.CalySettingsProperty.PaneSize, newValue);
    }

    partial void OnScanWhiteBalanceChanged(bool value)
    {
        var settingsService = App.Current?.Services?.GetService<ISettingsService>();
        settingsService?.SetProperty(CalySettings.CalySettingsProperty.ScanWhiteBalance, value);
        settingsService?.Save();

        foreach (var document in PdfDocuments)
        {
            document.SetScanWhiteBalance(value);
        }
    }

    public MainViewModel()
    {
        // Initialize ShowLayoutAnalysis from settings
        var settings = App.Current?.Services?.GetService<ISettingsService>()?.GetSettings();
        ShowLayoutAnalysis = settings?.Debug?.LayoutAnalysis == true;
        ScanWhiteBalance = settings?.ScanWhiteBalance == true;

        _documentCollectionDisposable = PdfDocuments
            .GetWeakCollectionChangedObservable()
            .ObserveOn(Scheduler.Default)
            .Subscribe(async e =>
            {
                Debug.ThrowOnUiThread();

                // NB: Tabalonia uses a Remove + Add when moving tabs
                try
                {
                    if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems?.Count > 0)
                    {
                        foreach (var newDoc in e.NewItems.OfType<DocumentViewModel>())
                        {
                            if (newDoc.WaitOpenAsync is null)
                            {
                                throw new Exception("WaitOpenAsync is null");
                            }

                            await newDoc.WaitOpenAsync; // Make sure the doc is open before proceeding
                            await newDoc.LoadPagesTask;
                        }
                    }
                    else if (e.Action == NotifyCollectionChangedAction.Remove)
                    {
                        if (PdfDocuments.Count == 0)
                        {
                            // We want to clear any possible reference to the last PdfDocumentViewModel.
                            // The collection keeps a reference of the last document in e.OldItems
                            // We trigger a NotifyCollectionChangedAction.Reset to flush.
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                if (PdfDocuments.Count == 0)
                                {
                                    PdfDocuments.Clear();
                                }
                            });
                        }
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
    }

    public void Dispose()
    {
        _documentCollectionDisposable.Dispose();
    }
    
    [RelayCommand]
    private async Task OpenFile(CancellationToken token)
    {
        try
        {
            var pdfDocumentsService = App.Current?.Services?.GetRequiredService<IPdfDocumentsManagerService>();
            if (pdfDocumentsService is null)
            {
                throw new NullReferenceException($"Missing {nameof(IPdfDocumentsManagerService)} instance.");
            }

            await pdfDocumentsService.OpenLoadDocument(token);
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
    }

    [RelayCommand]
    private async Task CloseTab(object tabItem, CancellationToken token)
    {
        // TODO - Finish proper dispose / unload of document on close 
        DocumentViewModel? vm = tabItem switch
        {
            DragTabItem item => item.DataContext as DocumentViewModel,
            DocumentViewModel document => document,
            _ => null
        };

        if (vm is not null)
        {
            await CloseDocumentInternal(vm, token);
        }
    }

    [RelayCommand]
    private async Task CloseDocument(CancellationToken token)
    {
        DocumentViewModel? vm = SelectedDocument;
        if (vm is null)
        {
            return;
        }

        await CloseDocumentInternal(vm, token);
    }

    private static async Task CloseDocumentInternal(DocumentViewModel vm, CancellationToken token)
    {
        var pdfDocumentsService = App.Current?.Services?.GetRequiredService<IPdfDocumentsManagerService>()!;
        await Task.Run(() => pdfDocumentsService.CloseUnloadDocument(vm), token);
    }

    [RelayCommand]
    private Task PrintDocument(CancellationToken token)
    {
        DocumentViewModel? vm = SelectedDocument;
        if (vm is null)
        {
            return Task.CompletedTask;
        }

        return vm.PrintCommand.ExecuteAsync(token);
    }

    [RelayCommand]
    private void ActivateSearchTextTab()
    {
        IsDocumentPaneOpen = true;
        SelectedDocument?.SelectedTabIndex = 2;
    }

    [RelayCommand]
    private Task CopyText(CancellationToken token)
    {
        DocumentViewModel? vm = SelectedDocument;
        return vm is null ? Task.CompletedTask : vm.CopyTextCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private void ActivateNextDocument()
    {
        int lastIndex = PdfDocuments.Count - 1;

        if (lastIndex <= 0)
        {
            return;
        }

        int newIndex = SelectedDocumentIndex + 1;

        if (newIndex > lastIndex)
        {
            newIndex = 0;
        }

        SelectedDocumentIndex = newIndex;
    }

    [RelayCommand]
    private void ActivatePreviousDocument()
    {
        int lastIndex = PdfDocuments.Count - 1;

        if (lastIndex <= 0)
        {
            return;
        }

        int newIndex = SelectedDocumentIndex - 1;

        if (newIndex < 0)
        {
            newIndex = lastIndex;
        }

        SelectedDocumentIndex = newIndex;
    }

    /// <summary>
    /// Toggles the document side pane. Exposed as a command so it can also be reached
    /// from the native menu bar (View / Show Sidebar).
    /// </summary>
    [RelayCommand]
    private void ToggleDocumentPane()
    {
        IsDocumentPaneOpen = !IsDocumentPaneOpen;
    }

    /// <summary>
    /// Toggles the settings pane. Exposed as a command so it can also be reached
    /// from the keyboard shortcut (Cmd+,) and the native menu bar.
    /// </summary>
    [RelayCommand]
    private void ToggleSettings()
    {
        IsSettingsPaneOpen = !IsSettingsPaneOpen;
    }

    /// <summary>
    /// Shows the 'About' information. Exposed for the native Help menu.
    /// </summary>
    [RelayCommand]
    private void ShowAbout()
    {
        var dialogService = App.Current?.Services?.GetService<IDialogService>();

        var version = typeof(App).Assembly.GetName().Version?.ToString() ?? "0.0";

        dialogService?.ShowNotification("TraceView",
            $"TraceView {version}\nPDF 阅读与结构化分析工具。",
            Avalonia.Controls.Notifications.NotificationType.Information);
    }
}