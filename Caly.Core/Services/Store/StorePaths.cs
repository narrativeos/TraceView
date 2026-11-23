using System;
using System.IO;
using Caly.Core.Services.Interfaces;

namespace Caly.Core.Services.Store;

/// <summary>
/// Single source of truth for resolving TraceView store paths
/// (see docs/traceview-storage-layout-spec.md).
/// Phase A: the on-disk layout is unchanged (legacy flat layout under ProjectHome);
/// this class centralizes resolution so no other code hardcodes "~/.TraceView".
/// </summary>
public static class StorePaths
{
    /// <summary>Directory name of the default store root under the user profile.</summary>
    public const string LegacyDirectoryName = ".TraceView";

    /// <summary>Name of the rebuildable project index file within the store root.</summary>
    public const string IndexFileName = "store-index.json";

    /// <summary>Transient scratch directory within the store root (spec §4: cleared on startup by age).</summary>
    public const string TmpDirectoryName = "tmp";

    /// <summary>Subdirectory of <see cref="TmpDirectoryName"/> used when no project path is available.</summary>
    public const string NoProjectDirectoryName = "no_project";

    /// <summary>Legacy flat geocoding cache directory within the store root.</summary>
    public const string GeoCacheDirectoryName = "geocoding_cache";

    /// <summary>Root manifest recording the store layout version.</summary>
    public const string ManifestFileName = "store.json";

    /// <summary>Directory holding sharded projects (V2 layout).</summary>
    public const string ProjectsDirectoryName = "projects";

    /// <summary>Length of the hex project id (first 32 hex chars of SHA-256).</summary>
    public const int ProjectIdHexLength = 32;

    /// <summary>
    /// Default store root: ~/.TraceView. The only place the user-profile literal appears.
    /// </summary>
    public static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), LegacyDirectoryName);

    /// <summary>
    /// Resolves the store root from settings (ProjectHome), falling back to
    /// <see cref="DefaultRoot"/> when settings or DI are unavailable.
    /// Replaces the previously hardcoded UserProfile + ".TraceView" fallbacks,
    /// so the configured ProjectHome is honored everywhere.
    /// </summary>
    public static string ResolveRoot()
    {
        try
        {
            var settings = (App.Current?.Services?.GetService(typeof(ISettingsService)) as ISettingsService)?.GetSettings();
            if (!string.IsNullOrWhiteSpace(settings?.ProjectHome))
            {
                return settings.ProjectHome;
            }
        }
        catch
        {
            // DI not available (e.g. unit tests) - use the default root.
        }

        return DefaultRoot;
    }

    /// <summary>Path of the rebuildable project index for the given store root.</summary>
    public static string IndexPath(string root) => Path.Combine(root, IndexFileName);

    /// <summary>Legacy per-document directory {root}/{docId} (pre-V2 layout).</summary>
    public static string LegacyProjectDir(string root, string docId) => Path.Combine(root, docId);

    /// <summary>Legacy MinerU extraction cache {root}/{docId}/mineru.</summary>
    public static string LegacyMineruCacheDir(string root, string docId) =>
        Path.Combine(LegacyProjectDir(root, docId), "mineru");

    /// <summary>Transient scratch directory ({root}/tmp).</summary>
    public static string TmpDir(string root) => Path.Combine(root, TmpDirectoryName);

    /// <summary>
    /// Fallback directory for output produced without a project context ({root}/tmp/no_project).
    /// Formerly {root}/__no_project__; the flat-layout directory never existed in real stores,
    /// so no migration is required.
    /// </summary>
    public static string NoProjectDir(string root) => Path.Combine(TmpDir(root), NoProjectDirectoryName);

    /// <summary>Legacy flat geocoding cache directory.</summary>
    public static string GeoCacheDir(string root) => Path.Combine(root, GeoCacheDirectoryName);

    /// <summary>Regeneratable cache root for the store ({root}/cache, spec §4).</summary>
    public static string StoreCacheDir(string root) => Path.Combine(root, "cache");

    /// <summary>Store-level MinerU result cache ({root}/cache/mineru).</summary>
    public static string StoreMinerUCacheDir(string root) => Path.Combine(StoreCacheDir(root), "mineru");

    /// <summary>Store-level Popo result cache ({root}/cache/popo).</summary>
    public static string StorePopoCacheDir(string root) => Path.Combine(StoreCacheDir(root), "popo");

    /// <summary>
    /// One-time relocation of a legacy %LocalAppData%/Caly/{sub} cache into <paramref name="targetDir"/>
    /// (spec §1: second cache root). Best effort: failures leave the legacy dir in place and are
    /// retried on the next call. Returns true when <paramref name="targetDir"/> ends up populated.
    /// </summary>
    public static bool MigrateLegacyAppCacheDir(string sub, string targetDir)
    {
        try
        {
            var legacy = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Caly", sub);
            if (!Directory.Exists(legacy))
            {
                return Directory.Exists(targetDir);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetDir)!);
            if (!Directory.Exists(targetDir))
            {
                Directory.Move(legacy, targetDir);
                return true;
            }

            // Target already in use: merge entries, never overwrite newer data.
            foreach (var entry in Directory.GetFileSystemEntries(legacy))
            {
                var dest = Path.Combine(targetDir, Path.GetFileName(entry));
                try
                {
                    if (!Directory.Exists(dest) && !File.Exists(dest))
                    {
                        Directory.Move(entry, dest);
                    }
                    else if (Directory.Exists(entry))
                    {
                        Directory.Delete(entry, recursive: true);
                    }
                }
                catch
                {
                    // Conflicting entry stays in the legacy dir; GC or the next run resolves it.
                }
            }

            try { Directory.Delete(legacy, recursive: true); } catch { /* still non-empty */ }
            return Directory.Exists(targetDir);
        }
        catch
        {
            // Cross-volume or permission issue: keep using the legacy path as-is.
            return Directory.Exists(targetDir);
        }
    }

    /// <summary>Root manifest path ({root}/store.json).</summary>
    public static string ManifestPath(string root) => Path.Combine(root, ManifestFileName);

    /// <summary>Sharded projects directory ({root}/projects).</summary>
    public static string ProjectsDir(string root) => Path.Combine(root, ProjectsDirectoryName);

    /// <summary>
    /// Sharded project directory for a project id: {root}/projects/{id[0:2]}/{id[2:4]}/{id}.
    /// Two-level hex sharding keeps any single directory's entry count bounded.
    /// </summary>
    public static string ShardedProjectDir(string root, string projectId)
    {
        if (projectId.Length < 4)
        {
            throw new ArgumentException($"Project id must be at least {4} hex chars.", nameof(projectId));
        }

        return Path.Combine(ProjectsDir(root), projectId[..2], projectId.Substring(2, 2), projectId);
    }

    /// <summary>
    /// True when the path lives under {root}/projects (V2 sharded layout).
    /// </summary>
    public static bool IsShardedPath(string root, string path)
    {
        var projectsRoot = ProjectsDir(root);
        return path.StartsWith(projectsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || string.Equals(path, projectsRoot, StringComparison.OrdinalIgnoreCase);
    }
}
