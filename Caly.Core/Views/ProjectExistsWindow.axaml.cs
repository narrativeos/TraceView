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
using System.IO;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Caly.Core.Views;

/// <summary>
/// Dialog window that lists existing projects for a given PDF and allows the user
/// to select one or create a new project.
/// Returns the selected project path, or null if the user chose "Create New Project".
/// </summary>
public partial class ProjectExistsWindow : Window
{
    private readonly string _pdfPath;
    private readonly List<string> _existingProjects;
    private ListBox? _projectList;

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
        _projectList = this.Find<ListBox>("PART_ProjectList");
        if (_projectList is not null)
        {
            var items = new List<ProjectListItem>();
            foreach (var path in _existingProjects)
            {
                var name = Path.GetFileName(path);
                items.Add(new ProjectListItem { Name = name, Path = path });
            }
            _projectList.ItemsSource = items;
            // Select the first item by default
            if (items.Count > 0)
                _projectList.SelectedIndex = 0;
        }
    }

    private void OpenProjectButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_projectList?.SelectedItem is ProjectListItem item)
        {
            Close(item.Path);
        }
        else
        {
            // No valid selection, default to first project
            if (_existingProjects.Count > 0)
                Close(_existingProjects[0]);
            else
                Close(null);
        }
    }

    private void CreateNewButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(null);
    }
}

/// <summary>
/// Simple model for displaying a project in the ListBox.
/// </summary>
public class ProjectListItem
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;

    public override string ToString() => Name;
}