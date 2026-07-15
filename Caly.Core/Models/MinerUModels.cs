// Copyright (c) 2025 BobLd
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;

namespace Caly.Core.Models;

/// <summary>
/// Page type classification from MinerU's page-level analysis.
/// Represents 15 distinct page types identified by MinerU's layout analysis.
/// </summary>
public enum PageType
{
    /// <summary>
    /// Unknown or unspecified page type.
    /// </summary>
    unknown = 0,

    /// <summary>
    /// Cover page (封面) - The front cover of the document.
    /// </summary>
    cover = 1,

    /// <summary>
    /// Half title page (半标题) - A page with the title, usually before the main title page.
    /// </summary>
    half_title = 2,

    /// <summary>
    /// Table of contents (目录) - The index or table of contents page.
    /// </summary>
    toc = 3,

    /// <summary>
    /// Body page (正文) - Regular content page.
    /// </summary>
    body = 4,

    /// <summary>
    /// Chapter start page (章节起始) - The first page of a new chapter.
    /// </summary>
    chapter_start = 5,

    /// <summary>
    /// Image dominant page (图片为主) - A page primarily containing images.
    /// </summary>
    image_dominant = 6,

    /// <summary>
    /// Table dominant page (表格为主) - A page primarily containing tables.
    /// </summary>
    table_dominant = 7,

    /// <summary>
    /// Blank page (空白) - An empty or nearly empty page.
    /// </summary>
    blank = 8,

    /// <summary>
    /// Back cover (封底) - The back cover of the document.
    /// </summary>
    back_cover = 9,

    /// <summary>
    /// Copyright page (版权) - The copyright and publication information page.
    /// </summary>
    copyright = 10,

    /// <summary>
    /// Colophon (版本记录) - Publication history and version information.
    /// </summary>
    colophon = 11,

    /// <summary>
    /// Acknowledgment page (致谢) - Acknowledgments and thanks section.
    /// </summary>
    acknowledgment = 12,

    /// <summary>
    /// Appendix (附录) - Supplementary material at the end of the document.
    /// </summary>
    appendix = 13,

    /// <summary>
    /// Glossary (术语表) - A list of terms and their definitions.
    /// </summary>
    glossary = 14,

    /// <summary>
    /// Reference page (参考文献) - Bibliography and references section.
    /// </summary>
    reference = 15
}

/// <summary>
/// Response from POST /tasks endpoint (MinerU v3.4.0+).
/// </summary>
public class MinerUTaskSubmitResponse
{
    [JsonPropertyName("task_id")]
    public string TaskId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("backend")]
    public string? Backend { get; set; }

    [JsonPropertyName("file_names")]
    public List<string>? FileNames { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("started_at")]
    public string? StartedAt { get; set; }

    [JsonPropertyName("completed_at")]
    public string? CompletedAt { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("status_url")]
    public string? StatusUrl { get; set; }

    [JsonPropertyName("result_url")]
    public string? ResultUrl { get; set; }

    [JsonPropertyName("queued_ahead")]
    public int? QueuedAhead { get; set; }
}

/// <summary>
/// Progress information returned by MinerU during task processing.
/// </summary>
public class MinerUTaskProgress
{
    /// <summary>
    /// Progress percentage (0-100).
    /// </summary>
    [JsonPropertyName("percent")]
    public int Percent { get; set; }

    /// <summary>
    /// Current page being processed (1-based).
    /// </summary>
    [JsonPropertyName("current_page")]
    public int CurrentPage { get; set; }

    /// <summary>
    /// Total number of pages in the document.
    /// </summary>
    [JsonPropertyName("total_pages")]
    public int TotalPages { get; set; }

    /// <summary>
    /// Current processing stage (e.g., "text_recognition", "layout_analysis").
    /// </summary>
    [JsonPropertyName("stage")]
    public string? Stage { get; set; }

    /// <summary>
    /// Gets a user-friendly display string for the progress.
    /// </summary>
    public string ToDisplayString()
    {
        var parts = new List<string>();

        if (!string.IsNullOrEmpty(Stage))
        {
            // Convert snake_case to Title Case
            var stageDisplay = string.Join(" ",
                Stage.Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(w => char.ToUpper(w[0]) + w.Substring(1).ToLower()));
            parts.Add(stageDisplay);
        }

        if (TotalPages > 0)
        {
            parts.Add($"Page {CurrentPage}/{TotalPages}");
        }

        return string.Join(" | ", parts);
    }
}

/// <summary>
/// Response from GET /tasks/{task_id} endpoint (MinerU v3.4.0+).
/// May include a "progress" field with detailed progress information.
/// </summary>
public class MinerUTaskStatusResponse
{
    [JsonPropertyName("task_id")]
    public string TaskId { get; set; } = string.Empty;

    /// <summary>
    /// Task status: pending, running/processing, completed, failed
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("backend")]
    public string? Backend { get; set; }

    [JsonPropertyName("file_names")]
    public List<string>? FileNames { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("started_at")]
    public string? StartedAt { get; set; }

    [JsonPropertyName("completed_at")]
    public string? CompletedAt { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("progress")]
    public MinerUTaskProgress? Progress { get; set; }

    [JsonPropertyName("status_url")]
    public string? StatusUrl { get; set; }

    [JsonPropertyName("result_url")]
    public string? ResultUrl { get; set; }

    [JsonPropertyName("queued_ahead")]
    public int? QueuedAhead { get; set; }

    /// <summary>
    /// Optional message (present in some responses).
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>
    /// Determines if the task is still running (pending, running, or processing).
    /// </summary>
    public bool IsRunning => Status is "pending" or "running" or "processing";

    /// <summary>
    /// Determines if the task is completed successfully.
    /// </summary>
    public bool IsCompleted => Status == "completed";

    /// <summary>
    /// Determines if the task has failed.
    /// </summary>
    public bool IsFailed => Status == "failed";

    /// <summary>
    /// Gets the error message if the task failed.
    /// </summary>
    public string? GetErrorMessage() => Error ?? Message;
}

/// <summary>
/// Final result of a MinerU parse operation.
/// Contains the parsed StructureDocument and local file paths.
/// </summary>
public class MinerUParseResult
{
    /// <summary>
    /// The structured MinerU document model built from MinerU output.
    /// </summary>
    public StructureDocument? StructureDocument { get; init; }

    /// <summary>
    /// Local path to the cached zip file from MinerU.
    /// </summary>
    public string ZipPath { get; init; } = string.Empty;

    /// <summary>
    /// Markdown content extracted from the parse result (if available).
    /// </summary>
    public string? Markdown { get; init; }

    /// <summary>
    /// Popo-enhanced Markdown content (if Popo postprocessing was applied).
    /// </summary>
    public string? PopoMarkdown { get; init; }

    /// <summary>
    /// Local path to the extracted artifacts directory (images, etc.).
    /// </summary>
    public string? ArtifactsDirectory { get; init; }
}