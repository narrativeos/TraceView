// Copyright (c) 2025 BobLd
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to copy, or sell copies of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Caly.Core.Models;
using Caly.Core.Services;
using Caly.Core.Services.Interfaces;
using Caly.Core.Services.Store;
using Xunit;

namespace Caly.Tests;

/// <summary>
/// Tests for the Phase A store layer: StorePaths centralization and the
/// rebuildable StoreIndex (docs/traceview-storage-layout-spec.md §9 Phase A).
/// </summary>
public class StoreIndexTests : IDisposable
{
    private readonly string _root;

    public StoreIndexTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "traceview-store-tests-" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    public void DefaultRoot_EndsWithLegacyDirectoryName()
    {
        Assert.EndsWith(StorePaths.LegacyDirectoryName, StorePaths.DefaultRoot);
    }

    [Fact]
    public void ResolveRoot_FallsBackToDefaultRootWithoutDi()
    {
        // No Avalonia Application/DI in unit tests: must resolve to the default root,
        // not throw and not return an empty path.
        Assert.Equal(StorePaths.DefaultRoot, StorePaths.ResolveRoot());
    }

    [Fact]
    public void PathCompositions_MatchLegacyLayout()
    {
        var root = "/home/u/.TraceView";
        Assert.Equal(Path.Combine(root, "store-index.json"), StorePaths.IndexPath(root));
        Assert.Equal(Path.Combine(root, "doc1", "mineru"), StorePaths.LegacyMineruCacheDir(root, "doc1"));
        Assert.Equal(Path.Combine(root, "tmp", "no_project"), StorePaths.NoProjectDir(root));
        Assert.Equal(Path.Combine(root, "geocoding_cache"), StorePaths.GeoCacheDir(root));
    }

    [Fact]
    public void Build_RegistersProjectsAndSkipsReservedAndIncompleteDirs()
    {
        var a = CreateProjectDir("bookA", "/pdfs/a.pdf");
        var b = CreateProjectDir("bookA_2", "/pdfs/b.pdf");

        // Reserved directories and a directory without project.json must not register.
        Directory.CreateDirectory(Path.Combine(_root, StorePaths.TmpDirectoryName, StorePaths.NoProjectDirectoryName, "semantic"));
        Directory.CreateDirectory(Path.Combine(_root, StorePaths.GeoCacheDirectoryName));
        Directory.CreateDirectory(Path.Combine(_root, "orphan_without_metadata"));

        var index = StoreIndex.Build(_root);

        Assert.Equal(2, index.Projects.Count);
        Assert.Equal(new[] { "bookA", "bookA_2" }, new[] { index.Projects[0].DirName, index.Projects[1].DirName });
        Assert.Equal(a, index.Projects[0].ProjectPath);
        Assert.Equal("/pdfs/a.pdf", index.Projects[0].PdfPath);
        Assert.True(index.Projects[0].Legacy);
        Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), index.Projects[0].CreatedAt);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAtomically()
    {
        CreateProjectDir("bookA", "/pdfs/a.pdf");
        var built = StoreIndex.BuildAndSave(_root);

        var loaded = StoreIndex.Load(_root);

        Assert.NotNull(loaded);
        Assert.Equal(built.Version, loaded!.Version);
        Assert.Single(loaded.Projects);
        Assert.Equal("/pdfs/a.pdf", loaded.Projects[0].PdfPath);

        // Atomic save: no leftover temp files next to the index.
        Assert.Empty(Directory.GetFiles(_root, "store-index.json.*.tmp"));
    }

    [Fact]
    public void Register_UpsertsWithoutDuplicates()
    {
        var p1 = CreateProjectDir("bookA", "/pdfs/a.pdf");
        StoreIndex.BuildAndSave(_root);

        // Re-register the same project with an updated timestamp: no duplicate entry.
        var createdAt = new DateTime(2026, 5, 5, 0, 0, 0, DateTimeKind.Utc);
        StoreIndex.Register(_root, new StoreProjectEntry
        {
            DirName = "bookA",
            ProjectPath = p1,
            PdfPath = "/pdfs/a.pdf",
            PdfFileName = "a.pdf",
            CreatedAt = createdAt,
            Legacy = true
        });

        var index = StoreIndex.Load(_root)!;
        Assert.Single(index.Projects);
        Assert.Equal(createdAt, index.Projects[0].CreatedAt);
    }

    [Fact]
    public void FindByPdfPath_IsCaseInsensitiveAndSorted()
    {
        CreateProjectDir("bookA_2", "/PDFS/A.pdf");
        CreateProjectDir("bookA", "/pdfs/a.pdf");
        CreateProjectDir("bookB", "/pdfs/b.pdf");
        var index = StoreIndex.Build(_root);

        var matches = StoreIndex.FindByPdfPath(index, "/Pdfs/A.PDF");

        Assert.Equal(new[] { "bookA", "bookA_2" }, new[] { matches[0].DirName, matches[1].DirName });
    }

    [Fact]
    public void Rebuild_DropsStaleEntriesForDeletedProjects()
    {
        var p = CreateProjectDir("bookA", "/pdfs/a.pdf");
        StoreIndex.BuildAndSave(_root);
        Directory.Delete(p, recursive: true);

        var index = StoreIndex.BuildAndSave(_root);

        Assert.Empty(index.Projects);
    }

    [Fact]
    public void ProjectService_CreateProject_RegistersProjectInIndex()
    {
        var settings = new FakeSettingsService { Settings = new CalySettings { ProjectHome = _root } };
        var service = new ProjectService(settings);

        var projectPath = service.CreateProject("/pdfs/new.pdf", Path.Combine(_root, "new"));

        Assert.True(File.Exists(Path.Combine(projectPath, "project.json")));
        var index = StoreIndex.Load(_root);
        Assert.NotNull(index);
        Assert.Single(index!.Projects);
        Assert.Equal(projectPath, index.Projects[0].ProjectPath);
        Assert.Equal("/pdfs/new.pdf", index.Projects[0].PdfPath);
    }

    [Fact]
    public void ProjectService_BuildStoreIndex_RegistersPreexistingProjectsInPlace()
    {
        // Legacy projects already on disk get registered without being moved (Phase A).
        var p1 = CreateProjectDir("legacy1", "/pdfs/1.pdf");
        var p2 = CreateProjectDir("legacy2", "/pdfs/2.pdf");
        var settings = new FakeSettingsService { Settings = new CalySettings { ProjectHome = _root } };
        var service = new ProjectService(settings);

        var index = service.BuildStoreIndex();

        Assert.Equal(2, index.Projects.Count);
        Assert.All(index.Projects, entry => Assert.True(entry.Legacy));
        Assert.Contains(index.Projects, e => e.ProjectPath == p1);
        Assert.Contains(index.Projects, e => e.ProjectPath == p2);
        // Files stayed where they were.
        Assert.True(Directory.Exists(p1));
        Assert.True(Directory.Exists(p2));
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
