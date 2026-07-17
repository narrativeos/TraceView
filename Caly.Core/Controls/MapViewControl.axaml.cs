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
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Data;
using Avalonia.Input;
using Caly.Core.Models;
using CommunityToolkit.Mvvm.Input;

namespace Caly.Core.Controls;

/// <summary>
/// Map view control for displaying location entities on GeoLibre map.
/// </summary>
public partial class MapViewControl : UserControl
{
    private TextBlock? _statusText;
    private TextBlock? _geoJsonPathText;
    private Button? _openInGeoLibreBtn;
    private Button? _exportGeoLibreBtn;
    private Button? _refreshBtn;

    private string? _geoJsonFilePath;
    private string? _geoLibreBaseUrl;

    public string? GeoJsonFilePath
    {
        get => _geoJsonFilePath;
        set => SetAndRaise(GeoJsonFilePathProperty, ref _geoJsonFilePath, value);
    }

    public static readonly DirectProperty<MapViewControl, string?> GeoJsonFilePathProperty =
        AvaloniaProperty.RegisterDirect<MapViewControl, string?>(
            nameof(GeoJsonFilePath),
            o => o._geoJsonFilePath,
            (o, v) => o.GeoJsonFilePath = v);

    public string? GeoLibreBaseUrl
    {
        get => _geoLibreBaseUrl;
        set => SetAndRaise(GeoLibreBaseUrlProperty, ref _geoLibreBaseUrl, value);
    }

    public static readonly DirectProperty<MapViewControl, string?> GeoLibreBaseUrlProperty =
        AvaloniaProperty.RegisterDirect<MapViewControl, string?>(
            nameof(GeoLibreBaseUrl),
            o => o._geoLibreBaseUrl,
            (o, v) => o.GeoLibreBaseUrl = v);

    public MapViewControl()
    {
        _geoJsonFilePath = "";
        _geoLibreBaseUrl = "https://web.geolibre.app";
    }

    private IRelayCommand? _refreshCommand;
    private IRelayCommand? _exportGeoLibreCommand;

    public IRelayCommand? RefreshCommand
    {
        get => _refreshCommand;
        set => SetAndRaise(RefreshCommandProperty, ref _refreshCommand, value);
    }

    public static readonly DirectProperty<MapViewControl, IRelayCommand?> RefreshCommandProperty =
        AvaloniaProperty.RegisterDirect<MapViewControl, IRelayCommand?>(
            nameof(RefreshCommand),
            o => o._refreshCommand,
            (o, v) => o.RefreshCommand = v);

    public IRelayCommand? ExportGeoLibreCommand
    {
        get => _exportGeoLibreCommand;
        set => SetAndRaise(ExportGeoLibreCommandProperty, ref _exportGeoLibreCommand, value);
    }

    public static readonly DirectProperty<MapViewControl, IRelayCommand?> ExportGeoLibreCommandProperty =
        AvaloniaProperty.RegisterDirect<MapViewControl, IRelayCommand?>(
            nameof(ExportGeoLibreCommand),
            o => o._exportGeoLibreCommand,
            (o, v) => o.ExportGeoLibreCommand = v);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == GeoJsonFilePathProperty)
        {
            _geoJsonFilePath = change.GetNewValue<string?>();
            UpdateUI();
        }
        else if (change.Property == GeoLibreBaseUrlProperty)
        {
            _geoLibreBaseUrl = change.GetNewValue<string?>();
        }
    }

    private void UpdateUI()
    {
        if (_statusText != null)
        {
            if (string.IsNullOrEmpty(_geoJsonFilePath) || !File.Exists(_geoJsonFilePath))
            {
                _statusText.Text = "No location data available. Run NLP analysis first.";
            }
            else
            {
                _statusText.Text = $"GeoJSON generated: {_geoJsonFilePath}";
            }
        }

        if (_geoJsonPathText != null)
        {
            _geoJsonPathText.Text = _geoJsonFilePath ?? "N/A";
        }

        if (_openInGeoLibreBtn != null)
        {
            _openInGeoLibreBtn.IsEnabled = !string.IsNullOrEmpty(_geoJsonFilePath) && File.Exists(_geoJsonFilePath);
        }

        if (_exportGeoLibreBtn != null)
        {
            _exportGeoLibreBtn.IsEnabled = !string.IsNullOrEmpty(_geoJsonFilePath) && File.Exists(_geoJsonFilePath);
        }
    }

    private void OnOpenInGeoLibre(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_geoJsonFilePath) || !File.Exists(_geoJsonFilePath))
            return;

        var baseUrl = _geoLibreBaseUrl ?? "https://web.geolibre.app";
        var fileUri = new Uri(_geoJsonFilePath).AbsoluteUri;
        var url = $"{baseUrl.TrimEnd('/')}?url={Uri.EscapeDataString(fileUri)}";

        // Open in default browser
        try
        {
#if WINDOWS
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
#elif MACOS
            Process.Start(new ProcessStartInfo
            {
                FileName = "open",
                Arguments = url,
                UseShellExecute = false
            });
#elif LINUX
            Process.Start(new ProcessStartInfo
            {
                FileName = "xdg-open",
                Arguments = url,
                UseShellExecute = false
            });
#else
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
#endif
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to open GeoLibre: {ex.Message}");
        }
    }

    private void OnRefreshGeoJson(object? sender, RoutedEventArgs e)
    {
        // Trigger a refresh - the parent ViewModel should handle this
        RefreshCommand?.Execute(null);
    }

    private void OnExportGeoLibre(object? sender, RoutedEventArgs e)
    {
        // Trigger the export command - the parent ViewModel should handle this
        ExportGeoLibreCommand?.Execute(null);
    }

}
