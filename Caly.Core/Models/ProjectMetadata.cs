using System;

namespace Caly.Core.Models;

/// <summary>
/// Metadata for a TraceView project, stored in project.json within each project directory.
/// </summary>
public class ProjectMetadata
{
    /// <summary>
    /// Original PDF file path.
    /// </summary>
    public string PdfPath { get; set; } = string.Empty;

    /// <summary>
    /// Original PDF file name (with extension).
    /// </summary>
    public string PdfFileName { get; set; } = string.Empty;

    /// <summary>
    /// Project name (folder name under ProjectHome).
    /// </summary>
    public string ProjectName { get; set; } = string.Empty;

    /// <summary>
    /// Project directory path.
    /// </summary>
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>
    /// Project creation timestamp.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last modification timestamp.
    /// </summary>
    public DateTime? LastModified { get; set; }
}