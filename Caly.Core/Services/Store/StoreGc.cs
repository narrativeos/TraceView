using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Caly.Core.Services.Store;

/// <summary>
/// Garbage collection for the regeneratable cache layer ({root}/cache, spec §7):
/// age-based (TTL) removal plus capacity-bounded LRU eviction. Everything under cache/ can be
/// regenerated from the service, so eviction is always safe; projects/ and tmp/ are not touched.
/// </summary>
public static class StoreGc
{
    /// <summary>Cache entries untouched for longer than this are evicted regardless of capacity.</summary>
    public static readonly TimeSpan DefaultCacheTtl = TimeSpan.FromDays(90);

    /// <summary>Total cache budget; oldest entries are evicted until the tree fits (§7: 容量上限).</summary>
    public const long DefaultCacheMaxBytes = 4L * 1024 * 1024 * 1024; // 4 GB

    /// <summary>
    /// Runs one GC pass over {root}/cache. Entry granularity: files and directories directly under a
    /// cache category directory (cache/mineru/{key}, cache/mineru/{key}.zip, cache/popo/...).
    /// Returns the number of bytes freed.
    /// </summary>
    public static long CleanupCache(string root, TimeSpan? ttl = null, long? maxBytes = null)
    {
        var cacheRoot = StorePaths.StoreCacheDir(root);
        if (!Directory.Exists(cacheRoot))
        {
            return 0;
        }

        var ttlValue = ttl ?? DefaultCacheTtl;
        var budget = maxBytes ?? DefaultCacheMaxBytes;
        var now = DateTime.UtcNow;
        var freed = 0L;

        // Pass 1: TTL.
        foreach (var category in Directory.GetDirectories(cacheRoot))
        {
            foreach (var entry in Directory.GetFileSystemEntries(category))
            {
                if ((now - GetAgeUtc(entry)) > ttlValue)
                {
                    freed += DeleteEntry(entry);
                }
            }
        }

        // Pass 2: capacity-bounded LRU over the surviving entries, oldest first.
        var total = CountBytes(cacheRoot);
        if (total > budget)
        {
            var entries = new List<(string Path, DateTime AgeUtc, long Bytes)>();
            foreach (var category in Directory.GetDirectories(cacheRoot))
            {
                foreach (var entry in Directory.GetFileSystemEntries(category))
                {
                    entries.Add((entry, GetAgeUtc(entry), Directory.Exists(entry) ? CountBytes(entry) : new FileInfo(entry).Length));
                }
            }

            foreach (var entry in entries.OrderBy(e => e.AgeUtc))
            {
                if (total <= budget)
                {
                    break;
                }

                freed += DeleteEntry(entry.Path);
                total -= entry.Bytes;
            }
        }

        return freed;
    }

    private static DateTime GetAgeUtc(string path) =>
        Directory.Exists(path) ? Directory.GetLastWriteTimeUtc(path) : File.GetLastWriteTimeUtc(path);

    private static long DeleteEntry(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                var bytes = CountBytes(path);
                Directory.Delete(path, recursive: true);
                return bytes;
            }

            var length = new FileInfo(path).Length;
            File.Delete(path);
            return length;
        }
        catch
        {
            // In-use entry: retried on the next pass.
            return 0;
        }
    }

    private static long CountBytes(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                return Directory
                    .EnumerateFiles(path, "*", SearchOption.AllDirectories)
                    .Sum(f => new FileInfo(f).Length);
            }

            return new FileInfo(path).Length;
        }
        catch
        {
            return 0;
        }
    }
}
