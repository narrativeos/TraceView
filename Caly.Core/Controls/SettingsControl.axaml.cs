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
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Caly.Core.Services.Interfaces;
using Caly.Core.ViewModels;

namespace Caly.Core.Controls;

public class SettingsControl : TemplatedControl
{
    private Button? _browseProjectHomeButton;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _browseProjectHomeButton = e.NameScope.Find<Button>("PART_BrowseProjectHome");
        if (_browseProjectHomeButton != null)
        {
            _browseProjectHomeButton.Click += OnBrowseProjectHomeClicked;
        }
    }

    private async void OnBrowseProjectHomeClicked(object? sender, RoutedEventArgs e)
    {
        // Get the top-level window
        var window = TopLevel.GetTopLevel(this) as Window;
        if (window?.StorageProvider is null)
            return;

        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Project Home Directory",
        });

        if (folders.Count > 0)
        {
            var selectedPath = folders[0].Path.LocalPath;
            var settingsService = App.Current?.Services?.GetService(typeof(ISettingsService)) as ISettingsService;
            if (settingsService is not null)
            {
                var settings = settingsService.GetSettings();
                settings.ProjectHome = selectedPath;
                settingsService.Save();
            }
        }
    }
}