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
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Caly.Core.Models;
using Caly.Core.Services;
using Caly.Core.Services.Interfaces;
using Caly.Core.Services.Store;
using Xunit;

namespace Caly.Tests;

/// <summary>
/// Tests for the Phase B sharded-layout migration (docs/traceview-storage-layout-spec.md §9 Phase B).
/// </summary>
public class StoreMigratorTests : IDisposable
{
    private readonly string _root;
    private readonly string _pdfs;

    public StoreMigratorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "traceview-migrate-tests-" + Guid.NewGuid().ToString("N"));
        _pdfs = Path.Combine(_root, "_pdfs");
        Directory.CreateDirectory(_pdfs);
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

    /// <summary>Creates a flat legacy project dir plus its PDF (unless missingPdf), returning the project directory path.</summary>
    private string CreateLegacyProject(string dirName, string pdfName, byte[]? content = null, bool missingPdf = false)
    {
        var pdfPath = Path.Combine(_pdfs, pdfName);
        if (!missingPdf)
        {
            File.WriteAllBytes(pdfPath, content ?? new byte[] { 1, 2, 3 });
        }

        var projectPath = Path.Combine(_root, dirName);
        Directory.CreateDirectory(projectPath);
        var metadata = new ProjectMetadata
        {
            PdfPath = pdfPath,
            PdfFileName = Path.GetFileName(pdfPath),
            ProjectName = dirName,
            ProjectPath = projectPath,
            CreatedAt = DateTime.UtcNow
        };
        File.WriteAllText(Path.Combine(projectPath, "project.json"),
            JsonSerializer.Serialize(metadata, SourceGenerationContext.Default.ProjectMetadata));
        return projectPath;
    }

    private static string ReadProjectId(string projectPath)
    {
        var json = File.ReadAllText(Path.Combine(projectPath, "project.json"));
        var metadata = JsonSerializer.Deserialize(json, SourceGenerationContext.Default.ProjectMetadata);
        return metadata!.ProjectId!;
    }

    [Fact]
    public void Migrate_MovesProjectsToShardedLayout_AndRecordsEverything()
    {
        CreateLegacyProject("bookA", "a.pdf", new byte[] { 9, 9, 9 });
        CreateLegacyProject("bookB", "b.pdf", new byte[] { 7, 7, 7 });

        var result = StoreMigrator.Migrate(_root);

        Assert.Equal(2, result.Moves.Count);
        Assert.False(result.DryRun);
        foreach (var move in result.Moves)
        {
            Assert.False(Directory.Exists(move.From));
            Assert.True(Directory.Exists(move.To));
            Assert.Equal(32, move.ProjectId.Length);
            Assert.Equal(StorePaths.ShardedProjectDir(_root, move.ProjectId), move.To);

            // project.json now carries the stable id and the corrected path.
            Assert.Equal(move.ProjectId, ReadProjectId(move.To));

            // Index records both the new location and the legacy origin for rollback.
            var entry = StoreIndex.Load(_root)!.Projects.Single(p => p.ProjectPath == move.To);
            Assert.False(entry.Legacy);
            Assert.Equal(move.From, entry.OldPath);
            Assert.Equal(move.ProjectId, entry.ProjectId);
        }

        // Manifest marks the store as sharded, keeping the creation-layout note for Phase C.
        var manifest = StoreManifest.Load(_root);
        Assert.NotNull(manifest);
        Assert.Equal(StoreManifest.LayoutVersionSharded, manifest!.LayoutVersion);
        Assert.NotNull(manifest.MigratedAt);
    }

    [Fact]
    public void Migrate_SamePdfInTwoProjects_KeepsThemDistinct()
    {
        // The {name}/{name}_2 duplicate-project pattern: same PDF content must NOT collide.
        CreateLegacyProject("ejij", "ejij.pdf", new byte[] { 4, 4, 4 });
        CreateLegacyProject("ejij_2", "ejij.pdf", new byte[] { 4, 4, 4 });

        var result = StoreMigrator.Migrate(_root);

        Assert.Equal(2, result.Moves.Count);
        Assert.NotEqual(result.Moves[0].ProjectId, result.Moves[1].ProjectId);
        Assert.All(result.Moves, m => Assert.True(Directory.Exists(m.To)));
    }

    [Fact]
    public void Migrate_MissingPdf_FallsBackToDeterministicSeedId()
    {
        CreateLegacyProject("ghost", "gone.pdf", missingPdf: true);

        var result = StoreMigrator.Migrate(_root);

        Assert.Single(result.Moves);
        var id = result.Moves[0].ProjectId;
        Assert.Equal(32, id.Length);
        Assert.Matches("^[0-9a-f]{32}$", id);
        // Same store rebuilt from scratch yields the same id (seed derives from the recorded pdf path).
        var recomputed = StoreIndex.Load(_root)!.Projects.Single();
        Assert.Equal(id, StoreMigrator.ComputeProjectId(recomputed));
    }

    [Fact]
    public void Migrate_IsIdempotent()
    {
        CreateLegacyProject("bookA", "a.pdf", new byte[] { 1 });

        StoreMigrator.Migrate(_root);
        var second = StoreMigrator.Migrate(_root);

        Assert.True(second.AlreadyMigrated);
        Assert.Empty(second.Moves);
    }

    [Fact]
    public void Migrate_DryRun_PlansWithoutTouchingDisk()
    {
        var projectPath = CreateLegacyProject("bookA", "a.pdf", new byte[] { 2 });
        Assert.Equal(Path.Combine(_root, "bookA"), projectPath);

        var plan = StoreMigrator.Migrate(_root, dryRun: true);

        Assert.Single(plan.Moves);
        Assert.True(plan.DryRun);
        Assert.True(Directory.Exists(projectPath));
        Assert.False(File.Exists(StorePaths.ManifestPath(_root)));
    }

    [Fact]
    public void Rollback_RestoresFlatLayoutAndRemovesManifest()
    {
        var p1 = CreateLegacyProject("bookA", "a.pdf", new byte[] { 3 });
        var p2 = CreateLegacyProject("bookB", "b.pdf", new byte[] { 5 });

        StoreMigrator.Migrate(_root);
        var rollback = StoreMigrator.Rollback(_root);

        Assert.Equal(2, rollback.Moves.Count);
        Assert.True(Directory.Exists(p1));
        Assert.True(Directory.Exists(p2));
        Assert.False(Directory.Exists(StorePaths.ProjectsDir(_root)));
        Assert.False(File.Exists(StorePaths.ManifestPath(_root)));

        var entry = StoreIndex.Load(_root)!.Projects.Single(e => e.ProjectPath == p1);
        Assert.True(entry.Legacy);
        Assert.Null(entry.OldPath);

        // After rollback the store can be migrated again.
        Assert.Equal(2, StoreMigrator.Migrate(_root).Moves.Count);
    }

    [Fact]
    public void Migrate_LeavesGeoCacheInPlace()
    {
        var geoDir = StorePaths.GeoCacheDir(_root);
        Directory.CreateDirectory(geoDir);
        File.WriteAllText(Path.Combine(geoDir, "栖岩寺.json"), "{}");
        CreateLegacyProject("bookA", "a.pdf", new byte[] { 6 });

        var result = StoreMigrator.Migrate(_root);

        Assert.True(Directory.Exists(geoDir));
        Assert.True(File.Exists(Path.Combine(geoDir, "栖岩寺.json")));
        Assert.Contains(result.Skipped, s => s.Contains("geocoding", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AfterMigration_IndexAndLookupWorkOnShardedLayout()
    {
        CreateLegacyProject("bookA", "a.pdf", new byte[] { 8 });
        var pdf = Path.Combine(_pdfs, "a.pdf");
        StoreMigrator.Migrate(_root);

        // A fresh rebuild (index lost) still discovers the sharded projects.
        File.Delete(StorePaths.IndexPath(_root));
        var index = StoreIndex.Build(_root);

        Assert.Single(index.Projects);
        Assert.False(index.Projects[0].Legacy);
        var matches = StoreIndex.FindByPdfPath(index, pdf);
        Assert.Single(matches);
        Assert.NotNull(matches[0].ProjectId);
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

    [Fact]
    public void CreateProject_Default_CreatesShardedProjectWithContentId()
    {
        var pdfPath = Path.Combine(_pdfs, "new.pdf");
        File.WriteAllBytes(pdfPath, new byte[] { 4, 5, 6 });
        var service = new ProjectService(new FakeSettingsService { Settings = new CalySettings { ProjectHome = _root } });

        var projectPath = service.CreateProject(pdfPath);

        var expectedId = StoreMigrator.ComputeProjectIdForPdf(pdfPath);
        Assert.Equal(StorePaths.ShardedProjectDir(_root, expectedId), projectPath);
        Assert.Equal(expectedId, ReadProjectId(projectPath));

        var entry = StoreIndex.Load(_root)!.Projects.Single();
        Assert.False(entry.Legacy);
        Assert.Equal(expectedId, entry.ProjectId);
    }

    [Fact]
    public void CreateProject_SamePdfTwice_GetsDistinctShardedProjects()
    {
        var pdfPath = Path.Combine(_pdfs, "dup.pdf");
        File.WriteAllBytes(pdfPath, new byte[] { 1, 1 });
        var service = new ProjectService(new FakeSettingsService { Settings = new CalySettings { ProjectHome = _root } });

        var p1 = service.CreateProject(pdfPath);
        var p2 = service.CreateProject(pdfPath);

        Assert.NotEqual(p1, p2);
        Assert.True(Directory.Exists(p1));
        Assert.True(Directory.Exists(p2));
        Assert.Equal(2, service.FindAllProjects(pdfPath).Count);
    }

    [Fact]
    public void CreateProject_NotesShardedCreationInManifest()
    {
        CreateLegacyProject("bookA", "a.pdf", new byte[] { 2, 2 });
        StoreMigrator.Migrate(_root); // manifest records NewProjectLayout=flat until Phase C creation runs
        var pdfPath = Path.Combine(_pdfs, "c.pdf");
        File.WriteAllBytes(pdfPath, new byte[] { 3, 3 });
        var service = new ProjectService(new FakeSettingsService { Settings = new CalySettings { ProjectHome = _root } });

        service.CreateProject(pdfPath);

        var manifest = StoreManifest.Load(_root);
        Assert.Equal("sharded", manifest!.NewProjectLayout);
    }

    [Fact]
    public void CreateProject_KeepsHumanReadableNameAfterMigration()
    {
        CreateLegacyProject("我的书名", "x.pdf", new byte[] { 8, 8 });
        StoreMigrator.Migrate(_root);

        var entry = StoreIndex.Load(_root)!.Projects.Single();
        var metadata = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(entry.ProjectPath, "project.json")),
            SourceGenerationContext.Default.ProjectMetadata)!;
        // Migration keeps the name recorded at creation; the directory became a hash id.
        Assert.Equal("我的书名", metadata.ProjectName);
    }

    [Fact]
    public void FindAllProjects_ResolvesViaIndex_BeforeAndAfterMigration()
    {
        var p1 = CreateLegacyProject("bookA", "a.pdf", new byte[] { 1 });
        var p2 = CreateLegacyProject("bookA_2", "a.pdf", new byte[] { 1 });
        var pdfPath = Path.Combine(_pdfs, "a.pdf");
        var service = new ProjectService(new FakeSettingsService { Settings = new CalySettings { ProjectHome = _root } });

        // Before migration: flat layout, historical ordering preserved (base name, then _N).
        Assert.Equal(new[] { p1, p2 }, service.FindAllProjects(pdfPath));

        // After migration: same two projects resolve from the sharded layout.
        StoreMigrator.Migrate(_root);
        var found = service.FindAllProjects(pdfPath);
        Assert.Equal(2, found.Count);
        Assert.All(found, p => Assert.StartsWith(StorePaths.ProjectsDir(_root), p));
    }

    [Fact]
    public void ProjectService_MigrateStore_EndToEnd()
    {
        CreateLegacyProject("bookA", "a.pdf", new byte[] { 1, 1 });
        var settings = new FakeSettingsService { Settings = new CalySettings { ProjectHome = _root } };
        var service = new ProjectService(settings);

        var result = service.MigrateStore();

        Assert.Single(result.Moves);
        Assert.True(Directory.Exists(Path.Combine(
            StorePaths.ProjectsDir(_root),
            result.Moves[0].ProjectId[..2],
            result.Moves[0].ProjectId.Substring(2, 2),
            result.Moves[0].ProjectId)));

        // The flat path that ViewModels may still hold is resolvable via the index.
        Assert.Empty(Directory.GetDirectories(_root, "bookA"));
    }
}
