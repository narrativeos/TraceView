using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Caly.Core.Services.Store;

/// <summary>
/// Read-only consistency scan of a store root (docs/traceview-storage-layout-spec.md §7, §11):
/// index vs disk agreement, tmp/ residue and age, layout manifest, and the geocoding cache.
/// The scan never mutates the store; GC and repair are separate operations.
/// </summary>
public sealed class StoreDoctorReport
{
    public string Root { get; set; } = string.Empty;

    /// <summary>store.json layout version; 0 when the manifest is missing or unreadable.</summary>
    public int LayoutVersion { get; set; }

    public string NewProjectLayout { get; set; } = string.Empty;

    /// <summary>Whether store-index.json was present (rebuilt in memory when not).</summary>
    public bool IndexPresent { get; set; }

    /// <summary>Projects registered in the persisted index.</summary>
    public int IndexedProjectCount { get; set; }

    /// <summary>Projects discovered on disk by a fresh scan.</summary>
    public int DiskProjectCount { get; set; }

    /// <summary>Indexed but directory missing → state broken (§7).</summary>
    public List<string> BrokenProjects { get; } = new();

    /// <summary>On disk but absent from the index → state orphan (§7).</summary>
    public List<string> OrphanProjects { get; } = new();

    /// <summary>Task residue directories currently under tmp/ (no_project excluded).</summary>
    public List<string> TmpEntries { get; } = new();

    /// <summary>tmp/ residue older than the stale-task bound.</summary>
    public List<string> StaleTmpEntries { get; } = new();

    public bool NoProjectOutputPresent { get; set; }

    public bool GeoCachePresent { get; set; }
    public int GeoCacheFileCount { get; set; }

    /// <summary>True when index and disk agree and the store is on the sharded layout manifest.</summary>
    public bool Consistent =>
        BrokenProjects.Count == 0 &&
        OrphanProjects.Count == 0 &&
        LayoutVersion >= StoreManifest.LayoutVersionSharded;

    public string Summarize()
    {
        var lines = new List<string>
        {
            $"store root: {Root}",
            $"layout: v{LayoutVersion} ({NewProjectLayout}), consistent: {Consistent}",
            $"projects: {IndexedProjectCount} indexed / {DiskProjectCount} on disk",
        };

        if (BrokenProjects.Count > 0)
        {
            lines.Add($"broken (indexed, missing on disk): {BrokenProjects.Count} e.g. {BrokenProjects[0]}");
        }

        if (OrphanProjects.Count > 0)
        {
            lines.Add($"orphan (on disk, missing from index): {OrphanProjects.Count} e.g. {OrphanProjects[0]}");
        }

        lines.Add($"tmp: {TmpEntries.Count} residue ({StaleTmpEntries.Count} stale), no_project output: {NoProjectOutputPresent}");
        lines.Add($"geocoding cache: {(GeoCachePresent ? GeoCacheFileCount + " entries" : "absent")}");
        return string.Join(Environment.NewLine, lines);
    }
}

public static class StoreDoctor
{
    /// <summary>
    /// Scans <paramref name="root"/> without mutating anything. Index entries are compared against
    /// a fresh disk scan by project path; tmp/ residue is aged against <see cref="StoreTask.DefaultStaleTaskMaxAge"/>.
    /// </summary>
    public static StoreDoctorReport Scan(string root, TimeSpan? maxAge = null)
    {
        var report = new StoreDoctorReport { Root = root };

        var manifest = StoreManifest.Load(root);
        if (manifest is not null)
        {
            report.LayoutVersion = manifest.LayoutVersion;
            report.NewProjectLayout = manifest.NewProjectLayout;
        }

        var loaded = StoreIndex.Load(root);
        report.IndexPresent = loaded is not null;
        var indexed = loaded?.Projects ?? new List<StoreProjectEntry>();
        report.IndexedProjectCount = indexed.Count;

        // Fresh disk scan: the directories are the source of truth (§5 重建即可自愈).
        var disk = StoreIndex.Build(root);
        report.DiskProjectCount = disk.Projects.Count;

        var diskPaths = new HashSet<string>(
            disk.Projects.Select(p => p.ProjectPath.TrimEnd(Path.DirectorySeparatorChar)),
            StringComparer.OrdinalIgnoreCase);

        foreach (var entry in indexed)
        {
            var path = entry.ProjectPath.TrimEnd(Path.DirectorySeparatorChar);
            if (!Directory.Exists(path))
            {
                report.BrokenProjects.Add(path);
            }
            else if (!diskPaths.Contains(path))
            {
                // Directory exists but no longer yields a valid project.json.
                report.BrokenProjects.Add(path + " (no valid project.json)");
            }
        }

        var indexedPaths = new HashSet<string>(
            indexed.Select(p => p.ProjectPath.TrimEnd(Path.DirectorySeparatorChar)),
            StringComparer.OrdinalIgnoreCase);
        foreach (var entry in disk.Projects)
        {
            var path = entry.ProjectPath.TrimEnd(Path.DirectorySeparatorChar);
            if (!indexedPaths.Contains(path))
            {
                report.OrphanProjects.Add(path);
            }
        }

        // tmp/ residue and age (§11: kill 后重启，tmp 残骸被清理).
        var age = maxAge ?? StoreTask.DefaultStaleTaskMaxAge;
        var tmpRoot = StorePaths.TmpDir(root);
        if (Directory.Exists(tmpRoot))
        {
            var now = DateTime.UtcNow;
            foreach (var dir in Directory.GetDirectories(tmpRoot))
            {
                var name = Path.GetFileName(dir);
                if (string.Equals(name, StorePaths.NoProjectDirectoryName, StringComparison.Ordinal))
                {
                    report.NoProjectOutputPresent = true;
                    continue;
                }

                report.TmpEntries.Add(name);
                if ((now - Directory.GetLastWriteTimeUtc(dir)) > age)
                {
                    report.StaleTmpEntries.Add(name);
                }
            }
        }

        var geoDir = StorePaths.GeoCacheDir(root);
        report.GeoCachePresent = Directory.Exists(geoDir);
        if (report.GeoCachePresent)
        {
            report.GeoCacheFileCount = Directory.GetFiles(geoDir).Length;
        }

        return report;
    }
}
