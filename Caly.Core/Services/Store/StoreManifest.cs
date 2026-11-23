using System;
using System.IO;
using System.Text.Json;

namespace Caly.Core.Services.Store;

/// <summary>
/// Root manifest ({root}/store.json) recording which on-disk layout the store uses.
/// Layout version 1: flat legacy layout under the root.
/// Layout version 2: projects live under {root}/projects/{aa}/{bb}/{projectId}
/// (docs/traceview-storage-layout-spec.md §4).
/// </summary>
public sealed class StoreManifest
{
    /// <summary>Legacy flat layout (pre-migration).</summary>
    public const int LayoutVersionLegacy = 1;

    /// <summary>Sharded layout produced by <see cref="StoreMigrator.Migrate"/>.</summary>
    public const int LayoutVersionSharded = 2;

    public int LayoutVersion { get; set; } = LayoutVersionLegacy;

    /// <summary>When the store was first initialized.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>When the store was migrated to sharded layout (layout 2 only).</summary>
    public DateTime? MigratedAt { get; set; }

    /// <summary>Where newly created projects land: "flat" until Phase C switches creation to sharded.</summary>
    public string NewProjectLayout { get; set; } = "flat";

    /// <summary>Loads the manifest. Returns null when the store has none yet (pre-Phase-A store).</summary>
    public static StoreManifest? Load(string root)
    {
        try
        {
            var path = StorePaths.ManifestPath(root);
            if (!File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize(json, SourceGenerationContext.Default.StoreManifest);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Atomically persists the manifest.</summary>
    public static void Save(string root, StoreManifest manifest)
    {
        Directory.CreateDirectory(root);
        var path = StorePaths.ManifestPath(root);
        var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var json = JsonSerializer.Serialize(manifest, SourceGenerationContext.Default.StoreManifest);
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, path, overwrite: true);
    }
}
