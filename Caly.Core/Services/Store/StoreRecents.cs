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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Caly.Core.Models;

namespace Caly.Core.Services.Store;

/// <summary>
/// 近期项目 list for the 项目主页: the store index sorted by most-recent activity, capped,
/// each row carrying its 队列看板 stage badge so the home reads like a mini board
/// (docs .design-flow/project-entry — E01). No services or DI: usable from tests and any
/// store root, same contract as <see cref="StoreIndex"/> and <see cref="QueueBoard"/>.
/// </summary>
public static class StoreRecents
{
    public const int DefaultLimit = 8;

    public static List<RecentProjectItem> List(string root, int limit = DefaultLimit)
    {
        var result = new List<RecentProjectItem>();
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root) || limit <= 0)
            return result;

        var index = StoreIndex.LoadOrBuild(root);

        // Stage badges come from the same pass the 队列看板 uses, keyed by project path.
        // Badges are decoration: a failed board pass leaves the list fully usable without them.
        var stageLabels = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (var item in QueueBoard.Build(root).Items)
            {
                stageLabels[item.ProjectPath] = QueueBoard.GetStageLabel(item.Stage);
            }
        }
        catch
        {
            // Best effort.
        }

        foreach (var entry in index.Projects
                     .OrderByDescending(p => p.LastModified ?? p.CreatedAt)
                     .Take(limit))
        {
            var name = !string.IsNullOrEmpty(entry.PdfFileName)
                ? Path.GetFileNameWithoutExtension(entry.PdfFileName)
                : entry.DirName;

            stageLabels.TryGetValue(entry.ProjectPath, out var stageLabel);

            result.Add(new RecentProjectItem
            {
                Name = name,
                ProjectPath = entry.ProjectPath,
                PdfPath = entry.PdfPath,
                LastOpenedAt = entry.LastModified ?? entry.CreatedAt,
                StageLabel = stageLabel ?? string.Empty,
            });
        }

        return result;
    }
}
