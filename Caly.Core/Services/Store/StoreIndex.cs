using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Caly.Core.Models;

namespace Caly.Core.Services.Store;

/// <summary>
/// A project registered in the store index. In Phase A every existing project keeps its
/// legacy location on disk and is registered in place with <see cref="Legacy"/> = true
/// (docs/traceview-storage-layout-spec.md §9 Phase A).
/// </summary>
public sealed class StoreProjectEntry
{
    /// <summary>Directory name under the store root.</summary>
    public string DirName { get; set; } = string.Empty;

    /// <summary>Absolute project directory path.</summary>
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>Original PDF path recorded in project.json.</summary>
    public string PdfPath { get; set; } = string.Empty;

    /// <summary>Original PDF file name.</summary>
    public string PdfFileName { get; set; } = string.Empty;

    /// <summary>Project creation timestamp.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Last modification timestamp, if recorded.</summary>
    public DateTime? LastModified { get; set; }

    /// <summary>True while the project still lives at its legacy flat location.
    /// Always written: the context ignores default values, and the initializer default is
    /// true, so an omitted <c>false</c> would silently round-trip back to true.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool Legacy { get; set; } = true;

    /// <summary>Stable project id (sha256-derived) assigned by Phase B migration; null for never-migrated projects.</summary>
    public string? ProjectId { get; set; }

    /// <summary>Legacy flat location the project was moved from (Phase B); null if never moved.</summary>
    public string? OldPath { get; set; }

    /// <summary>Timestamp of the Phase B move.</summary>
    public DateTime? MigratedAt { get; set; }
}

/// <summary>Serialized form of the store index ({root}/store-index.json).</summary>
public sealed class StoreIndexFile
{
    /// <summary>Index schema version.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Timestamp of the last build/save.</summary>
    public DateTime GeneratedAt { get; set; }

    /// <summary>All registered projects.</summary>
    public List<StoreProjectEntry> Projects { get; set; } = new();
}

/// <summary>
/// Rebuildable index over the projects in a store root. The directory contents stay the
/// source of truth; <see cref="Build"/> can always reconstruct the index by scanning
/// project.json files, so a corrupted or missing index self-heals on the next rebuild.
/// </summary>
public static class StoreIndex
{
    /// <summary>
    /// Scans the store root for project directories and builds a fresh index. Handles both
    /// the legacy flat layout (top-level directories containing a project.json) and the
    /// the sharded layout ({root}/projects/{aa}/{bb}/{id}). Reserved directories
    /// (tmp, geocoding_cache, projects) are skipped.
    /// </summary>
    public static StoreIndexFile Build(string root)
    {
        var index = new StoreIndexFile
        {
            Version = 1,
            GeneratedAt = DateTime.UtcNow
        };

        if (!Directory.Exists(root))
        {
            return index;
        }

        // Legacy flat layout: top-level project directories.
        foreach (var dir in Directory.GetDirectories(root))
        {
            var dirName = Path.GetFileName(dir);
            if (string.Equals(dirName, StorePaths.TmpDirectoryName, StringComparison.Ordinal) ||
                string.Equals(dirName, StorePaths.GeoCacheDirectoryName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(dirName, StorePaths.ProjectsDirectoryName, StringComparison.OrdinalIgnoreCase) ||
                dirName.StartsWith('.'))
            {
                continue;
            }

            TryAddProject(index, dir, legacy: true);
        }

        // Sharded layout: projects/{aa}/{bb}/{id}.
        var projectsRoot = StorePaths.ProjectsDir(root);
        if (Directory.Exists(projectsRoot))
        {
            foreach (var shardA in Directory.GetDirectories(projectsRoot))
            {
                foreach (var shardB in Directory.GetDirectories(shardA))
                {
                    foreach (var projectDir in Directory.GetDirectories(shardB))
                    {
                        TryAddProject(index, projectDir, legacy: false);
                    }
                }
            }
        }

        index.Projects.Sort((a, b) => string.CompareOrdinal(a.DirName, b.DirName));
        return index;
    }

    private static void TryAddProject(StoreIndexFile index, string dir, bool legacy)
    {
        var metadataPath = Path.Combine(dir, "project.json");
        if (!File.Exists(metadataPath))
        {
            return;
        }

        ProjectMetadata? metadata;
        try
        {
            var json = File.ReadAllText(metadataPath);
            metadata = JsonSerializer.Deserialize(json, SourceGenerationContext.Default.ProjectMetadata);
        }
        catch
        {
            // Unreadable metadata: skip; doctor/rebuild heuristics can flag it later.
            return;
        }

        if (metadata is null)
        {
            return;
        }

        index.Projects.Add(new StoreProjectEntry
        {
            DirName = Path.GetFileName(dir),
            ProjectPath = dir,
            PdfPath = metadata.PdfPath,
            PdfFileName = metadata.PdfFileName,
            CreatedAt = metadata.CreatedAt,
            LastModified = metadata.LastModified,
            Legacy = legacy,
            ProjectId = metadata.ProjectId
        });
    }

    /// <summary>Loads the persisted index. Returns null when missing or unreadable.</summary>
    public static StoreIndexFile? Load(string root)
    {
        try
        {
            var path = StorePaths.IndexPath(root);
            if (!File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize(json, SourceGenerationContext.Default.StoreIndexFile);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Atomically persists the index (temp file + move) so a crash cannot leave a
    /// half-written index behind.
    /// </summary>
    public static void Save(string root, StoreIndexFile index)
    {
        Directory.CreateDirectory(root);
        var path = StorePaths.IndexPath(root);
        var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var json = JsonSerializer.Serialize(index, SourceGenerationContext.Default.StoreIndexFile);
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, path, overwrite: true);
    }

    /// <summary>
    /// Loads the persisted index, building and saving one when absent.
    /// </summary>
    public static StoreIndexFile LoadOrBuild(string root)
    {
        return Load(root) ?? BuildAndSave(root);
    }

    /// <summary>Rebuilds from disk and persists in one step.</summary>
    public static StoreIndexFile BuildAndSave(string root)
    {
        var index = Build(root);
        Save(root, index);
        return index;
    }

    /// <summary>
    /// Upserts the entry for the given project directory and saves.
    /// </summary>
    public static void Register(string root, StoreProjectEntry entry)
    {
        var index = LoadOrBuild(root);
        index.Projects.RemoveAll(p => string.Equals(p.ProjectPath, entry.ProjectPath, StringComparison.OrdinalIgnoreCase));
        index.Projects.Add(entry);
        index.GeneratedAt = DateTime.UtcNow;
        Save(root, index);
    }

    /// <summary>
    /// Finds all entries whose recorded PDF matches the given path (case-insensitive).
    /// Replaces the basename-glob discovery of the legacy layout for indexed lookups.
    /// </summary>
    public static List<StoreProjectEntry> FindByPdfPath(StoreIndexFile index, string pdfPath)
    {
        var results = new List<StoreProjectEntry>();
        foreach (var entry in index.Projects)
        {
            if (string.Equals(entry.PdfPath, pdfPath, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(entry);
            }
        }

        results.Sort((a, b) => string.CompareOrdinal(a.DirName, b.DirName));
        return results;
    }
}
