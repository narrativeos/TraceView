using System;
using System.Collections.Generic;
using System.IO;

namespace Caly.Core.Services.Store;

/// <summary>
/// Task staging protocol for the store (docs/traceview-storage-layout-spec.md §4 rule 3, §8, §11):
/// long-running task output is staged under {root}/tmp/{taskId}/, and writes become visible only
/// through validation + atomic rename. Crash residue can therefore only ever live in tmp/, and
/// <see cref="CleanupStaleTasks"/> removes it on startup once it exceeds the age bound.
/// </summary>
public static class StoreTask
{
    /// <summary>Staged task directories older than this are removed at startup (§7: 超龄即删).</summary>
    public static readonly TimeSpan DefaultStaleTaskMaxAge = TimeSpan.FromHours(24);

    /// <summary>
    /// Validates a task id against the §4 naming rule: only hex and fixed ASCII ([A-Za-z0-9._-]).
    /// </summary>
    public static bool IsValidTaskId(string taskId)
    {
        if (string.IsNullOrEmpty(taskId))
        {
            return false;
        }

        foreach (var c in taskId)
        {
            var ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                     (c >= '0' && c <= '9') || c == '-' || c == '_' || c == '.';
            if (!ok || c == '.' && taskId.IndexOf('.') == taskId.Length - 1)
            {
                // Disallow path traversal outright: no "..", no separators, no leading-dot names.
                return false;
            }
        }

        return !taskId.Contains("..") && !taskId.StartsWith('.');
    }

    /// <summary>Staging directory for a task: {root}/tmp/{taskId}.</summary>
    public static string StagingDir(string root, string taskId)
    {
        if (!IsValidTaskId(taskId))
        {
            throw new ArgumentException($"Task id '{taskId}' violates the store naming rule (§4 rule 1).", nameof(taskId));
        }

        return Path.Combine(StorePaths.TmpDir(root), taskId);
    }

    /// <summary>Creates the staging directory for a task and returns its path.</summary>
    public static string BeginTask(string root, string taskId)
    {
        var dir = StagingDir(root, taskId);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Ends a task by deleting its staging directory (任务结束必删). Missing directories are ignored.
    /// Publishing pipelines rename the staged content out before calling this.
    /// </summary>
    public static void EndTask(string root, string taskId)
    {
        var dir = StagingDir(root, taskId);
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// Startup sweep: deletes crash residue under tmp/ older than <paramref name="maxAge"/>,
    /// returning the deleted entry names. The no-project fallback output directory
    /// ({root}/tmp/no_project) is never touched: until the §8 publish pipeline exists it holds
    /// real semantic output that must survive restarts (spec §9 差异裁决).
    /// </summary>
    public static List<string> CleanupStaleTasks(string root, TimeSpan? maxAge = null)
    {
        var removed = new List<string>();
        var tmpRoot = StorePaths.TmpDir(root);
        if (!Directory.Exists(tmpRoot))
        {
            return removed;
        }

        var age = maxAge ?? DefaultStaleTaskMaxAge;
        var now = DateTime.UtcNow;

        foreach (var dir in Directory.GetDirectories(tmpRoot))
        {
            var name = Path.GetFileName(dir);
            if (string.Equals(name, StorePaths.NoProjectDirectoryName, StringComparison.Ordinal))
            {
                continue;
            }

            if ((now - Directory.GetLastWriteTimeUtc(dir)) > age)
            {
                try
                {
                    Directory.Delete(dir, recursive: true);
                    removed.Add(name);
                }
                catch
                {
                    // In-use residue is retried at the next startup sweep.
                }
            }
        }

        return removed;
    }
}
