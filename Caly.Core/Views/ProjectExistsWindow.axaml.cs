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

using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

namespace Caly.Core.Views;

/// <summary>
/// Dialog window asking user whether to create a new project or open an existing one.
/// Returns true for "Create New Project", false for "Open Existing Project".
/// </summary>
public partial class ProjectExistsWindow : Window
{
    private readonly string _projectName;
    private TextBlock? _messageText;

    public ProjectExistsWindow(string projectName)
    {
        _projectName = projectName;
        InitializeComponent();
    }

    [MemberNotNull(nameof(_messageText))]
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _messageText = this.Find<TextBlock>("PART_MessageText")!;
        if (_messageText is not null)
        {
            _messageText.Text = $"项目 '{_projectName}' 已存在，请选择：";
        }
    }

    private void CreateNewButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void OpenExistingButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}