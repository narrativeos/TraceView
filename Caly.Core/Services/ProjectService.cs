using System;
using System.IO;
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
        Directory.CreateDirectory(minerUDir);
        Directory.CreateDirectory(popoDir);

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