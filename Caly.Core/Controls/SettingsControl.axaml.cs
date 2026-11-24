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
using Avalonia.Controls.Notifications;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Caly.Core.Services;
using Caly.Core.Services.Interfaces;
using Caly.Core.ViewModels;

namespace Caly.Core.Controls;

public class SettingsControl : TemplatedControl
{
    private Button? _browseProjectHomeButton;
    private TextBox? _aiKeyInput;
    private Button? _saveAiKeyButton;
    private TextBox? _popoBaseUrlInput;
    private TextBox? _aiBaseUrlInput;
    private TextBox? _aiModelInput;
    private TextBox? _aiCredentialRefInput;
    private DispatcherTimer? _autoSaveTimer;

    /// <summary>Debounce before persisting agent/server settings typed straight into the settings singleton.</summary>
    private static readonly TimeSpan AutoSaveDelay = TimeSpan.FromMilliseconds(600);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _browseProjectHomeButton = e.NameScope.Find<Button>("PART_BrowseProjectHome");
        if (_browseProjectHomeButton != null)
        {
            _browseProjectHomeButton.Click += OnBrowseProjectHomeClicked;
        }

        _aiKeyInput = e.NameScope.Find<TextBox>("PART_AiKeyInput");
        _saveAiKeyButton = e.NameScope.Find<Button>("PART_SaveAiKeyButton");
        if (_saveAiKeyButton != null)
        {
            _saveAiKeyButton.Click += OnSaveAiKeyClicked;
        }

        // Agent and Popo endpoints/models autosave (debounced) so an agent run never
        // depends on the user remembering to hit a save button (spec C02).
        HookAutoSaveTextBox(e, "PART_PopoBaseUrlInput", ref _popoBaseUrlInput);
        HookAutoSaveTextBox(e, "PART_AiBaseUrlInput", ref _aiBaseUrlInput);
        HookAutoSaveTextBox(e, "PART_AiModelInput", ref _aiModelInput);
        HookAutoSaveTextBox(e, "PART_AiCredentialRefInput", ref _aiCredentialRefInput);
    }

    private void HookAutoSaveTextBox(TemplateAppliedEventArgs e, string name, ref TextBox? field)
    {
        if (field is not null)
        {
            field.TextChanged -= OnAutoSaveSettingTextChanged;
        }

        field = e.NameScope.Find<TextBox>(name);
        if (field is not null)
        {
            field.TextChanged += OnAutoSaveSettingTextChanged;
        }
    }

    private void OnAutoSaveSettingTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_autoSaveTimer is null)
        {
            _autoSaveTimer = new DispatcherTimer { Interval = AutoSaveDelay };
            _autoSaveTimer.Tick += (_, _) =>
            {
                _autoSaveTimer?.Stop();
                if (App.Current?.Services?.GetService(typeof(ISettingsService)) is ISettingsService settingsService)
                {
                    settingsService.Save();
                }
            };
        }

        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
    }

    private void OnSaveAiKeyClicked(object? sender, RoutedEventArgs e)
    {
        if (_aiKeyInput?.Text is not { Length: > 0 } key)
        {
            Notify("智能体密钥", "请先在左侧输入 API 密钥。", NotificationType.Warning);
            return;
        }

        var reference = (DataContext as MainViewModel)?.Settings.AiCredentialRef ?? string.Empty;
        if (!reference.StartsWith("keychain:", StringComparison.OrdinalIgnoreCase))
        {
            Notify("智能体密钥",
                "密钥引用需为 keychain:服务/账户 形式才能写入钥匙串（env: 引用只读）。",
                NotificationType.Warning);
            return;
        }

        if (App.Current?.Services?.GetService(typeof(ISecretStore)) is not ISecretStore secretStore ||
            !secretStore.TrySet(reference, key))
        {
            Notify("智能体密钥", "写入系统钥匙串失败，请检查密钥引用格式。", NotificationType.Error);
            return;
        }

        // Wipe the field once stored, and persist the settings file so the reference
        // survives the next start.
        _aiKeyInput.Text = null;

        if (App.Current?.Services?.GetService(typeof(ISettingsService)) is ISettingsService settingsService)
        {
            settingsService.Save();
        }

        Notify("智能体密钥", "密钥已存入系统钥匙串。", NotificationType.Success);
    }

    private static void Notify(string title, string message, NotificationType type)
    {
        if (App.Current?.Services?.GetService(typeof(IDialogService)) is IDialogService dialogService)
        {
            dialogService.ShowNotification(title, message, type);
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
            Title = "选择项目主目录",
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