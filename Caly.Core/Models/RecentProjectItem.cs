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
using Caly.Core.Utilities;

namespace Caly.Core.Models;

/// <summary>
/// One 近期打开的项目 row on the 项目主页: a stored project plus the queue-board stage
/// badge and the relative 最近打开 text shown beside its name.
/// </summary>
public sealed class RecentProjectItem
{
    /// <summary>PDF file name without extension, falling back to the project directory name.</summary>
    public required string Name { get; init; }

    public required string ProjectPath { get; init; }

    public required string PdfPath { get; init; }

    /// <summary>UTC timestamp of the last open / modification.</summary>
    public required DateTime LastOpenedAt { get; init; }

    /// <summary>队列看板 stage label (e.g. 完成 / 解析中); empty when the board pass was unavailable.</summary>
    public string StageLabel { get; init; } = string.Empty;

    public bool IsDoneStage => StageLabel == "完成";

    public bool HasStageBadge => !string.IsNullOrEmpty(StageLabel);

    public string LastOpenedText => TimeText.SinceUtc(LastOpenedAt);
}
