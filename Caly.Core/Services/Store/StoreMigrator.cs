using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Caly.Core.Models;

namespace Caly.Core.Services.Store;

/// <summary>A single planned or executed project move (Phase B migration).</summary>
public sealed class StoreMove
{
    public string From { get; init; } = string.Empty;
    public string To { get; init; } = string.Empty;
    public string ProjectId { get; init; } = string.Empty;
}

/// <summary>Result of a migrate/plan/rollback call.</summary>
public sealed class StoreMigrateResult
{
    public bool AlreadyMigrated { get; set; }
    public bool DryRun { get; set; }

    /// <summary>Moves planned (dry run) or executed (applied).</summary>
    public List<StoreMove> Moves { get; } = new();

    /// <summary>Reasons projects/directories were left in place.</summary>
    public List<string> Skipped { get; } = new();
}

/// <summary>
/// Phase B one-time migration: moves flat legacy project directories to the sharded
/// V2 layout keyed by a content-derived project id
/// ({root}/projects/{id[0:2]}/{id[2:4]}/{id}, docs/traceview-storage-layout-spec.md §9).
/// Moves are same-volume renames, so the physical data cost is independent of size.
/// The geocoding cache stays in place: it is a bind-mount written by the Nominatim
/// container, so its location is owned by that mount configuration.
/// </summary>
public static class StoreMigrator
{
    /// <summary>
    /// Plans the migration without touching the disk.
    /// </summary>
    public static StoreMigrateResult Plan(string root)
    {
        var result = new StoreMigrateResult { DryRun = true };
        var manifest = StoreManifest.Load(root);
        if (manifest is not null && manifest.LayoutVersion >= StoreManifest.LayoutVersionSharded)
        {
            result.AlreadyMigrated = true;
            return result;
        }

        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in StoreIndex.Build(root).Projects)
        {
            if (!entry.Legacy)
            {
                // Already under projects/ - leave in place.
                continue;
            }

            var id = ComputeProjectId(entry);
            if (!usedIds.Add(id))
            {
                // Two projects share the same content (e.g. {name} and {name}_2 pointing at
                // the same PDF): keep them distinct with a deterministic directory-derived suffix.
                id = id + "-" + Sha256Hex(entry.DirName.ToLowerInvariant())[..8];
                usedIds.Add(id);
            }

            result.Moves.Add(new StoreMove
            {
                From = entry.ProjectPath,
                To = StorePaths.ShardedProjectDir(root, id),
                ProjectId = id
            });
        }

        if (Directory.Exists(StorePaths.GeoCacheDir(root)))
        {
            result.Skipped.Add("geocoding cache: left in place (Nominatim container bind-mount)");
        }

        return result;
    }

    /// <summary>
    /// Applies the migration. Idempotent: a store already at layout 2 is returned unchanged.
    /// </summary>
    public static StoreMigrateResult Migrate(string root, bool dryRun = false)
    {
        var result = Plan(root);
        result.DryRun = dryRun;
        if (result.AlreadyMigrated || dryRun)
        {
            return result;
        }

        var now = DateTime.UtcNow;
        var index = StoreIndex.LoadOrBuild(root);
        var manifest = StoreManifest.Load(root) ?? new StoreManifest { CreatedAt = now };

        foreach (var move in result.Moves)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(move.To)!);
            Directory.Move(move.From, move.To);

            SetProjectIdInManifest(move.To, move.ProjectId);

            var entry = index.Projects.Find(p => string.Equals(p.ProjectPath, move.From, StringComparison.OrdinalIgnoreCase));
            if (entry is not null)
            {
                entry.OldPath = move.From;
                entry.ProjectPath = move.To;
                entry.DirName = Path.GetFileName(move.To);
                entry.ProjectId = move.ProjectId;
                entry.Legacy = false;
                entry.MigratedAt = now;
            }
        }

        StoreIndex.Save(root, index);

        manifest.LayoutVersion = StoreManifest.LayoutVersionSharded;
        manifest.MigratedAt = now;
        manifest.NewProjectLayout = "sharded";
        StoreManifest.Save(root, manifest);

        return result;
    }

    /// <summary>
    /// Reverses a migration using the index's recorded OldPath values: moves every project
    /// back to its flat location and removes the sharded layout marker.
    /// </summary>
    public static StoreMigrateResult Rollback(string root)
    {
        var result = new StoreMigrateResult();
        var manifest = StoreManifest.Load(root);
        if (manifest is null || manifest.LayoutVersion < StoreManifest.LayoutVersionSharded)
        {
            return result;
        }

        var index = StoreIndex.LoadOrBuild(root);
        foreach (var entry in index.Projects)
        {
            if (entry.OldPath is null || entry.Legacy)
            {
                continue;
            }

            if (!Directory.Exists(entry.ProjectPath))
            {
                result.Skipped.Add($"missing current location for {entry.DirName}");
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(entry.OldPath)!);
            Directory.Move(entry.ProjectPath, entry.OldPath);

            result.Moves.Add(new StoreMove
            {
                From = entry.ProjectPath,
                To = entry.OldPath,
                ProjectId = entry.ProjectId ?? string.Empty
            });

            entry.ProjectPath = entry.OldPath;
            entry.DirName = Path.GetFileName(entry.OldPath);
            entry.OldPath = null;
            entry.Legacy = true;
        }

        PruneEmptyShards(StorePaths.ProjectsDir(root));
        StoreIndex.Save(root, index);
        File.Delete(StorePaths.ManifestPath(root));
        return result;
    }

    /// <summary>
    /// Content-derived project id: sha256(pdf)[0:32]. Falls back to a deterministic
    /// hash of the recorded PDF path + directory name when the original PDF is gone.
    /// </summary>
    public static string ComputeProjectId(StoreProjectEntry entry) =>
        ComputeProjectIdForPdf(entry.PdfPath ?? string.Empty);

    /// <summary>
    /// Project id for a PDF path: sha256 of the file content when readable, else a
    /// deterministic hash of the path itself. Used by both migration and creation.
    /// </summary>
    public static string ComputeProjectIdForPdf(string pdfPath)
    {
        try
        {
            if (!string.IsNullOrEmpty(pdfPath) && File.Exists(pdfPath))
            {
                using var fs = File.OpenRead(pdfPath);
                return Convert.ToHexStringLower(SHA256.HashData(fs))[..StorePaths.ProjectIdHexLength];
            }
        }
        catch
        {
            // Fall through to the seed hash below.
        }

        return Sha256Hex(pdfPath.ToLowerInvariant())[..StorePaths.ProjectIdHexLength];
    }

    /// <summary>Deterministic next candidate id when a freshly computed id is already taken.</summary>
    public static string ChainProjectId(string previousId) =>
        Sha256Hex(previousId)[..StorePaths.ProjectIdHexLength];

    private static string Sha256Hex(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>Records the project id in the project's project.json (atomic rewrite).</summary>
    private static void SetProjectIdInManifest(string projectPath, string projectId)
    {
        var metadataPath = Path.Combine(projectPath, "project.json");
        ProjectMetadata? metadata;
        try
        {
            var json = File.ReadAllText(metadataPath);
            metadata = JsonSerializer.Deserialize(json, SourceGenerationContext.Default.ProjectMetadata);
        }
        catch
        {
            // Unparseable manifest: create a minimal one so the id is still recorded in the index.
            metadata = new ProjectMetadata { ProjectPath = projectPath };
        }

        metadata ??= new ProjectMetadata { ProjectPath = projectPath };
        metadata.ProjectPath = projectPath;
        // Keep the human-readable name recorded at creation; the directory becomes a hash id.
        metadata.ProjectId = projectId;

        var tempPath = metadataPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(metadata, SourceGenerationContext.Default.ProjectMetadata));
        File.Move(tempPath, metadataPath, overwrite: true);
    }

    /// <summary>Removes now-empty shard directories left behind by a rollback.</summary>
    private static void PruneEmptyShards(string projectsRoot)
    {
        if (!Directory.Exists(projectsRoot))
        {
            return;
        }

        foreach (var shardA in Directory.GetDirectories(projectsRoot))
        {
            foreach (var shardB in Directory.GetDirectories(shardA))
            {
                if (Directory.GetFileSystemEntries(shardB).Length == 0)
                {
                    Directory.Delete(shardB);
                }
            }

            if (Directory.GetFileSystemEntries(shardA).Length == 0)
            {
                Directory.Delete(shardA);
            }
        }

        if (Directory.GetFileSystemEntries(projectsRoot).Length == 0)
        {
            Directory.Delete(projectsRoot);
        }
    }
}
