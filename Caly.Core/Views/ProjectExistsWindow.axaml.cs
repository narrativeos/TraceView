// Copyright (c) 2025 BobLd
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated associated documentation files (the "Software"), to deal
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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using Caly.Core.Services;
using Caly.Core.Utilities;
using Microsoft.Extensions.DependencyInjection;

namespace Caly.Core.Views;

/// <summary>
/// Dialog window that lists existing projects for a given PDF and allows the user
/// to select one or create a new project.
/// Returns the selected project path, or null if the user chose "Create New Project".
/// </summary>
public partial class ProjectExistsWindow : Window
{
    /// <summary>
    /// Returned by <see cref="Window.Close"/> when the user cancels (Esc) instead of
    /// selecting an existing project or creating a new one. A project path cannot
    /// contain a NUL character, so this cannot collide with a real selection.
    /// </summary>
    public const string CancelOpen = "\0cancel";

    private readonly string _pdfPath;
    private readonly List<string> _existingProjects;
    private ListBox? _projectList;
    private bool _closing;

    /// <summary>
    /// Default constructor required by Avalonia XAML loader.
    /// </summary>
    public ProjectExistsWindow()
    {
        _pdfPath = string.Empty;
        _existingProjects = new List<string>();
        InitializeComponent();
    }

    public ProjectExistsWindow(string pdfPath, List<string> existingProjects)
    {
        _pdfPath = pdfPath;
        _existingProjects = existingProjects;
        InitializeComponent();
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        // 文案随数量说明情况（列表为空时根本不会弹这个窗，调用方已直接新建并打开）。
        if (this.Find<TextBlock>("PART_HeaderText") is { } headerText)
        {
            headerText.Text = $"这个 PDF 已有 {_existingProjects.Count} 个项目";
        }

        _projectList = this.Find<ListBox>("PART_ProjectList");
        if (_projectList is not null)
        {
            var projectService = App.Current?.Services?.GetService<ProjectService>();
            var items = new List<ProjectListItem>();
            foreach (var path in _existingProjects)
            {
                var name = Path.GetFileName(path);
                var metadata = projectService?.LoadProjectMetadata(path);
                var lastOpened = metadata?.LastModified ?? metadata?.CreatedAt;
                items.Add(new ProjectListItem
                {
                    Name = name,
                    Path = path,
                    TimeText = lastOpened is { } timestamp ? TimeText.SinceUtc(timestamp) : string.Empty,
                });
            }
            _projectList.ItemsSource = items;
        }
    }

    /// <summary>
    /// When user clicks a project item, immediately close with that project's path.
    /// </summary>
    private void ProjectList_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_projectList?.SelectedItem is ProjectListItem item)
        {
            CloseWithResult(item.Path);
        }
    }

    /// <summary>
    /// Closes on the first pointer press over a row. <see cref="SelectionChanged"/>
    /// does not fire when clicking the already-selected row, so this guarantees every
    /// click on a project closes the dialog.
    /// </summary>
    private void ProjectList_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Visual source &&
            source.FindAncestorOfType<ListBoxItem>() is { DataContext: ProjectListItem item })
        {
            CloseWithResult(item.Path);
        }
    }

    private void CreateNewButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        CloseWithResult(null);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseWithResult(CancelOpen);
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private void CloseWithResult(string? result)
    {
        // PointerPressed and the resulting selection change can both reach a close
        // path within one interaction; only the first one counts.
        if (_closing)
        {
            return;
        }

        _closing = true;
        Close(result);
    }
}

/// <summary>
/// Simple model for displaying a project in the ListBox.
/// </summary>
public class ProjectListItem
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;

    /// <summary>Relative 最近打开 text (e.g. 3 小时前); empty when metadata is unreadable.</summary>
    public string TimeText { get; set; } = string.Empty;

    public bool HasTime => !string.IsNullOrEmpty(TimeText);

    public override string ToString() => Name;
}