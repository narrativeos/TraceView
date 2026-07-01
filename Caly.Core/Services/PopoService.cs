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
    public async Task<string> SubmitTaskAsync(string zipPath, CancellationToken ct = default)
    {
        if (!File.Exists(zipPath))
            throw new PopoServiceException($"ZIP file not found: {zipPath}");

        var zipBytes = File.ReadAllBytes(zipPath);

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(zipBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(fileContent, "file", Path.GetFileName(zipPath));

        using var response = await _httpClient.PostAsync($"{_baseUrl}/tasks", content, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);

        if (string.IsNullOrEmpty(json) || !json.TrimStart().StartsWith("{"))
        {
            var preview = (json?.Length > 200) ? json.Substring(0, 200) + "..." : json;
            throw new PopoServiceException(
                $"Invalid JSON response from Popo /tasks. Response: {preview}");
        }

        var result = JsonSerializer.Deserialize<PopoTaskSubmitResponse>(json);
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

        var result = JsonSerializer.Deserialize<PopoTaskStatusResponse>(json);
        if (result is null)
        {
            throw new PopoServiceException($"JsonSerializer returned null for Popo /tasks/{{id}} response.");
        }

        return result;
    }

    /// <summary>
    /// Polls the task status until it completes or fails.
    /// </summary>
    public async Task PollUntilCompleteAsync(
        string taskId,
        Action<PopoProcessStatus, int>? onProgress = null,
        CancellationToken ct = default)
    {
        int lastProgress = 10;

        while (!ct.IsCancellationRequested)
        {
            var status = await GetTaskStatusAsync(taskId, ct);

            if (status.IsRunning)
            {
                var progress = status.Status == "pending" ? 20 : 50;
                if (progress != lastProgress)
                {
                    lastProgress = progress;
                    onProgress?.Invoke(PopoProcessStatus.Processing, progress);
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
            else
            {
                var progress = 35;
                if (progress != lastProgress)
                {
                    lastProgress = progress;
                    onProgress?.Invoke(PopoProcessStatus.Processing, progress);
                }
            }

            await Task.Delay(DefaultPollInterval, ct);
        }
    }

    /// <summary>
    /// Downloads the result via GET /tasks/{task_id}/result.
    /// </summary>
    public async Task<string> DownloadResultAsync(string taskId, string sourceDocId, CancellationToken ct = default)
    {
        using var response = await _httpClient.GetAsync($"{_baseUrl}/tasks/{taskId}/result", ct);
        response.EnsureSuccessStatusCode();

        var contentBytes = await response.Content.ReadAsByteArrayAsync(ct);

        // Determine if the response is a ZIP file by checking magic bytes
        bool isZip = contentBytes.Length >= 4 &&
                     contentBytes[0] == 0x50 && contentBytes[1] == 0x4B &&
                     contentBytes[2] == 0x03 && contentBytes[3] == 0x04;

        var resultDir = Path.Combine(_cacheDirectory, $"result_{sourceDocId}");
        Directory.CreateDirectory(resultDir);

        if (isZip)
        {
            // Save and extract ZIP
            var zipPath = Path.Combine(resultDir, "popo_result.zip");
            File.WriteAllBytes(zipPath, contentBytes);

            var extractDir = Path.Combine(resultDir, "extract");
            if (Directory.Exists(extractDir))
                Directory.Delete(extractDir, recursive: true);
            Directory.CreateDirectory(extractDir);
            ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);

            return extractDir;
        }
        else
        {
            // Check if it's JSON
            var content = System.Text.Encoding.UTF8.GetString(contentBytes);
            if (content.TrimStart().StartsWith("{") || content.TrimStart().StartsWith("["))
            {
                var jsonPath = Path.Combine(resultDir, "popo_result.json");
                File.WriteAllText(jsonPath, content);
                return resultDir;
            }

            // Save as raw text
            var textPath = Path.Combine(resultDir, "popo_result.txt");
            File.WriteAllText(textPath, content);
            return resultDir;
        }
    }

    /// <summary>
    /// Full processing flow: submit -> poll -> download -> parse.
    /// </summary>
    public async Task<PopoProcessResult> ProcessAsync(
        string zipPath,
        string sourceDocId,
        Action<PopoProcessStatus, int>? onProgress = null,
        CancellationToken ct = default)
    {
        // Step 1: Submit
        onProgress?.Invoke(PopoProcessStatus.Submitting, 10);
        var taskId = await SubmitTaskAsync(zipPath, ct);

        // Step 2: Poll
        onProgress?.Invoke(PopoProcessStatus.Queued, 15);
        await PollUntilCompleteAsync(taskId, onProgress, ct);

        // Step 3: Download
        onProgress?.Invoke(PopoProcessStatus.Downloading, 70);
        var resultDir = await DownloadResultAsync(taskId, sourceDocId, ct);

        // Step 4: Parse
        onProgress?.Invoke(PopoProcessStatus.ParsingResult, 85);
        var popoDoc = PopoJsonService.TryParsePopoResultDir(resultDir);

        // Step 5: Complete
        onProgress?.Invoke(PopoProcessStatus.Completed, 100);

        return new PopoProcessResult
        {
            AnalysisDocument = popoDoc,
            ResultDirectory = resultDir
        };
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
    public AnalysisDocument? AnalysisDocument { get; init; }
    public string ResultDirectory { get; init; } = string.Empty;
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

    public bool IsRunning => Status is "pending" or "running" or "processing";
    public bool IsCompleted => Status == "completed";
    public bool IsFailed => Status == "failed";

    public string? GetErrorMessage() => Error ?? Message;
}