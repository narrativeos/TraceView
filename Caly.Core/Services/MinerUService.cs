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
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Caly.Core.Models;
using Caly.Core.Utilities;

namespace Caly.Core.Services;

/// <summary>
/// HTTP client for the MinerU external parsing service.
/// Supports both sync (/file_parse) and async (/tasks) modes.
/// </summary>
public sealed class MinerUService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _cacheDirectory;

    /// <summary>
    /// Default MinerU API base URL.
    /// </summary>
    public const string DefaultBaseUrl = "http://localhost:8401";

    /// <summary>
    /// Default parse backend.
    /// </summary>
    public const string DefaultBackend = "hybrid-engine";

    /// <summary>
    /// Polling interval for async task status checks.
    /// </summary>
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Creates a new MinerUService instance.
    /// </summary>
    /// <param name="baseUrl">MinerU API base URL (default: http://localhost:8401).</param>
    /// <param name="cacheDirectory">Local directory for caching parse results.</param>
    public MinerUService(string? baseUrl = null, string? cacheDirectory = null)
    {
        _baseUrl = baseUrl ?? DefaultBaseUrl;
        _cacheDirectory = cacheDirectory ?? GetDefaultCacheDirectory();

        _httpClient = new HttpClient
        {
            // MinerU GPU inference can take a long time
            Timeout = TimeSpan.FromMinutes(30)
        };

        // Ensure cache directory exists
        Directory.CreateDirectory(_cacheDirectory);
    }

    private static string GetDefaultCacheDirectory()
    {
        var baseCache = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(baseCache, "Caly", "mineru");
    }

    #region Health Check

    /// <summary>
    /// Checks if the MinerU service is reachable.
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

    #region Sync Parse (/file_parse)

    /// <summary>
    /// Synchronously parses a PDF file using MinerU.
    /// Uploads the file, waits for completion, and returns the result in the same response.
    /// </summary>
    public async Task<MinerUParseResult> ParseSyncAsync(
        string pdfPath,
        string backend = DefaultBackend,
        Action<MinerUParseStatus, int>? onProgress = null,
        CancellationToken ct = default)
    {
        onProgress?.Invoke(MinerUParseStatus.Submitting, 10);

        var zipBytes = await UploadAndParseSyncAsync(pdfPath, backend, ct);
        var zipPath = CacheResultZip(pdfPath, zipBytes);

        onProgress?.Invoke(MinerUParseStatus.Caching, 80);

        return await BuildParseResultAsync(zipPath, onProgress, ct);
    }

    private async Task<byte[]> UploadAndParseSyncAsync(string pdfPath, string backend, CancellationToken ct)
    {
        var pdfBytes = File.ReadAllBytes(pdfPath);

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(pdfBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(fileContent, "files", Path.GetFileName(pdfPath));
        content.Add(new StringContent(backend), "backend");

        using var response = await _httpClient.PostAsync($"{_baseUrl}/file_parse", content, ct);
        response.EnsureSuccessStatusCode();

        // The sync endpoint returns the zip directly in the response body
        var zipBytes = await response.Content.ReadAsByteArrayAsync(ct);

        // Also check for JSON metadata in headers or response
        return zipBytes;
    }

    #endregion

    #region Async Parse (/tasks)

    /// <summary>
    /// Asynchronously parses a PDF file using MinerU.
    /// Submits the task, polls for completion, downloads the result, and builds the PopoDocument.
    /// </summary>
    public async Task<MinerUParseResult> ParseAsync(
        string pdfPath,
        string backend = DefaultBackend,
        Action<MinerUParseStatus, int>? onProgress = null,
        CancellationToken ct = default)
    {
        // Step 1: Submit task
        onProgress?.Invoke(MinerUParseStatus.Submitting, 10);
        var taskId = await SubmitTaskAsync(pdfPath, backend, ct);

        // Step 2: Poll until complete
        onProgress?.Invoke(MinerUParseStatus.Queued, 15);
        await PollUntilCompleteAsync(taskId, onProgress, ct);

        // Step 3: Download result
        onProgress?.Invoke(MinerUParseStatus.Downloading, 70);
        var zipPath = await DownloadResultAsync(taskId, pdfPath, ct);

        // Step 4: Cache
        onProgress?.Invoke(MinerUParseStatus.Caching, 80);

        // Step 5: Build result
        return await BuildParseResultAsync(zipPath, onProgress, ct);
    }

    /// <summary>
    /// Submits a PDF file for async parsing via POST /tasks.
    /// Returns the task ID for status polling.
    /// </summary>
    public async Task<string> SubmitTaskAsync(string pdfPath, string backend = DefaultBackend, CancellationToken ct = default)
    {
        var pdfBytes = File.ReadAllBytes(pdfPath);

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(pdfBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(fileContent, "files", Path.GetFileName(pdfPath));
        content.Add(new StringContent(backend), "backend");

        using var response = await _httpClient.PostAsync($"{_baseUrl}/tasks", content, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<MinerUTaskSubmitResponse>(json);

        if (result is null || string.IsNullOrEmpty(result.TaskId))
        {
            throw new MinerUServiceException("Failed to get task ID from MinerU response.");
        }

        return result.TaskId;
    }

    /// <summary>
    /// Gets the current status of an async parse task via GET /tasks/{task_id}.
    /// </summary>
    public async Task<MinerUTaskStatusResponse> GetTaskStatusAsync(string taskId, CancellationToken ct = default)
    {
        using var response = await _httpClient.GetAsync($"{_baseUrl}/tasks/{taskId}", ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<MinerUTaskStatusResponse>(json);

        return result ?? throw new MinerUServiceException("Failed to parse task status response.");
    }

    /// <summary>
    /// Polls the task status until it completes or fails.
    /// Calls onProgress at each status change.
    /// </summary>
    public async Task PollUntilCompleteAsync(
        string taskId,
        Action<MinerUParseStatus, int>? onProgress = null,
        CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            var status = await GetTaskStatusAsync(taskId, ct);

            if (status.IsRunning)
            {
                // Map MinerU status to our progress
                var minerUStatus = status.Status == "pending"
                    ? MinerUParseStatus.Queued
                    : MinerUParseStatus.Processing;

                var progress = status.Progress ?? minerUStatus.ToProgressPercent();
                onProgress?.Invoke(minerUStatus, progress);
            }
            else if (status.IsCompleted)
            {
                onProgress?.Invoke(MinerUParseStatus.Downloading, 70);
                return;
            }
            else if (status.IsFailed)
            {
                onProgress?.Invoke(MinerUParseStatus.Failed, -1);
                throw new MinerUServiceException($"MinerU task failed: {status.Message}");
            }
            else
            {
                // Unknown status, treat as running
                onProgress?.Invoke(MinerUParseStatus.Processing, status.Progress ?? 35);
            }

            await Task.Delay(DefaultPollInterval, ct);
        }
    }

    /// <summary>
    /// Downloads the parse result zip via GET /tasks/{task_id}/result.
    /// </summary>
    public async Task<string> DownloadResultAsync(string taskId, string sourcePdfPath, CancellationToken ct = default)
    {
        using var response = await _httpClient.GetAsync($"{_baseUrl}/tasks/{taskId}/result", ct);
        response.EnsureSuccessStatusCode();

        var zipBytes = await response.Content.ReadAsByteArrayAsync(ct);
        return CacheResultZip(sourcePdfPath, zipBytes);
    }

    #endregion

    #region Result Processing

    /// <summary>
    /// Saves the raw zip result to the local cache directory.
    /// </summary>
    private string CacheResultZip(string sourcePdfPath, byte[] zipBytes)
    {
        var docId = Path.GetFileNameWithoutExtension(sourcePdfPath);
        var zipFileName = $"{docId}_mineru.zip";
        var zipPath = Path.Combine(_cacheDirectory, zipFileName);

        File.WriteAllBytes(zipPath, zipBytes);
        return zipPath;
    }

    /// <summary>
    /// Extracts the zip, parses the MinerU output, and builds a MinerUParseResult.
    /// </summary>
    private async Task<MinerUParseResult> BuildParseResultAsync(
        string zipPath,
        Action<MinerUParseStatus, int>? onProgress,
        CancellationToken ct)
    {
        // Extract zip to a temp directory
        var tempDir = Path.Combine(_cacheDirectory, Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);

        try
        {
            ZipFile.ExtractToDirectory(zipPath, tempDir, overwriteFiles: true);
        }
        catch
        {
            // If zip extraction fails, try to use the zip directly
            // (some MinerU versions return non-standard zip format)
        }

        onProgress?.Invoke(MinerUParseStatus.ParsingResult, 85);

        // Find and parse middle.json
        // TODO(Phase 2): Implement PopoJsonService.ParseMinerUMiddleJson and ParseMinerUZip
        var popoDoc = await Task.Run(() =>
        {
            // Search for *_middle.json in the extracted directory
            var middleJsonFiles = Directory.GetFiles(tempDir, "*_middle.json", SearchOption.AllDirectories);
            if (middleJsonFiles.Length > 0)
            {
                return PopoJsonService.TryParseMinerUMiddleJson(middleJsonFiles[0]);
            }

            // Fallback: try to parse from the zip directly
            return PopoJsonService.TryParseMinerUZip(zipPath);
        }, ct);

        // Find markdown files
        string? markdown = null;
        string? popoMarkdown = null;

        var mdFiles = Directory.GetFiles(tempDir, "*.md", SearchOption.AllDirectories);
        foreach (var mdFile in mdFiles)
        {
            var fileName = Path.GetFileNameWithoutExtension(mdFile).ToLowerInvariant();
            if (fileName.Contains("_popo"))
            {
                popoMarkdown = File.ReadAllText(mdFile);
            }
            else if (popoMarkdown is null)
            {
                // Use the first non-popo markdown as the main markdown
                markdown ??= File.ReadAllText(mdFile);
            }
        }

        return new MinerUParseResult
        {
            PopoDocument = popoDoc,
            ZipPath = zipPath,
            Markdown = markdown,
            PopoMarkdown = popoMarkdown,
            ArtifactsDirectory = tempDir
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
/// Exception thrown when a MinerU service operation fails.
/// </summary>
public class MinerUServiceException : Exception
{
    public MinerUServiceException(string message) : base(message) { }

    public MinerUServiceException(string message, Exception inner) : base(message, inner) { }
}