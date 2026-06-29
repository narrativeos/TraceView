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

using System.Text.Json.Serialization;

namespace Caly.Core.Models;

/// <summary>
/// Response from POST /tasks endpoint.
/// </summary>
public class MinerUTaskSubmitResponse
{
    [JsonPropertyName("task_id")]
    public string TaskId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

/// <summary>
/// Response from GET /tasks/{task_id} endpoint.
/// </summary>
public class MinerUTaskStatusResponse
{
    [JsonPropertyName("task_id")]
    public string TaskId { get; set; } = string.Empty;

    /// <summary>
    /// Task status: pending, running, completed, failed
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Progress percentage (0-100), provided by MinerU if available.
    /// </summary>
    [JsonPropertyName("progress")]
    public int? Progress { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public string? UpdatedAt { get; set; }

    /// <summary>
    /// Determines if the task is still running (pending or running).
    /// </summary>
    public bool IsRunning => Status is "pending" or "running";

    /// <summary>
    /// Determines if the task is completed successfully.
    /// </summary>
    public bool IsCompleted => Status == "completed";

    /// <summary>
    /// Determines if the task has failed.
    /// </summary>
    public bool IsFailed => Status == "failed";
}

/// <summary>
/// Final result of a MinerU parse operation.
/// Contains the parsed PopoDocument and local file paths.
/// </summary>
public class MinerUParseResult
{
    /// <summary>
    /// The structured Popo document model built from MinerU output.
    /// </summary>
    public PopoDocument? PopoDocument { get; init; }

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