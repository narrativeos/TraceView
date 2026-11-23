// Cache keys are content-derived (sha256(pdf)[0:32]) so renames/moves of the same
// document keep reusing the cache (spec §11 acceptance criteria).

using System;
using System.IO;
using Caly.Core.Services;
using Caly.Core.Services.Store;
using Xunit;

namespace Caly.Tests;

public sealed class MinerUServiceCacheKeyTests : IDisposable
{
    private readonly string _root;

    public MinerUServiceCacheKeyTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "caly-mineru-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private string CreatePdf(string name, string content)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static MinerUService CreateService(string cacheDirectory) =>
        new("http://127.0.0.1:1", cacheDirectory);

    [Fact]
    public void CacheKey_IsContentHash_SameContentDifferentName_SameKey()
    {
        var a = CreatePdf("a.pdf", "same bytes");
        var b = CreatePdf("renamed-later.pdf", "same bytes");

        Assert.Equal(StoreMigrator.ComputeProjectIdForPdf(a), StoreMigrator.ComputeProjectIdForPdf(b));
    }

    [Fact]
    public void CacheKey_SameNameDifferentContent_DifferentKeys()
    {
        var dirA = Path.Combine(_root, "a");
        var dirB = Path.Combine(_root, "b");
        Directory.CreateDirectory(dirA);
        Directory.CreateDirectory(dirB);
        var a = Path.Combine(dirA, "same-name.pdf");
        var b = Path.Combine(dirB, "same-name.pdf");
        File.WriteAllText(a, "v1");
        File.WriteAllText(b, "v2");

        Assert.NotEqual(StoreMigrator.ComputeProjectIdForPdf(a), StoreMigrator.ComputeProjectIdForPdf(b));
    }

    [Fact]
    public void TaskIdFile_UsesContentHashKey_RoundTrips()
    {
        var pdf = CreatePdf("doc.pdf", "pdf bytes");
        var project = Path.Combine(_root, "project");
        var service = CreateService(Path.Combine(_root, "cache"));

        service.SaveTaskId(pdf, "task-42", project);

        var key = StoreMigrator.ComputeProjectIdForPdf(pdf);
        var taskFile = Path.Combine(project, "mineru", $"{key}_task_id.json");
        Assert.True(File.Exists(taskFile));
        Assert.Equal("task-42", service.LoadTaskId(pdf, project));

        service.ClearTaskId(pdf, project);
        Assert.False(File.Exists(taskFile));
        Assert.Null(service.LoadTaskId(pdf, project));
    }

    [Fact]
    public void ClearOldExtractedDirs_RemovesLegacyBasenameDirs_KeepsHashDir()
    {
        var pdf = CreatePdf("doc.pdf", "pdf bytes");
        var cache = Path.Combine(_root, "cache");
        Directory.CreateDirectory(cache);

        var key = StoreMigrator.ComputeProjectIdForPdf(pdf);
        var legacyDir = Path.Combine(cache, Path.GetFileNameWithoutExtension(pdf));
        var hashDir = Path.Combine(cache, key);
        Directory.CreateDirectory(legacyDir);
        Directory.CreateDirectory(hashDir);

        CreateService(cache).ClearOldExtractedDirs(pdf);

        Assert.False(Directory.Exists(legacyDir));
        Assert.True(Directory.Exists(hashDir));
    }
}
