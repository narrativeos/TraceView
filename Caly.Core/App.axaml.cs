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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Avalonia.Data.Core.Plugins;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Caly.Core.Services;
using Caly.Core.Services.Interfaces;
using Caly.Core.Utilities;
using Caly.Core.ViewModels;
using Caly.Core.Views;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Caly.Core;

public partial class App : Application
{
    #region Cursors definition

    public static readonly Cursor DefaultCursor = Cursor.Default;
    public static readonly Cursor PanCursor = new(StandardCursorType.SizeAll);
    public static readonly Cursor SizeWestEastCursor = new(StandardCursorType.SizeWestEast);
    public static readonly Cursor IbeamCursor = new(StandardCursorType.Ibeam);
    public static readonly Cursor HandCursor = new(StandardCursorType.Hand);

    #endregion

    public static MainViewModel? MainViewModelInstance { get; private set; }

    public static readonly IMessenger Messenger = StrongReferenceMessenger.Default;

    private readonly FilePipeStream _pipeServer = new();
    private readonly CancellationTokenSource _listeningToFilesCts = new();
    private readonly CancellationToken _listeningToFilesToken;
    private Task? _listeningToFiles;

    private IPdfDocumentsManagerService? _pdfDocumentsService;

    public new static App? Current => Application.Current as App;

    /// <summary>
    /// Gets the <see cref="IServiceProvider"/> instance to resolve application services.
    /// </summary>
    public IServiceProvider? Services { get; private set; }

    public App() : base()
    {
        _listeningToFilesToken = _listeningToFilesCts.Token;
    }

    public override void Initialize()
    {
        // Use typeof(App) explicitly so that derived classes (e.g., DesktopApp) 
        // will load the base App XAML resources instead of looking for their own.
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Initialise dependencies
        var services = new ServiceCollection();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;

            var mainViewModel = new MainViewModel();
            MainViewModelInstance = mainViewModel;

            desktop.MainWindow = new MainWindow
            {
                DataContext = mainViewModel
            };

            services.AddSingleton<Visual>(_ => desktop.MainWindow);
            services.AddSingleton<IStorageProvider>(_ => desktop.MainWindow.StorageProvider);
            services.AddSingleton<IClipboard>(_ =>
                desktop.MainWindow.Clipboard ?? throw new ArgumentNullException(nameof(IClipboard)));

            desktop.Startup += Desktop_Startup;
            desktop.Exit += Desktop_Exit;

            if (OperatingSystem.IsMacOS())
            {
                // macOS body text is 13 pt (HIG - Typography); other platforms keep 12.
                Resources["ControlContentThemeFontSize"] = 13.0;

                // The bundled Inter has no CJK glyphs and the automatic fallback does not reach the
                // system fonts, which renders Chinese text as boxes. The system font carries the
                // platform's own fallback chain (HIG - Typography: use the system font).
                Resources["CalyUiFontFamily"] = new FontFamily(".AppleSystemUIFont");

                // Same reasoning for the monospace surfaces (Markdown/JSON/formula): Menlo has no CJK
                // glyphs, so the chain must end at the system font to keep Chinese readable.
                Resources["CalyMonoFontFamily"] = new FontFamily("Menlo, .AppleSystemUIFont");

                // NOTE: closing the last window quits the app (OnMainWindowClose) rather than keeping
                // a window-less process alive. A Dock click on a running app brings the window back
                // through the 'reopen' callback - see MacAppleEventBridge.
            }
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime activityLifetime)
        {
            MainView? mainView = null;
            activityLifetime.MainViewFactory = () => mainView = new MainView
            {
                DataContext = MainViewModelInstance ??= new MainViewModel()
            };
            services.AddSingleton<Visual>(_ => mainView ??
                throw new InvalidOperationException("MainView has not been created yet."));
            services.AddSingleton<IStorageProvider>(_ =>
                TopLevel.GetTopLevel(mainView)?.StorageProvider ??
                throw new ArgumentNullException(nameof(IStorageProvider)));
            services.AddSingleton<IClipboard>(_ =>
                TopLevel.GetTopLevel(mainView)?.Clipboard ??
                throw new ArgumentNullException(nameof(IClipboard)));
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MainView
            {
                DataContext = MainViewModelInstance ??= new MainViewModel()
            };
            services.AddSingleton<Visual>(_ => singleViewPlatform.MainView);
            services.AddSingleton<IStorageProvider>(_ =>
                TopLevel.GetTopLevel(singleViewPlatform.MainView)?.StorageProvider ??
                throw new ArgumentNullException(nameof(IStorageProvider)));
            services.AddSingleton<IClipboard>(_ =>
                TopLevel.GetTopLevel(singleViewPlatform.MainView)?.Clipboard ??
                throw new ArgumentNullException(nameof(IClipboard)));
        }
#if DEBUG
        else if (ApplicationLifetime is null && Avalonia.Controls.Design.IsDesignMode)
        {
            var mainView = new MainView { DataContext = MainViewModelInstance ??= new MainViewModel() };
            services.AddSingleton<Visual>(_ => mainView);
            services.AddSingleton<IStorageProvider>(_ => TopLevel.GetTopLevel(mainView)?.StorageProvider);
            services.AddSingleton<IClipboard>(_ => TopLevel.GetTopLevel(mainView)?.Clipboard);
        }
#endif

        services.AddSingleton<ISettingsService, JsonSettingsService>();
        services.AddSingleton<ProjectService>();
        services.AddSingleton<IFilesService, FilesService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<IPdfDocumentsManagerService, PdfDocumentsManagerService>();

        services.AddScoped<IPdfDocumentService, PdfPigDocumentService>();
        services.AddScoped<PdfPageService>();
        services.AddScoped<ITextSearchService, SearchValuesTextSearchService>();
        services.AddScoped<DocumentViewModel>();

        OverrideRegisteredServices(services);

        Services = services.BuildServiceProvider();

        // Load settings
        Services.GetRequiredService<ISettingsService>().Load();

        // We need to make sure IPdfDocumentsService singleton is initiated in UI thread
        _pdfDocumentsService = Services.GetRequiredService<IPdfDocumentsManagerService>();

        if (OperatingSystem.IsMacOS() && ApplicationLifetime is IClassicDesktopStyleApplicationLifetime)
        {
            // Opening a document from the Finder and clicking the Dock icon only reach the process
            // through the AppKit delegate callbacks - see MacAppleEventBridge.
            MacAppleEventBridge.Install(
                path => _ = OpenMacDocumentAsync(path),
                static () => (Application.Current as App)?.TryBringToFront());
        }

        base.OnFrameworkInitializationCompleted();
    }

    protected virtual void OverrideRegisteredServices(IServiceCollection services)
    {
        // No-op, for testing purpose
    }

    public bool TryBringToFront()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return false;
        }

        if (desktop.MainWindow is null)
        {
            return false;
        }

        try
        {
            desktop.MainWindow.Activate(); // Bring window to front

            Dispatcher.UIThread.Invoke(() =>
            {
                // Popup from taskbar
                if (desktop.MainWindow.WindowState == WindowState.Minimized)
                {
                    desktop.MainWindow.WindowState = WindowState.Normal;
                }
            });

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Opens a document handed over by AppKit (Finder double click, "Open With", drag onto the Dock icon).
    /// The callback comes from a native trampoline, so a failure here must never take the process down.
    /// </summary>
    private async Task OpenMacDocumentAsync(string path)
    {
        try
        {
            await OpenDoc(path, CancellationToken.None);
        }
        catch (Exception ex)
        {
            ShowExceptionWindowSafely(ex);
            Debug.WriteExceptionToFile(ex);
        }
    }

    private async void Desktop_Startup(object? sender, ControlledApplicationLifetimeStartupEventArgs e)
    {
        try
        {
            if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            {
                return;
            }

            desktop.Startup -= Desktop_Startup;

            _listeningToFiles = Task.Run(ListenToIncomingFiles); // Start listening

            if (e.Args.Length == 0)
            {
                return;
            }

            await Task.Run(() => OpenDoc(e.Args[0], CancellationToken.None));
        }
        catch (Exception ex)
        {
            ShowExceptionWindowSafely(ex);
            Debug.WriteExceptionToFile(ex);
        }
    }

    private void Desktop_Exit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        _listeningToFilesCts.Cancel();
        GC.KeepAlive(_listeningToFiles);
        
        _listeningToFilesCts.Dispose();
        _pipeServer.Dispose();
        
        if (_pdfDocumentsService is IDisposable disposable)
        {
            disposable.Dispose();
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Exit -= Desktop_Exit;
        }
    }

    private async Task ListenToIncomingFiles()
    {
        Debug.ThrowOnUiThread();

        try
        {
            await Parallel.ForEachAsync(_pipeServer.ReceivePathAsync(_listeningToFilesToken),
                _listeningToFilesToken, async (path, ct) =>
                {
                    if (string.IsNullOrEmpty(path))
                    {
                        return;
                    }

                    await OpenDoc(path, ct);
                });
        }
        catch (OperationCanceledException)
        {
            // No op
        }
        catch (Exception ex)
        {
            // Critical error...
            ShowExceptionWindowSafely(ex);
            Debug.WriteExceptionToFile(ex);
            throw;
        }
        finally
        {
            await _pipeServer.DisposeAsync();
        }
    }

    private async Task OpenDoc(string? path, CancellationToken token)
    {
        Debug.ThrowOnUiThread();

        try
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                var dialogService = Services?.GetRequiredService<IDialogService>();
                dialogService?.ShowNotification("无法打开文件",
                    "文件不存在或路径无效。",
                    NotificationType.Error);

                return;
            }

            if (_pdfDocumentsService is not null)
            {
                await _pdfDocumentsService.OpenLoadDocument(path, token);
            }
        }
        catch (Exception ex)
        {
            ShowExceptionNotificationSafely(ex);
            Debug.WriteExceptionToFile(ex);
        }
    }

    private void ShowExceptionNotificationSafely(Exception? ex)
    {
        try
        {
            if (ex is null) return;

            var dialogService = Services?.GetRequiredService<IDialogService>();
            dialogService?.ShowNotification("错误", ex.Message, NotificationType.Error);
        }
        catch
        {
            // No op
        }
    }

    private void ShowExceptionWindowSafely(Exception? ex)
    {
        try
        {
            if (ex is null) return;

            var dialogService = Services?.GetRequiredService<IDialogService>();
            dialogService?.ShowExceptionWindow(ex);
        }
        catch
        {
            // No op
        }
    }
}