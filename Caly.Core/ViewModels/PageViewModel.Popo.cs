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

using Caly.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;

namespace Caly.Core.ViewModels;

public sealed partial class PageViewModel
{
    [ObservableProperty]
    private IReadOnlyList<MinerUBlock>? _minerUBlocks;

    /// <summary>
    /// Preproc blocks (raw detection blocks) for this page.
    /// These are shown on the PDF overlay and connected to para/discarded blocks via connection lines.
    /// </summary>
    [ObservableProperty]
    private IReadOnlyList<MinerUBlock>? _preprocBlocks;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MinerUBlocks))]
    private bool _hasMinerUBlocks;

    partial void OnMinerUBlocksChanged(IReadOnlyList<MinerUBlock>? value)
    {
        HasMinerUBlocks = value is not null && value.Count > 0;
    }
}
