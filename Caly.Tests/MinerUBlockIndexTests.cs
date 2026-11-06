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
using Caly.Core.ViewModels;
using System.ComponentModel;

namespace Caly.Tests;

/// <summary>
/// Tests for the automatic 1-based block index shown in the MinerU blocks column
/// (<see cref="MinerUBlockViewModel.AssignPageIndexes"/>). Numbering must restart at 1
/// on every page so it mirrors the index badges drawn on the PDF block overlay.
/// </summary>
public class MinerUBlockIndexTests
{
    private static MinerUBlockViewModel Block(int page, string blockId = "")
        => new(new MinerUMiddlePageBlock { Page = page, BlockId = blockId, Type = "text" });

    [Fact]
    public void AssignPageIndexes_NumberIsOneBasedAndRestartsOnEveryPage()
    {
        var blocks = new List<MinerUBlockViewModel>
        {
            Block(1), Block(1), Block(1),
            Block(2), Block(2),
            Block(3)
        };

        MinerUBlockViewModel.AssignPageIndexes(blocks);

        Assert.Equal(new[] { 1, 2, 3, 1, 2, 1 }, blocks.Select(b => b.BlockIndex));
    }

    [Fact]
    public void AssignPageIndexes_NumberingFollowsTheListOrderNotTheMinerUId()
    {
        // MinerU's internal ids are a global counter and must not leak into the display index.
        var blocks = new List<MinerUBlockViewModel>
        {
            new(new MinerUMiddlePageBlock { Page = 1, Id = 42 }),
            new(new MinerUMiddlePageBlock { Page = 1, Id = 7 })
        };

        MinerUBlockViewModel.AssignPageIndexes(blocks);

        Assert.Equal(new[] { 1, 2 }, blocks.Select(b => b.BlockIndex));
    }

    [Fact]
    public void AssignPageIndexes_InterleavedPages_KeepSeparateCounters()
    {
        var blocks = new List<MinerUBlockViewModel>
        {
            Block(1), Block(2), Block(1), Block(2), Block(2)
        };

        MinerUBlockViewModel.AssignPageIndexes(blocks);

        Assert.Equal(new[] { 1, 1, 2, 2, 3 }, blocks.Select(b => b.BlockIndex));
    }

    [Fact]
    public void AssignPageIndexes_EmptyCollection_DoesNotThrow()
    {
        MinerUBlockViewModel.AssignPageIndexes(new List<MinerUBlockViewModel>());
    }

    [Fact]
    public void IndexLabel_ReflectsTheAssignedIndex()
    {
        var block = Block(1);

        MinerUBlockViewModel.AssignPageIndexes(new List<MinerUBlockViewModel> { block });

        Assert.Equal("#1", block.IndexLabel);
    }

    [Fact]
    public void IndexLabel_RaisesPropertyChangedWhenIndexChanges()
    {
        var block = Block(1);
        var raised = new List<string?>();
        ((INotifyPropertyChanged)block).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        block.BlockIndex = 5;

        Assert.Contains(nameof(MinerUBlockViewModel.IndexLabel), raised);
        Assert.Equal("#5", block.IndexLabel);
    }
}
