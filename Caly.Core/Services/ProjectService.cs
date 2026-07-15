using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Caly.Core.Models;
using Caly.Core.Services.Interfaces;

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
    }

    /// <summary>
    /// Gets the ProjectHome directory from settings.
    /// </summary>
    public string GetProjectHome()
    {
        var settings = _settingsService.GetSettings();
        return settings.ProjectHome;
    }

    /// <summary>
    /// Gets the default project path for a given PDF file.
    /// Format: {ProjectHome}/{PDFFileNameWithoutExtension}/
    /// </summary>
    public string GetDefaultProjectPath(string pdfPath)
    {
        var projectHome = GetProjectHome();
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(pdfPath);
        return Path.Combine(projectHome, fileNameWithoutExtension);
    }

    /// <summary>
    /// Checks if a project directory already exists for the given PDF.
    /// </summary>
    public bool ProjectExists(string pdfPath)
    {
        var projectPath = GetDefaultProjectPath(pdfPath);
        return Directory.Exists(projectPath);
    }

    /// <summary>
    /// Finds all existing project directories for a given PDF file.
    /// Projects follow the naming pattern: {basename}, {basename}_2, {basename}_3, etc.
    /// Returns a list of full project paths that have valid project.json metadata matching the PDF.
    /// </summary>
    public List<string> FindAllProjects(string pdfPath)
    {
        var projectHome = GetProjectHome();
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(pdfPath);
        var results = new List<string>();

        if (!Directory.Exists(projectHome))
            return results;

        // Find all directories that match the pattern: {basename} or {basename}_N
        var dirs = Directory.GetDirectories(projectHome, fileNameWithoutExtension + "*");
        foreach (var dir in dirs)
        {
            var dirName = Path.GetFileName(dir);
            // Must match exactly {basename} or {basename}_N pattern
            if (dirName == fileNameWithoutExtension || 
                (dirName.StartsWith(fileNameWithoutExtension + "_") && IsNumeric(dirName.Substring(fileNameWithoutExtension.Length + 1))))
            {
                // Verify it's a valid project by checking for project.json
                var metadataPath = Path.Combine(dir, "project.json");
                if (File.Exists(metadataPath))
                {
                    var metadata = LoadProjectMetadata(dir);
                    // Verify the project.json references the same PDF file
                    if (metadata is not null && string.Equals(metadata.PdfPath, pdfPath, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(dir);
                    }
                }
            }
        }

        // Sort: base name first, then by numeric suffix
        results.Sort((a, b) =>
        {
            var nameA = Path.GetFileName(a);
            var nameB = Path.GetFileName(b);
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
        var basePath = GetDefaultProjectPath(pdfPath);
        if (!Directory.Exists(basePath))
            return basePath;

        var directory = Path.GetDirectoryName(basePath)!;
        var fileName = Path.GetFileName(basePath);
        var counter = 2;

        while (true)
        {
            var newPath = Path.Combine(directory, $"{fileName}_{counter}");
            if (!Directory.Exists(newPath))
                return newPath;
            counter++;
        }
    }

    /// <summary>
    /// Creates a new project directory with the standard folder structure.
    /// Returns the project path.
    /// </summary>
    public string CreateProject(string pdfPath, string? projectPath = null)
    {
        projectPath ??= GetDefaultProjectPath(pdfPath);

        // Create main project directory
        Directory.CreateDirectory(projectPath);

        // Create subdirectories
        var minerUDir = Path.Combine(projectPath, "mineru");
        var popoDir = Path.Combine(projectPath, "popo");
        var semanticDir = Path.Combine(projectPath, "semantic");
        Directory.CreateDirectory(minerUDir);
        Directory.CreateDirectory(popoDir);
        Directory.CreateDirectory(semanticDir);

        // Create project metadata
        var metadata = new ProjectMetadata
        {
            PdfPath = pdfPath,
            PdfFileName = Path.GetFileName(pdfPath),
            ProjectName = Path.GetFileName(projectPath),
            ProjectPath = projectPath,
            CreatedAt = DateTime.UtcNow
        };

        var metadataPath = Path.Combine(projectPath, "project.json");
        File.WriteAllText(metadataPath, JsonSerializer.Serialize(metadata, SourceGenerationContext.Default.ProjectMetadata));

        return projectPath;
    }

    /// <summary>
    /// Ensures a project exists for the given PDF. If it doesn't exist, creates one.
    /// </summary>
    public string EnsureProject(string pdfPath)
    {
        var projectPath = GetDefaultProjectPath(pdfPath);
        if (!Directory.Exists(projectPath))
        {
            return CreateProject(pdfPath, projectPath);
        }
        return projectPath;
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
}