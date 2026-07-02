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
/// Contains the parsed MinerUDocument and local file paths.
/// </summary>
public class MinerUParseResult
{
    /// <summary>
    /// The structured MinerU document model built from MinerU output.
    /// </summary>
    public MinerUDocument? MinerUDocument { get; init; }

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