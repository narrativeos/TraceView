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
/// Represents 20 distinct page types identified by MinerU's layout analysis (v2.2).
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
    reference = 15,

    /// <summary>
    /// Preface page (序言) - The preface of the document.
    /// </summary>
    preface = 16,

    /// <summary>
    /// Title page (扉页) - The standard title page (distinct from cover),
    /// typically containing the standard name, standard number (GB/JB etc.)
    /// and structured fields such as approving department and effective date.
    /// Added in v2.2.
    /// </summary>
    title_page = 17,

    /// <summary>
    /// Announcement page (发布公告) - The announcement issued by the
    /// competent authority to publish/approve a standard,
    /// e.g. "住房城乡建设部公告 第1093号".
    /// Added in v2.2.
    /// </summary>
    announcement = 18,

    /// <summary>
    /// Publication info page (出版信息) - The publication information page of a standard
    /// (distinct from book copyright page), containing publisher, address,
    /// print run, price, website and other metadata.
    /// Added in v2.2.
    /// </summary>
    publication_info = 19,

    /// <summary>
    /// Foreword page (前言) - The foreword and its continuation pages
    /// (revision notes, drafting units, etc.), extending until the TOC
    /// or the start of the main body.
    /// Added in v2.2.
    /// </summary>
    foreword = 20,

    /// <summary>
    /// Normative references page (引用标准) - The "normative references" chapter
    /// (distinct from bibliography/reference).
    /// Added in v2.2.
    /// </summary>
    normative_references = 21
}

/// <summary>
/// Response from POST /v1/uploads and POST /v1/uploads/{id}/complete (MinerU 4.0 V1 API).
/// When the sha256sum hits an already-stored blob, status is "completed" and the
/// <see cref="File"/> object is embedded directly (byte upload and complete steps are skipped).
/// </summary>
public class MinerUUploadResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("bytes")]
    public long Bytes { get; set; }

    [JsonPropertyName("filename")]
    public string? Filename { get; set; }

    [JsonPropertyName("mime_type")]
    public string? MimeType { get; set; }

    [JsonPropertyName("sha256sum")]
    public string? Sha256Sum { get; set; }

    /// <summary>
    /// Upload status: pending, completed, cancelled, expired.
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// URL to PUT the raw bytes to (present while status is "pending").
    /// May be relative (resolved against the API base URL) or an absolute presigned URL.
    /// </summary>
    [JsonPropertyName("upload_url")]
    public string? UploadUrl { get; set; }

    /// <summary>
    /// HTTP method for the byte upload (always "PUT" for self-hosted services).
    /// </summary>
    [JsonPropertyName("upload_method")]
    public string? UploadMethod { get; set; }

    /// <summary>
    /// Extra headers to send verbatim with the byte upload request.
    /// </summary>
    [JsonPropertyName("upload_headers")]
    public Dictionary<string, string>? UploadHeaders { get; set; }

    /// <summary>
    /// The created file object (present when status is "completed").
    /// </summary>
    [JsonPropertyName("file")]
    public MinerUFileObject? File { get; set; }
}

/// <summary>
/// A file resource in the MinerU 4.0 V1 API (source file or parse output).
/// </summary>
public class MinerUFileObject
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("bytes")]
    public long Bytes { get; set; }

    [JsonPropertyName("filename")]
    public string? Filename { get; set; }

    [JsonPropertyName("purpose")]
    public string? Purpose { get; set; }

    [JsonPropertyName("sha256sum")]
    public string? Sha256Sum { get; set; }
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
/// Response from POST /v1/parse/jobs and GET /v1/parse/jobs/{job_id} (MinerU 4.0 V1 API).
/// </summary>
public class MinerUJobResponse
{
    [JsonPropertyName("job_id")]
    public string JobId { get; set; } = string.Empty;

    /// <summary>
    /// Job status: queued, running, completed, partial, failed, canceled.
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("tier")]
    public string? Tier { get; set; }

    [JsonPropertyName("output_formats")]
    public List<string>? OutputFormats { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("started_at")]
    public string? StartedAt { get; set; }

    [JsonPropertyName("finished_at")]
    public string? FinishedAt { get; set; }

    /// <summary>
    /// File-level progress counters (the V1 API does not report page-level progress).
    /// </summary>
    [JsonPropertyName("progress")]
    public MinerUJobProgress? Progress { get; set; }

    [JsonPropertyName("files")]
    public List<MinerUJobFileResult> Files { get; set; } = new List<MinerUJobFileResult>();

    /// <summary>
    /// Determines if the job is still in flight (queued or running).
    /// </summary>
    public bool IsRunning => Status is "queued" or "running";

    /// <summary>
    /// Determines if the job completed with all files successful.
    /// </summary>
    public bool IsCompleted => Status == "completed";

    /// <summary>
    /// Determines if the job finished with some files successful and some failed.
    /// </summary>
    public bool IsPartial => Status == "partial";

    /// <summary>
    /// Determines if the job failed.
    /// </summary>
    public bool IsFailed => Status == "failed";

    /// <summary>
    /// Determines if the job was canceled.
    /// </summary>
    public bool IsCanceled => Status == "canceled";

    /// <summary>
    /// Determines if the job reached a terminal state
    /// (completed, partial, failed or canceled).
    /// </summary>
    public bool IsTerminal => Status is "completed" or "partial" or "failed" or "canceled";

    /// <summary>
    /// Gets the first per-file error message, if any file failed.
    /// </summary>
    public string? GetErrorMessage() =>
        Files.FirstOrDefault(f => f.Error is not null)?.Error?.Message;
}

/// <summary>
/// File-level progress counters reported by the V1 parse job API.
/// </summary>
public class MinerUJobProgress
{
    [JsonPropertyName("completed")]
    public int Completed { get; set; }

    [JsonPropertyName("failed")]
    public int Failed { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }
}

/// <summary>
/// Per-file result within a V1 parse job.
/// </summary>
public class MinerUJobFileResult
{
    [JsonPropertyName("file_id")]
    public string? FileId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("page_range")]
    public string? PageRange { get; set; }

    /// <summary>
    /// Per-file status: queued, running, completed, failed.
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Requested output artifacts. Content is downloaded via GET /v1/files/{file_id}/content.
    /// </summary>
    [JsonPropertyName("output_files")]
    public MinerUOutputFiles? OutputFiles { get; set; }

    [JsonPropertyName("error")]
    public MinerUErrorDetail? Error { get; set; }
}

/// <summary>
/// Output artifact references for a single file in a V1 parse job.
/// </summary>
public class MinerUOutputFiles
{
    [JsonPropertyName("markdown")]
    public MinerUOutputFileRef? Markdown { get; set; }

    [JsonPropertyName("middle_json")]
    public MinerUOutputFileRef? MiddleJson { get; set; }

    [JsonPropertyName("structured_content")]
    public MinerUOutputFileRef? StructuredContent { get; set; }

    [JsonPropertyName("layout_quality")]
    public MinerUOutputFileRef? LayoutQuality { get; set; }

    [JsonPropertyName("zip")]
    public MinerUOutputFileRef? Zip { get; set; }
}

/// <summary>
/// Reference to a downloadable output file (fetched via GET /v1/files/{file_id}/content).
/// </summary>
public class MinerUOutputFileRef
{
    [JsonPropertyName("file_id")]
    public string FileId { get; set; } = string.Empty;

    [JsonPropertyName("bytes")]
    public long Bytes { get; set; }
}

/// <summary>
/// Error detail attached to a failed V1 job or file.
/// </summary>
public class MinerUErrorDetail
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

/// <summary>
/// Request body for POST /v1/uploads (MinerU 4.0 V1 API).
/// </summary>
public class MinerUUploadCreateRequest
{
    [JsonPropertyName("filename")]
    public string Filename { get; set; } = string.Empty;

    [JsonPropertyName("bytes")]
    public long Bytes { get; set; }

    [JsonPropertyName("mime_type")]
    public string? MimeType { get; set; }

    [JsonPropertyName("purpose")]
    public string? Purpose { get; set; }

    [JsonPropertyName("sha256sum")]
    public string? Sha256Sum { get; set; }
}

/// <summary>
/// Request body for POST /v1/uploads/{id}/complete (MinerU 4.0 V1 API).
/// </summary>
public class MinerUUploadCompleteRequest
{
    [JsonPropertyName("sha256sum")]
    public string? Sha256Sum { get; set; }
}

/// <summary>
/// Request body for POST /v1/parse/jobs (MinerU 4.0 V1 API).
/// </summary>
public class MinerUParseJobRequest
{
    [JsonPropertyName("files")]
    public List<MinerUParseJobFile> Files { get; set; } = new();

    [JsonPropertyName("tier")]
    public string? Tier { get; set; }

    [JsonPropertyName("output_formats")]
    public List<string>? OutputFormats { get; set; }
}

/// <summary>
/// A single file entry in a MinerU 4.0 V1 parse job request.
/// </summary>
public class MinerUParseJobFile
{
    [JsonPropertyName("source")]
    public MinerUParseJobFileSource? Source { get; set; }
}

/// <summary>
/// The source reference for a file in a MinerU 4.0 V1 parse job request.
/// </summary>
public class MinerUParseJobFileSource
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("file_id")]
    public string? FileId { get; set; }
}

/// <summary>
/// Persisted task ID record (saved to {docId}_task_id.json for resume support).
/// </summary>
public class MinerUTaskIdRecord
{
    [JsonPropertyName("taskId")]
    public string? TaskId { get; set; }

    [JsonPropertyName("pdfPath")]
    public string? PdfPath { get; set; }

    [JsonPropertyName("createdAt")]
    public string? CreatedAt { get; set; }
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