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
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using System;

namespace Caly.Core.Views;

public partial class MainWindow : Window
{
    public WindowNotificationManager? NotificationManager { get; set; }

    public MainWindow()
    {
        InitializeComponent();

        // macOS: keep the standard title bar with the traffic-light buttons above the
        // content instead of drawing our own chrome over it (HIG - never hide or
        // reposition the window controls).
        if (OperatingSystem.IsMacOS())
        {
            ExtendClientAreaToDecorationsHint = false;
        }

        // The title bar should identify the document being read (HIG - document windows
        // show the document name).
        AffectsTitle();
    }

    private void AffectsTitle()
    {
        if (DataContext is ViewModels.MainViewModel vm)
        {
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(vm.SelectedDocument))
                {
                    UpdateTitle(vm);
                }
            };

            UpdateTitle(vm);
        }
    }

    private void UpdateTitle(ViewModels.MainViewModel vm)
    {
        Title = string.IsNullOrEmpty(vm.SelectedDocument?.FileName)
            ? "TraceView"
            : vm.SelectedDocument!.FileName;
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        NotificationManager = new WindowNotificationManager(this)
        {
            // macOS notifications slide in under the menu bar (top right); on other
            // platforms the bottom-right corner is the conventional spot.
            Position = OperatingSystem.IsMacOS() ? NotificationPosition.TopRight : NotificationPosition.BottomRight,
#if DEBUG
            MaxItems = 50
#else
            MaxItems = 5
#endif
        };

        base.OnLoaded(e); // We need to OnLoad() after the NotificationManager is created
    }
}