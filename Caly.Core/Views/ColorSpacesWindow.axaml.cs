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

using Avalonia.Controls;

namespace Caly.Core.Views;

/// <summary>
/// A simple, read-only window that displays the identified color spaces of a PDF page,
/// including whether each is rendered faithfully or only approximately (e.g. ICCBased).
/// </summary>
public partial class ColorSpacesWindow : Window
{
    public string Header { get; }

    public string Report { get; }

    /// <summary>
    /// Default constructor required by the Avalonia XAML loader.
    /// </summary>
    public ColorSpacesWindow()
    {
        Header = string.Empty;
        Report = string.Empty;
        InitializeComponent();
    }

    public ColorSpacesWindow(string header, string report)
    {
        Header = header;
        Report = report;
        Title = header;
        DataContext = this;
        InitializeComponent();
    }
}
