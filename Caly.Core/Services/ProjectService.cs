using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Caly.Core.Models;
using Caly.Core.Services.Interfaces;
using Caly.Core.Services.Store;

namespace Caly.Core.Services;

/// <summary>
/// Service for managing TraceView project directories.
/// Each PDF document gets its own project folder under ProjectHome.
/// </summary>
public class ProjectService
{
    private readonly ISettingsService _settingsService;

    public ProjectService(ISettingsService settingsService)
    {
        _settingsService = settingsService;

        // Store initialization hook (spec §10): startup sweep of crash residue in tmp/
        // (§8/§11: writes only ever leave debris in tmp/, so clearing stale entries there
        // is safe; the no_project fallback output is excluded by design).
        try
        {
            var home = GetProjectHome();
            if (!string.IsNullOrWhiteSpace(home))
            {
                StoreTask.CleanupStaleTasks(home);

                // §7 cache GC (TTL + capacity): background pass over the regeneratable cache,
                // never blocking startup and never touching projects/ or tmp/.
                _ = Task.Run(() =>
                {
                    try
                    {
                        StoreGc.CleanupCache(home);
                    }
                    catch
                    {
                        // Best effort: the next startup pass retries.
                    }
                });
            }
        }
        catch
        {
            // Best effort: the next startup sweep retries.
        }
    }

    /// <summary>
    /// Read-only consistency scan of the store root (§7/§11): index vs disk agreement,
    /// tmp/ residue, manifest and geocoding cache. Never mutates the store.
    /// </summary>
    public StoreDoctorReport RunStoreDoctor() => StoreDoctor.Scan(GetProjectHome());

    /// <summary>
    /// Gets the ProjectHome directory from settings.
    /// </summary>
    public string GetProjectHome()
    {
        var settings = _settingsService.GetSettings();
        return settings.ProjectHome;
    }

    /// <summary>
    /// Finds all existing project directories for a given PDF file via the store index
    /// (docs/traceview-storage-layout-spec.md §9), so both the legacy flat layout and the
    /// sharded layout resolve. Replaces the {basename}[_N] directory glob, which stopped
    /// matching once migrated directories became content-hash ids.
    /// </summary>
    public List<string> FindAllProjects(string pdfPath)
    {
        var projectHome = GetProjectHome();
        var results = new List<string>();

        if (!Directory.Exists(projectHome))
            return results;

        var index = StoreIndex.LoadOrBuild(projectHome);
        foreach (var entry in StoreIndex.FindByPdfPath(index, pdfPath))
        {
            if (Directory.Exists(entry.ProjectPath))
                results.Add(entry.ProjectPath);
        }

        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(pdfPath);

        // For never-migrated directories keep the historical ordering (base name first, then
        // numeric suffix); sharded directories are hash ids and stay in DirName order.
        results.Sort((a, b) =>
        {
            var nameA = Path.GetFileName(a);
            var nameB = Path.GetFileName(b);
            if (!IsLegacyDirName(nameA, fileNameWithoutExtension) || !IsLegacyDirName(nameB, fileNameWithoutExtension))
                return string.Compare(nameA, nameB, StringComparison.Ordinal);
            if (nameA == fileNameWithoutExtension) return -1;
            if (nameB == fileNameWithoutExtension) return 1;

            var suffixA = nameA.Substring(fileNameWithoutExtension.Length + 1);
            var suffixB = nameB.Substring(fileNameWithoutExtension.Length + 1);
            if (int.TryParse(suffixA, out var numA) && int.TryParse(suffixB, out var numB))
                return numA.CompareTo(numB);
            return string.Compare(nameA, nameB, StringComparison.Ordinal);
        });

        return results;
    }

    private static bool IsLegacyDirName(string dirName, string baseName) =>
        dirName == baseName ||
        (dirName.StartsWith(baseName + "_", StringComparison.Ordinal) && IsNumeric(dirName.Substring(baseName.Length + 1)));

    private static bool IsNumeric(string value)
    {
        return int.TryParse(value, out _);
    }

    /// <summary>
    /// Gets a unique project path. If the default path already exists,
    /// appends _2, _3, etc. to create a new unique directory.
    /// </summary>
    public string GetUniqueProjectPath(string pdfPath)
    {
        return StorePaths.ShardedProjectDir(GetProjectHome(), NextFreeProjectId(pdfPath));
    }

    /// <summary>
    /// First unused project id for the PDF: content hash, then deterministically chained
    /// hashes while the sharded directory is already taken (duplicate-PDF projects).
    /// </summary>
    private string NextFreeProjectId(string pdfPath)
    {
        var root = GetProjectHome();
        var id = StoreMigrator.ComputeProjectIdForPdf(pdfPath);
        while (Directory.Exists(StorePaths.ShardedProjectDir(root, id)))
            id = StoreMigrator.ChainProjectId(id);
        return id;
    }

    /// <summary>
    /// Creates a new project directory with the standard folder structure.
    /// Returns the project path.
    /// </summary>
    public string CreateProject(string pdfPath, string? projectPath = null)
    {
        // Phase C: new projects live in the sharded layout under a content-derived id
        // (docs/traceview-storage-layout-spec.md §9); {basename}[_N] flat directories are
        // only what legacy stores still hold.
        projectPath ??= GetUniqueProjectPath(pdfPath);

        // Create main project directory
        Directory.CreateDirectory(projectPath);

        // Create subdirectories
        var minerUDir = Path.Combine(projectPath, "mineru");
        var popoDir = Path.Combine(projectPath, "popo");
        var semanticDir = Path.Combine(projectPath, "semantic");
        Directory.CreateDirectory(minerUDir);
        Directory.CreateDirectory(popoDir);
        Directory.CreateDirectory(semanticDir);

        var sharded = StorePaths.IsShardedPath(GetProjectHome(), projectPath);

        // Create project metadata
        var metadata = new ProjectMetadata
        {
            PdfPath = pdfPath,
            PdfFileName = Path.GetFileName(pdfPath),
            ProjectName = sharded ? Path.GetFileNameWithoutExtension(pdfPath) : Path.GetFileName(projectPath),
            ProjectPath = projectPath,
            ProjectId = sharded ? Path.GetFileName(projectPath) : null,
            CreatedAt = DateTime.UtcNow
        };

        if (sharded)
            NoteShardedCreation();

        var metadataPath = Path.Combine(projectPath, "project.json");
        File.WriteAllText(metadataPath, JsonSerializer.Serialize(metadata, SourceGenerationContext.Default.ProjectMetadata));

        TryRegisterInIndex(projectPath, metadata);

        return projectPath;
    }

    /// <summary>
    /// Rebuilds the store index (store-index.json) from the projects on disk and returns it.
    /// The index is a rebuildable cache; the directories remain the source of truth
    /// (docs/traceview-storage-layout-spec.md Phase A).
    /// </summary>
    public StoreIndexFile BuildStoreIndex()
    {
        return StoreIndex.BuildAndSave(GetProjectHome());
    }

    /// <summary>
    /// Phase B: migrates flat legacy projects to the sharded layout. Pass dryRun: true to
    /// get the plan without touching the disk. Idempotent; <see cref="RollbackStore"/> reverses it.
    /// </summary>
    public StoreMigrateResult MigrateStore(bool dryRun = false)
    {
        return StoreMigrator.Migrate(GetProjectHome(), dryRun);
    }

    /// <summary>Reverses a Phase B migration using the index's recorded original paths.</summary>
    public StoreMigrateResult RollbackStore()
    {
        return StoreMigrator.Rollback(GetProjectHome());
    }

    /// <summary>Startup sweep wrapper: removes stale task staging directories under tmp/, returns their names.</summary>
    public List<string> CleanupStaleStoreTasks(string root, TimeSpan? maxAge = null) =>
        StoreTask.CleanupStaleTasks(root, maxAge);

    /// <summary>
    /// Best-effort registration of a created project in the store index.
    /// Never fails project creation: the index self-heals via <see cref="BuildStoreIndex"/>.
    /// </summary>
    private void TryRegisterInIndex(string projectPath, ProjectMetadata metadata)
    {
        try
        {
            var root = GetProjectHome();
            if (!projectPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return;

            StoreIndex.Register(root, new StoreProjectEntry
            {
                DirName = Path.GetFileName(projectPath),
                ProjectPath = projectPath,
                PdfPath = metadata.PdfPath,
                PdfFileName = metadata.PdfFileName,
                CreatedAt = metadata.CreatedAt,
                LastModified = metadata.LastModified,
                Legacy = !StorePaths.IsShardedPath(root, projectPath),
                ProjectId = metadata.ProjectId
            });
        }
        catch
        {
            // Index maintenance is best-effort.
        }
    }

    /// <summary>
    /// Best-effort manifest note that creation now lands in the sharded layout.
    /// </summary>
    private void NoteShardedCreation()
    {
        try
        {
            var root = GetProjectHome();
            var manifest = StoreManifest.Load(root);
            if (manifest is not null && !string.Equals(manifest.NewProjectLayout, "sharded", StringComparison.Ordinal))
            {
                manifest.NewProjectLayout = "sharded";
                StoreManifest.Save(root, manifest);
            }
        }
        catch
        {
            // Informational only.
        }
    }

    /// <summary>
    /// Gets the MinerU output directory for a project.
    /// </summary>
    public string GetMinerUOutputDir(string projectPath)
    {
        return Path.Combine(projectPath, "mineru");
    }

    /// <summary>
    /// Gets the Popo output directory for a project.
    /// </summary>
    public string GetPopoOutputDir(string projectPath)
    {
        return Path.Combine(projectPath, "popo");
    }

    /// <summary>
    /// Loads project metadata from a project directory.
    /// </summary>
    public ProjectMetadata? LoadProjectMetadata(string projectPath)
    {
        var metadataPath = Path.Combine(projectPath, "project.json");
        if (!File.Exists(metadataPath))
            return null;

        try
        {
            var json = File.ReadAllText(metadataPath);
            return JsonSerializer.Deserialize(json, SourceGenerationContext.Default.ProjectMetadata);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Updates the LastModified timestamp of a project.
    /// </summary>
    public void UpdateLastModified(string projectPath)
    {
        var metadataPath = Path.Combine(projectPath, "project.json");
        if (!File.Exists(metadataPath))
            return;

        try
        {
            var json = File.ReadAllText(metadataPath);
            var metadata = JsonSerializer.Deserialize(json, SourceGenerationContext.Default.ProjectMetadata);
            if (metadata is not null)
            {
                metadata.LastModified = DateTime.UtcNow;
                File.WriteAllText(metadataPath, JsonSerializer.Serialize(metadata, SourceGenerationContext.Default.ProjectMetadata));
            }
        }
        catch
        {
            // Silently ignore errors updating metadata
        }
    }

    /// <summary>
    /// Gets the PDF path associated with a project directory.
    /// </summary>
    public string? GetPdfPath(string projectPath)
    {
        var metadata = LoadProjectMetadata(projectPath);
        return metadata?.PdfPath;
    }

    /// <summary>
    /// Saves the MinerU images directory path to project metadata.
    /// This caches the discovered path to avoid scanning thousands of files on subsequent opens.
    /// </summary>
    public void SaveMinerUImagesDir(string projectPath, string imagesDir)
    {
        var metadata = LoadProjectMetadata(projectPath);
        if (metadata is null)
            return;

        try
        {
            // Store as relative path if it's under the project directory
            if (imagesDir.StartsWith(projectPath, StringComparison.OrdinalIgnoreCase))
            {
                metadata.MinerUImagesDir = Path.GetRelativePath(projectPath, imagesDir);
            }
            else
            {
                metadata.MinerUImagesDir = imagesDir;
            }

            var metadataPath = Path.Combine(projectPath, "project.json");
            File.WriteAllText(metadataPath, JsonSerializer.Serialize(metadata, SourceGenerationContext.Default.ProjectMetadata));
        }
        catch
        {
            // Silently ignore errors saving metadata
        }
    }

    /// <summary>
    /// Gets the cached MinerU images directory path from project metadata.
    /// Returns null if not cached or if the path doesn't exist.
    /// </summary>
    public string? GetCachedMinerUImagesDir(string projectPath)
    {
        var metadata = LoadProjectMetadata(projectPath);
        if (metadata?.MinerUImagesDir is null)
            return null;

        // Resolve relative paths
        var imagesDir = metadata.MinerUImagesDir;
        if (!Path.IsPathRooted(imagesDir))
        {
            imagesDir = Path.Combine(projectPath, imagesDir);
        }

        // Verify the directory still exists
        if (Directory.Exists(imagesDir))
            return imagesDir;

        return null;
    }
}