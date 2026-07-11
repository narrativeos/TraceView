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
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Caly.Core.Models;

namespace Caly.Core.Services;

/// <summary>
/// HTTP client for the Popo processing service.
/// Supports async task-based processing via /tasks endpoint.
/// </summary>
public sealed class PopoService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _cacheDirectory;

    /// <summary>
    /// Default Popo API base URL.
    /// </summary>
    public const string DefaultBaseUrl = "http://localhost:8440";

    /// <summary>
    /// Polling interval for async task status checks.
    /// </summary>
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Creates a new PopoService instance.
    /// </summary>
    /// <param name="baseUrl">Popo API base URL (default: http://localhost:8440).</param>
    /// <param name="cacheDirectory">Local directory for caching results.</param>
    public PopoService(string? baseUrl = null, string? cacheDirectory = null)
    {
        _baseUrl = baseUrl ?? DefaultBaseUrl;
        _cacheDirectory = cacheDirectory ?? GetDefaultCacheDirectory();

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(30)
        };

        Directory.CreateDirectory(_cacheDirectory);
    }

    private static string GetDefaultCacheDirectory()
    {
        var baseCache = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(baseCache, "Caly", "popo");
    }

    #region Health Check

    /// <summary>
    /// Checks if the Popo service is reachable.
    /// </summary>
    public async Task<bool> HealthCheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync($"{_baseUrl}/health", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region Async Task Processing

    /// <summary>
    /// Submits a MinerU ZIP file to Popo for processing via POST /tasks.
    /// Returns the task ID for status polling.
    /// </summary>
    /// <param name="zipPath">Path to the MinerU ZIP file.</param>
    /// <param name="model">Model name (e.g., "mineru", "monkeyocr"). Required by Popo API.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<string> SubmitTaskAsync(string zipPath, string model, CancellationToken ct = default)
    {
        if (!File.Exists(zipPath))
            throw new PopoServiceException($"ZIP file not found: {zipPath}");

        var zipBytes = File.ReadAllBytes(zipPath);

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(zipBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(fileContent, "file", Path.GetFileName(zipPath));
        content.Add(new StringContent(model), "model");

        using var response = await _httpClient.PostAsync($"{_baseUrl}/tasks", content, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);

        if (string.IsNullOrEmpty(json) || !json.TrimStart().StartsWith("{"))
        {
            var preview = (json?.Length > 200) ? json.Substring(0, 200) + "..." : json;
            throw new PopoServiceException(
                $"Invalid JSON response from Popo /tasks. Response: {preview}");
        }

        var result = JsonSerializer.Deserialize<PopoTaskSubmitResponse>(json, PopoJsonService.DefaultDeserializeOptions);
        if (result is null || string.IsNullOrEmpty(result.TaskId))
        {
            throw new PopoServiceException(
                $"Failed to get task ID from Popo response. Raw: {json?.Substring(0, Math.Min(500, json.Length))}");
        }

        return result.TaskId;
    }

    /// <summary>
    /// Gets the current status of a Popo task via GET /tasks/{task_id}.
    /// </summary>
    public async Task<PopoTaskStatusResponse> GetTaskStatusAsync(string taskId, CancellationToken ct = default)
    {
        using var response = await _httpClient.GetAsync($"{_baseUrl}/tasks/{taskId}", ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);

        if (string.IsNullOrEmpty(json) || !json.TrimStart().StartsWith("{"))
        {
            var preview = (json?.Length > 200) ? json.Substring(0, 200) + "..." : json;
            throw new PopoServiceException(
                $"Invalid JSON response from Popo /tasks/{{id}}. Response: {preview}");
        }

        var result = JsonSerializer.Deserialize<PopoTaskStatusResponse>(json, PopoJsonService.DefaultDeserializeOptions);
        if (result is null)
        {
            throw new PopoServiceException($"JsonSerializer returned null for Popo /tasks/{{id}} response.");
        }

        return result;
    }

    /// <summary>
    /// Polls the task status until it completes or fails.
    /// Uses the actual progress percentage from the API's progress field.
    /// </summary>
    public async Task PollUntilCompleteAsync(
        string taskId,
        Action<PopoProcessStatus, int>? onProgress = null,
        CancellationToken ct = default)
    {
        int lastProgress = -1;

        while (!ct.IsCancellationRequested)
        {
            var status = await GetTaskStatusAsync(taskId, ct);

            if (status.IsRunning)
            {
                // Parse actual progress percentage from API response
                // Format: "[60%] Image-text association (1 chunks)"
                var apiProgress = status.ParseProgressPercent() ?? 35;
                if (apiProgress != lastProgress)
                {
                    lastProgress = apiProgress;
                    onProgress?.Invoke(PopoProcessStatus.Processing, apiProgress);
                }
            }
            else if (status.IsCompleted)
            {
                onProgress?.Invoke(PopoProcessStatus.Downloading, 70);
                return;
            }
            else if (status.IsFailed)
            {
                onProgress?.Invoke(PopoProcessStatus.Failed, -1);
                var errorMessage = status.GetErrorMessage() ?? "Unknown error";
                throw new PopoServiceException($"Popo task failed: {errorMessage}");
            }

            await Task.Delay(DefaultPollInterval, ct);
        }
    }

    /// <summary>
    /// Downloads and parses the result via GET /tasks/{task_id}/result.
    /// The Popo API returns JSON in the format: { task_id, status, result: { doc_id, tree: {...} } }.
    /// Parses the tree and builds a MinerUDocument directly.
    /// </summary>
    public async Task<StructureDocument?> DownloadAndParseResultAsync(string taskId, string sourceDocId, CancellationToken ct = default)
    {
        using var response = await _httpClient.GetAsync($"{_baseUrl}/tasks/{taskId}/result", ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);

        if (string.IsNullOrEmpty(json) || !json.TrimStart().StartsWith("{"))
        {
            var preview = (json?.Length > 200) ? json.Substring(0, 200) + "..." : json;
            throw new PopoServiceException(
                $"Invalid JSON response from Popo /tasks/{{id}}/result. Response: {preview}");
        }

        // Cache the raw response for debugging
        var resultDir = Path.Combine(_cacheDirectory, $"result_{sourceDocId}");
        Directory.CreateDirectory(resultDir);
        var jsonPath = Path.Combine(resultDir, "popo_result.json");
        File.WriteAllText(jsonPath, json);

        // Parse the wrapped response
        // Use StructureDocumentOptions (reflection-based) because AnalysisTreeNode also uses
        // [ObservableProperty] — the JSON source generator can't see its generated properties.
        var resultResponse = JsonSerializer.Deserialize<PopoTaskResultResponse>(json, PopoJsonService.StructureDocumentOptions);
        if (resultResponse?.Result?.Tree is null)
        {
            throw new PopoServiceException(
                $"Popo result missing tree. Status: {resultResponse?.Result?.Status}, Error: {resultResponse?.Error}");
        }

        // Build StructureDocument from the API tree
        var minerUDoc = PopoJsonService.BuildStructureDocumentFromTree(
            resultResponse.Result.Tree,
            sourceDocId,
            resultResponse.TaskId);

        return minerUDoc;
    }

    /// <summary>
    /// Full processing flow: submit -> poll -> download result -> build StructureDocument.
    /// </summary>
    /// <param name="zipPath">Path to the MinerU ZIP file.</param>
    /// <param name="sourceDocId">Document ID for result caching.</param>
    /// <param name="model">Model name (e.g., "mineru"). Required by Popo API.</param>
    /// <param name="onProgress">Progress callback (status, percent 0-100).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<PopoProcessResult> ProcessAsync(
        string zipPath,
        string sourceDocId,
        string model = "mineru",
        Action<PopoProcessStatus, int>? onProgress = null,
        CancellationToken ct = default)
    {
        // Step 1: Submit task
        onProgress?.Invoke(PopoProcessStatus.Submitting, 10);
        var taskId = await SubmitTaskAsync(zipPath, model, ct);

        // Step 2: Poll until complete (progress from API parsed automatically)
        onProgress?.Invoke(PopoProcessStatus.Queued, 15);
        await PollUntilCompleteAsync(taskId, onProgress, ct);

        // Step 3: Download and parse result (builds MinerUDocument from the API tree)
        onProgress?.Invoke(PopoProcessStatus.Downloading, 70);
        var minerUDoc = await DownloadAndParseResultAsync(taskId, sourceDocId, ct);

        onProgress?.Invoke(PopoProcessStatus.ParsingResult, 85);

        // Step 4: Use MinerU extracted directory as the image source instead of re-extracting from ZIP.
        // The popo_result.json contains img_path like "images/xxx.jpg", which is relative to the
        // hybrid_auto directory. By pointing to the MinerU extract directory, we avoid duplicating
        // image files and save disk space.
        // Directory structure: ~/.TraceView/{docId}/mineru/{docId}/hybrid_auto/images/
        var artifactsDir = FindMinerUImagesDirectory(sourceDocId);

        onProgress?.Invoke(PopoProcessStatus.Completed, 100);

        return new PopoProcessResult
        {
            StructureDocument = minerUDoc,
            ArtifactsDirectory = artifactsDir
        };
    }

    #endregion

    #region MinerU Images Directory

    /// <summary>
    /// Finds the MinerU extracted images directory for a given document ID.
    /// The directory structure is: ~/.TraceView/{docId}/mineru/{docId}/hybrid_auto/
    /// The ZIP contains {docId}/hybrid_auto/... which extracts to {mineru}/{docId}/hybrid_auto/...
    /// This is the directory where popo_result.json's img_path (e.g., "images/xxx.jpg") is relative to.
    /// </summary>
    private string? FindMinerUImagesDirectory(string docId)
    {
        try
        {
            var cacheBase = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".TraceView", docId);
            var mineruCacheDir = Path.Combine(cacheBase, "mineru");

            if (!Directory.Exists(mineruCacheDir))
                return null;

            // The ZIP extracts {docId}/hybrid_auto/ to {mineru}/{docId}/hybrid_auto/
            var artifactsDir = Path.Combine(mineruCacheDir, docId);

            if (!Directory.Exists(artifactsDir))
                return null;

            var hybridAutoDir = Path.Combine(artifactsDir, "hybrid_auto");

            if (Directory.Exists(hybridAutoDir))
            {
                // Verify there are actually images in the images subdirectory
                var imagesDir = Path.Combine(hybridAutoDir, "images");
                if (Directory.Exists(imagesDir))
                {
                    var imageFilesInHybrid = Directory.GetFiles(imagesDir, "*.*", SearchOption.TopDirectoryOnly)
                        .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg")
                        .ToList();
                    if (imageFilesInHybrid.Count > 0)
                        return hybridAutoDir;
                }
            }

            // Fallback: search the entire artifacts directory for images
            var imageFiles = Directory.GetFiles(artifactsDir, "*.*", SearchOption.AllDirectories)
                .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg")
                .ToList();
            if (imageFiles.Count > 0)
                return artifactsDir;
        }
        catch
        {
            // Ignore errors finding the MinerU images directory
        }

        return null;
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    #endregion
}

/// <summary>
/// Exception thrown when a Popo service operation fails.
/// </summary>
public class PopoServiceException : Exception
{
    public PopoServiceException(string message) : base(message) { }
    public PopoServiceException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Processing stages for Popo tasks.
/// </summary>
public enum PopoProcessStatus
{
    Idle,
    Submitting,
    Queued,
    Processing,
    Downloading,
    ParsingResult,
    Completed,
    Failed
}

public static class PopoProcessStatusExtensions
{
    public static int ToProgressPercent(this PopoProcessStatus status)
    {
        return status switch
        {
            PopoProcessStatus.Idle => 0,
            PopoProcessStatus.Submitting => 10,
            PopoProcessStatus.Queued => 15,
            PopoProcessStatus.Processing => 35,
            PopoProcessStatus.Downloading => 70,
            PopoProcessStatus.ParsingResult => 85,
            PopoProcessStatus.Completed => 100,
            PopoProcessStatus.Failed => -1,
            _ => 0
        };
    }

    public static string ToDisplayName(this PopoProcessStatus status)
    {
        return status switch
        {
            PopoProcessStatus.Idle => "Ready",
            PopoProcessStatus.Submitting => "Uploading to Popo...",
            PopoProcessStatus.Queued => "Waiting in queue...",
            PopoProcessStatus.Processing => "Popo processing...",
            PopoProcessStatus.Downloading => "Downloading results...",
            PopoProcessStatus.ParsingResult => "Building document structure...",
            PopoProcessStatus.Completed => "Popo completed",
            PopoProcessStatus.Failed => "Popo failed",
            _ => "Unknown"
        };
    }

    public static bool IsTerminal(this PopoProcessStatus status)
    {
        return status is PopoProcessStatus.Completed or PopoProcessStatus.Failed;
    }
}

/// <summary>
/// Result of a Popo processing operation.
/// </summary>
public class PopoProcessResult
{
    public StructureDocument? StructureDocument { get; init; }
    
    /// <summary>
    /// Local path to the extracted artifacts directory (images, etc.).
    /// </summary>
    public string? ArtifactsDirectory { get; init; }
}

/// <summary>
/// Response from POST /tasks endpoint.
/// </summary>
public class PopoTaskSubmitResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("task_id")]
    public string TaskId { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("status")]
    public string? Status { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("message")]
    public string? Message { get; set; }
}

/// <summary>
/// Response from GET /tasks/{task_id} endpoint.
/// </summary>
public class PopoTaskStatusResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("task_id")]
    public string TaskId { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("error")]
    public string? Error { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("message")]
    public string? Message { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("result_url")]
    public string? ResultUrl { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("progress")]
    public string? Progress { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("doc_id")]
    public string? DocId { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("model")]
    public string? Model { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("updated_at")]
    public string? UpdatedAt { get; set; }

    /// <summary>
    /// Parses the percentage from the progress string like "[60%] Image-text association (1 chunks)".
    /// Returns null if no percentage can be parsed.
    /// </summary>
    public int? ParseProgressPercent()
    {
        if (string.IsNullOrEmpty(Progress))
            return null;

        // Match pattern: [XX%] at the start of the string
        var match = System.Text.RegularExpressions.Regex.Match(Progress, @"^\[(\d+)%\]");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var percent))
            return percent;

        return null;
    }

    public bool IsRunning => !IsCompleted && !IsFailed
        && Status is "pending" or "processing" or "running" or "queued" or "in_progress" or "started";

    public bool IsCompleted => Status is "completed" or "success" or "done" or "finished";

    public bool IsFailed => Status is "failed" or "error" or "cancelled";

    public string? GetErrorMessage() => Error ?? Message;
}

/// <summary>
/// Response from GET /tasks/{task_id}/result endpoint.
/// Wraps the actual processing result in a "result" field.
/// </summary>
public class PopoTaskResultResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("task_id")]
    public string TaskId { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("result")]
    public PopoProcessResultJson? Result { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>
/// The inner result object from a Popo task result.
/// Contains the document tree.
/// </summary>
public class PopoProcessResultJson
{
    [System.Text.Json.Serialization.JsonPropertyName("doc_id")]
    public string DocId { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("message")]
    public string? Message { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("tree")]
    public Caly.Core.Models.AnalysisTreeNode? Tree { get; set; }
}