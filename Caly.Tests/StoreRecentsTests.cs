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
using System.IO;
using System.Linq;
using Caly.Core.Models;
using Caly.Core.Services;
using Caly.Core.Services.Store;
using Caly.Core.Utilities;
using System.Text.Json;
using Xunit;

namespace Caly.Tests;

/// <summary>
/// Tests for the 项目主页 近期项目 list (docs .design-flow/project-entry E01):
/// StoreIndex ordering by 最近打开, the cap, name resolution and stage badges.
/// </summary>
public class StoreRecentsTests : IDisposable
{
    private readonly string _root;

    public StoreRecentsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "traceview-recents-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup of the temp dir.
        }
    }

    private string CreateProject(
        string projectId,
        string pdfFileName,
        DateTime createdAt,
        DateTime? lastModified = null,
        bool pdfExists = true)
    {
        var projectPath = Path.Combine(_root, "projects", projectId[..2], projectId.Substring(2, 2), projectId);
        Directory.CreateDirectory(projectPath);
        var pdf = Path.Combine(_root, pdfFileName);
        if (pdfExists)
        {
            File.WriteAllText(pdf, "pdf");
        }

        var metadata = new ProjectMetadata
        {
            PdfPath = pdf,
            PdfFileName = pdfFileName,
            ProjectName = projectId,
            ProjectPath = projectPath,
            CreatedAt = createdAt,
            LastModified = lastModified,
            ProjectId = projectId,
        };
        File.WriteAllText(Path.Combine(projectPath, "project.json"),
            JsonSerializer.Serialize(metadata, SourceGenerationContext.Default.ProjectMetadata));
        return projectPath;
    }

    private static string Id(int n) => n.ToString("x32");

    [Fact]
    public void List_OrdersByLastModifiedFallingBackToCreatedAt_NewestFirst()
    {
        var old = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        CreateProject(Id(1), "a.pdf", old);
        CreateProject(Id(2), "b.pdf", old, lastModified: old.AddHours(3));
        CreateProject(Id(3), "c.pdf", old, lastModified: old.AddHours(1));

        var recents = StoreRecents.List(_root);

        Assert.Equal(["b", "c", "a"], recents.Select(r => r.Name));
    }

    [Fact]
    public void List_CapsAtLimit()
    {
        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 1; i <= 10; i++)
        {
            CreateProject(Id(i), $"p{i}.pdf", baseTime, lastModified: baseTime.AddMinutes(i));
        }

        var recents = StoreRecents.List(_root);

        Assert.Equal(StoreRecents.DefaultLimit, recents.Count);
        Assert.Equal("p10", recents[0].Name);
    }

    [Fact]
    public void List_FallsBackToDirName_WhenPdfFileNameMissing_AndBadgesStage()
    {
        var pdf = Path.Combine(_root, "gone.pdf"); // never created -> PdfMissing stage
        var projectPath = Path.Combine(_root, "projects", "00", "00", Id(0));
        Directory.CreateDirectory(projectPath);
        var metadata = new ProjectMetadata
        {
            PdfPath = pdf,
            PdfFileName = string.Empty,
            ProjectName = Id(0),
            ProjectPath = projectPath,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ProjectId = Id(0),
        };
        File.WriteAllText(Path.Combine(projectPath, "project.json"),
            JsonSerializer.Serialize(metadata, SourceGenerationContext.Default.ProjectMetadata));

        var recents = StoreRecents.List(_root);

        var only = Assert.Single(recents);
        Assert.Equal(Id(0), only.Name);
        Assert.Equal("PDF 缺失", only.StageLabel);
        Assert.True(only.HasStageBadge);
        Assert.False(only.IsDoneStage);
    }

    [Fact]
    public void List_EmptyOrMissingRoot_ReturnsEmpty()
    {
        Assert.Empty(StoreRecents.List(Path.Combine(_root, "nope")));
        Assert.Empty(StoreRecents.List(string.Empty));
    }

    [Fact]
    public void List_NonPositiveLimit_ReturnsEmpty()
    {
        CreateProject(Id(1), "a.pdf", DateTime.UtcNow);
        Assert.Empty(StoreRecents.List(_root, limit: 0));
    }
}

/// <summary>Boundary checks for the shared 相对时间 wording used by 项目主页 and 选择项目 dialogs.</summary>
public class TimeTextTests
{
    [Theory]
    [InlineData(-0.5, "刚刚")]
    [InlineData(-10.5, "10 分钟前")]
    [InlineData(-2.5 * 60, "2 小时前")]
    [InlineData(-3.5 * 24 * 60, "3 天前")]
    [InlineData(-10.0 * 24 * 60, "1 周前")]
    public void SinceUtc_FormatsBuckets(double minutesAgo, string expected)
    {
        Assert.Equal(expected, TimeText.SinceUtc(DateTime.UtcNow.AddMinutes(minutesAgo)));
    }

    [Fact]
    public void SinceUtc_OldDates_UsePlainDate()
    {
        var text = TimeText.SinceUtc(DateTime.UtcNow.AddDays(-100));
        Assert.Equal(10, text.Length);
        Assert.True(DateTime.TryParseExact(text, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out _));
    }
}
