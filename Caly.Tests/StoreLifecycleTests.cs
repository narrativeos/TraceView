// Tests for the §8 task staging protocol, the startup tmp/ sweep, and the §7 store doctor
// (docs/traceview-storage-layout-spec.md §7/§8/§11).

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Caly.Core.Models;
using Caly.Core.Services;
using Caly.Core.Services.Interfaces;
using Caly.Core.Services.Store;
using Xunit;

namespace Caly.Tests;

public class StoreLifecycleTests : IDisposable
{
    private readonly string _root;

    public StoreLifecycleTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "traceview-lifecycle-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best effort */ }
    }

    private string CreateProjectDir(string dirName, string pdfPath)
    {
        var projectPath = Path.Combine(_root, dirName);
        Directory.CreateDirectory(projectPath);
        var metadata = new ProjectMetadata
        {
            PdfPath = pdfPath,
            PdfFileName = Path.GetFileName(pdfPath),
            ProjectName = dirName,
            ProjectPath = projectPath,
            CreatedAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc)
        };
        File.WriteAllText(Path.Combine(projectPath, "project.json"),
            JsonSerializer.Serialize(metadata, SourceGenerationContext.Default.ProjectMetadata));
        return projectPath;
    }

    private void WriteShardedManifest() =>
        StoreManifest.Save(_root, new StoreManifest
        {
            LayoutVersion = StoreManifest.LayoutVersionSharded,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            NewProjectLayout = "sharded"
        });

    private static void AgeUp(string dir, TimeSpan age) =>
        Directory.SetLastWriteTimeUtc(dir, DateTime.UtcNow - age);

    // ── StoreTask staging protocol (§4 rule 3, §8) ─────────────────────────────

    [Fact]
    public void BeginTask_CreatesStagingDirUnderTmp_EndTaskRemovesIt()
    {
        var taskId = Guid.NewGuid().ToString("N");
        var dir = StoreTask.BeginTask(_root, taskId);

        Assert.Equal(Path.Combine(_root, "tmp", taskId), dir);
        Assert.True(Directory.Exists(dir));

        File.WriteAllText(Path.Combine(dir, "middle.json"), "{}");
        StoreTask.EndTask(_root, taskId);

        Assert.False(Directory.Exists(dir));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../evil")]
    [InlineData("a/b")]
    [InlineData(".hidden")]
    [InlineData("trailing.")]
    public void TaskId_RejectsPathTraversalsAndIllegalChars(string taskId) =>
        Assert.False(StoreTask.IsValidTaskId(taskId));

    [Theory]
    [InlineData("0f4c1a2b3d4e5f60718293a4b5c6d7e8")]
    [InlineData("task-1_2026.rev1")]
    public void TaskId_AcceptsHexAndFixedAscii(string taskId) =>
        Assert.True(StoreTask.IsValidTaskId(taskId));

    [Fact]
    public void CleanupStaleTasks_DeletesOldRemovesOnlyStale_AndSparesNoProject()
    {
        var stale = StoreTask.BeginTask(_root, "stale-task");
        var fresh = StoreTask.BeginTask(_root, "fresh-task");
        var noProject = Path.Combine(_root, "tmp", "no_project", "semantic");
        Directory.CreateDirectory(noProject);

        AgeUp(stale, TimeSpan.FromHours(30));
        AgeUp(noProject, TimeSpan.FromDays(90)); // no_project output must survive regardless of age

        var removed = StoreTask.CleanupStaleTasks(_root);

        Assert.Equal(new[] { "stale-task" }, removed);
        Assert.False(Directory.Exists(stale));
        Assert.True(Directory.Exists(fresh));
        Assert.True(Directory.Exists(noProject));
    }

    [Fact]
    public void ProjectServiceStartup_SweepsStaleTmpResidue()
    {
        // §11: kill mid-task leaves tmp/ residue; a restart sweeps it (constructor hook).
        var stale = StoreTask.BeginTask(_root, "crashed-task");
        AgeUp(stale, TimeSpan.FromHours(25));
        WriteShardedManifest();

        var service = new ProjectService(new FakeSettingsService
        {
            Settings = new CalySettings { ProjectHome = _root }
        });

        _ = service; // construction performs the sweep
        Assert.False(Directory.Exists(stale));
    }

    // ── StoreDoctor (§7) ───────────────────────────────────────────────────────

    [Fact]
    public void Doctor_CleanStore_IsConsistent()
    {
        CreateProjectDir("bookA", "/pdfs/a.pdf");
        StoreIndex.BuildAndSave(_root);
        WriteShardedManifest();

        var report = StoreDoctor.Scan(_root);

        Assert.True(report.Consistent);
        Assert.Equal(1, report.IndexedProjectCount);
        Assert.Equal(1, report.DiskProjectCount);
        Assert.Empty(report.BrokenProjects);
        Assert.Empty(report.OrphanProjects);
    }

    [Fact]
    public void Doctor_IndexedProjectMissingOnDisk_ReportsBroken()
    {
        var gone = CreateProjectDir("bookA", "/pdfs/a.pdf");
        StoreIndex.BuildAndSave(_root);
        WriteShardedManifest();
        Directory.Delete(gone, recursive: true);

        var report = StoreDoctor.Scan(_root);

        Assert.False(report.Consistent);
        Assert.Contains(report.BrokenProjects, p => p.Contains("bookA"));
        Assert.Empty(report.OrphanProjects);
    }

    [Fact]
    public void Doctor_DiskProjectMissingFromIndex_ReportsOrphan()
    {
        CreateProjectDir("bookA", "/pdfs/a.pdf");
        StoreIndex.BuildAndSave(_root);
        WriteShardedManifest();
        CreateProjectDir("bookB", "/pdfs/b.pdf"); // created after the persisted index

        var report = StoreDoctor.Scan(_root);

        Assert.False(report.Consistent);
        Assert.Equal(1, report.IndexedProjectCount);
        Assert.Equal(2, report.DiskProjectCount);
        Assert.Contains(report.OrphanProjects, p => p.Contains("bookB"));
    }

    [Fact]
    public void Doctor_ReportsTmpResidueAndGeoCache()
    {
        WriteShardedManifest();
        StoreIndex.BuildAndSave(_root);

        var stale = StoreTask.BeginTask(_root, "zombie-task");
        AgeUp(stale, TimeSpan.FromHours(26));
        var geoDir = StorePaths.GeoCacheDir(_root);
        Directory.CreateDirectory(geoDir);
        File.WriteAllText(Path.Combine(geoDir, "x.json"), "{}");

        var report = StoreDoctor.Scan(_root);

        Assert.Contains("zombie-task", report.TmpEntries);
        Assert.Contains("zombie-task", report.StaleTmpEntries);
        Assert.True(report.GeoCachePresent);
        Assert.Equal(1, report.GeoCacheFileCount);
    }

    [Fact]
    public void Gc_ExpiredEntries_AreEvicted_FreshEntriesSurvive()
    {
        var category = Path.Combine(StorePaths.StoreCacheDir(_root), "mineru");
        var expired = CreateCacheEntry(category, "expired", 64);
        var fresh = CreateCacheEntry(category, "fresh", 64);
        Directory.SetLastWriteTimeUtc(expired, DateTime.UtcNow.AddDays(-91));

        StoreGc.CleanupCache(_root);

        Assert.False(Directory.Exists(expired));
        Assert.True(Directory.Exists(fresh));
    }

    [Fact]
    public void Gc_OverBudget_EvictsOldestFirst()
    {
        var category = Path.Combine(StorePaths.StoreCacheDir(_root), "mineru");
        var oldEntry = CreateCacheEntry(category, "old", 600);
        var newEntry = CreateCacheEntry(category, "new", 600);
        Directory.SetLastWriteTimeUtc(oldEntry, DateTime.UtcNow.AddDays(-10));
        Directory.SetLastWriteTimeUtc(newEntry, DateTime.UtcNow.AddDays(-1));

        StoreGc.CleanupCache(_root, maxBytes: 1000);

        Assert.False(Directory.Exists(oldEntry));
        Assert.True(Directory.Exists(newEntry));
    }

    private static string CreateCacheEntry(string category, string name, int fileSize)
    {
        var entry = Path.Combine(category, name);
        Directory.CreateDirectory(entry);
        File.WriteAllBytes(Path.Combine(entry, "data.bin"), new byte[fileSize]);
        return entry;
    }

    [Fact]
    public void Doctor_MissingManifest_IsNotConsistent()
    {
        CreateProjectDir("bookA", "/pdfs/a.pdf");
        StoreIndex.BuildAndSave(_root);
        // no store.json: pre-Phase-B store shape.

        var report = StoreDoctor.Scan(_root);

        Assert.Equal(0, report.LayoutVersion);
        Assert.False(report.Consistent);
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public CalySettings Settings { get; set; } = new();

        public void SetProperty(CalySettings.CalySettingsProperty property, object value)
        {
        }

        public CalySettings GetSettings() => Settings;

        public ValueTask<CalySettings> GetSettingsAsync() => ValueTask.FromResult(Settings);

        public void Load()
        {
        }

        public Task LoadAsync() => Task.CompletedTask;

        public void Save()
        {
        }

        public Task SaveAsync() => Task.CompletedTask;
    }
}
