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
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System;
using Caly.Core.ViewModels;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Caly.Core.Controls;

/// <summary>
/// The built-in agent conversation thread shown in its own right-hand column:
/// messages, inline approvals and the composer.
/// </summary>
public partial class AgentConversationControl : UserControl
{
    private AnalysisViewModel? _subscribedViewModel;
    private TextBox? _composer;
    private EventHandler<KeyEventArgs>? _composerKeyDownHandler;

    public AgentConversationControl()
    {
        InitializeComponent();
        _composerKeyDownHandler = OnAgentComposerKeyDown;

        // The XAML is this UserControl's content, not a template: resolve the
        // composer directly (FindControl), never through a template NameScope.
        _composer = this.FindControl<TextBox>("PART_AgentComposer");
        if (_composer is not null)
        {
            // TextBox can mark Enter handled before an ordinary instance handler runs.
            // Subscribe in the tunneling phase and with handledEventsToo in the bubble
            // phase as a fallback. A second invocation is a no-op: sending clears first.
            _composer.AddHandler(KeyDownEvent, _composerKeyDownHandler, RoutingStrategies.Tunnel, handledEventsToo: true);
            _composer.AddHandler(KeyDownEvent, _composerKeyDownHandler, RoutingStrategies.Bubble, handledEventsToo: true);
        }

        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.AgentMessages.CollectionChanged -= OnAgentMessagesChanged;
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedViewModel = null;
        }

        if (DataContext is not AnalysisViewModel vm)
        {
            return;
        }

        _subscribedViewModel = vm;
        vm.AgentMessages.CollectionChanged += OnAgentMessagesChanged;
        vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnAgentMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var scroll = this.FindControl<ScrollViewer>("PART_AgentScroll");
        if (scroll is not null)
        {
            // Scroll after the new message has been measured/arranged.
            Dispatcher.UIThread.Post(scroll.ScrollToEnd, DispatcherPriority.Loaded);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AnalysisViewModel.AgentComposerFocusToken))
        {
            return;
        }

        var composer = this.FindControl<TextBox>("PART_AgentComposer");
        if (composer is null)
        {
            return;
        }

        // The panel may have just become visible; focus once layout has caught up.
        Dispatcher.UIThread.Post(
            () =>
            {
                composer.Focus();
                composer.SelectAll();
            },
            DispatcherPriority.Input);
    }

    public void OnAgentComposerKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            return;
        }

        if (sender is not TextBox composer || string.IsNullOrWhiteSpace(composer.Text))
        {
            return;
        }

        // Resolve the VM from the composer's own inherited DataContext first; the
        // subscription token can lag when the panel appears after the document loads.
        var vm = composer.DataContext as AnalysisViewModel ?? _subscribedViewModel;
        if (vm is not null && vm.SendAgentPromptCommand.CanExecute(null))
        {
            vm.SendAgentPromptCommand.Execute(null);
            e.Handled = true;
        }
    }
}
