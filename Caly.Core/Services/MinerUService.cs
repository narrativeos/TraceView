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
    /// <param name="cacheDirectory">Local directory for caching parse results. If null, uses project's mineru/ dir or default cache.</param>
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

        return await BuildParseResultAsync(zipPath, pdfPath, onProgress, ct);
    }

    private async Task<byte[]> UploadAndParseSyncAsync(string pdfPath, string backend, CancellationToken ct)
    {
        var pdfBytes = File.ReadAllBytes(pdfPath);

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(pdfBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(fileContent, "files", Path.GetFileName(pdfPath));
        content.Add(new StringContent(backend), "backend");
        content.Add(new StringContent("auto"), "parse_method");
        content.Add(new StringContent("true"), "return_md");
        content.Add(new StringContent("true"), "return_middle_json");
        content.Add(new StringContent("true"), "response_format_zip");

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
        return await BuildParseResultAsync(zipPath, pdfPath, onProgress, ct);
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
        content.Add(new StringContent("auto"), "parse_method");
        content.Add(new StringContent("true"), "return_md");
        content.Add(new StringContent("true"), "return_middle_json");
        content.Add(new StringContent("true"), "response_format_zip");

        using var response = await _httpClient.PostAsync($"{_baseUrl}/tasks", content, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);

        if (string.IsNullOrEmpty(json) || !json.TrimStart().StartsWith("{"))
        {
            var preview = json?.Length > 200 ? json.Substring(0, 200) + "..." : json;
            throw new MinerUServiceException(
                $"Invalid JSON response from MinerU /tasks. Response: {preview}");
        }

        MinerUTaskSubmitResponse result;
        try
        {
            result = JsonSerializer.Deserialize(json, SourceGenerationContext.Default.MinerUTaskSubmitResponse)
                ?? throw new MinerUServiceException($"JsonSerializer returned null for /tasks response. Raw: {json?.Substring(0, Math.Min(500, json.Length))}");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            var rawJson = json?.Substring(0, Math.Min(2000, json?.Length ?? 0));
            var errorMsg = $"Failed to deserialize MinerU /tasks response. Error: {ex.GetType().Name}: {ex.Message}. Raw JSON: {rawJson}";
            System.Diagnostics.Debug.WriteLine($"[MinerU] {errorMsg}");
            System.Console.Error.WriteLine($"[MinerU] {errorMsg}");
            throw new MinerUServiceException(errorMsg, ex);
        }

        if (string.IsNullOrEmpty(result.TaskId))
        {
            throw new MinerUServiceException(
                $"Failed to get task ID from MinerU response. Raw: {json?.Substring(0, Math.Min(500, json.Length))}");
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

        if (string.IsNullOrEmpty(json) || !json.TrimStart().StartsWith("{"))
        {
            var preview = json?.Length > 200 ? json.Substring(0, 200) + "..." : json;
            throw new MinerUServiceException(
                $"Invalid JSON response from MinerU /tasks/{{id}}. Response: {preview}");
        }

        MinerUTaskStatusResponse result;
        try
        {
            result = JsonSerializer.Deserialize(json, SourceGenerationContext.Default.MinerUTaskStatusResponse)
                ?? throw new MinerUServiceException($"JsonSerializer returned null for /tasks/{{id}} response. Raw: {json?.Substring(0, Math.Min(500, json.Length))}");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            var rawJson = json?.Substring(0, Math.Min(2000, json?.Length ?? 0));
            var errorMsg = $"Failed to deserialize MinerU /tasks/{{id}} response. Error: {ex.GetType().Name}: {ex.Message}. Raw JSON: {rawJson}";
            System.Diagnostics.Debug.WriteLine($"[MinerU] {errorMsg}");
            System.Console.Error.WriteLine($"[MinerU] {errorMsg}");
            throw new MinerUServiceException(errorMsg, ex);
        }

        return result;
    }

    /// <summary>
    /// Polls the task status until it completes or fails.
    /// Calls onProgress at each status change.
    /// Note: MinerU v3.4.0 no longer provides a "progress" field,
    /// so progress is inferred from status transitions.
    /// </summary>
    public async Task PollUntilCompleteAsync(
        string taskId,
        Action<MinerUParseStatus, int>? onProgress = null,
        CancellationToken ct = default)
    {
        int lastProgress = 15;

        while (!ct.IsCancellationRequested)
        {
            var status = await GetTaskStatusAsync(taskId, ct);

            if (status.IsRunning)
            {
                // Map MinerU status to our progress
                var minerUStatus = status.Status == "pending"
                    ? MinerUParseStatus.Queued
                    : MinerUParseStatus.Processing;

                // Infer progress from status (v3.4.0 no longer returns progress)
                var progress = minerUStatus == MinerUParseStatus.Queued ? 20 : 50;

                // Only update if progress changed significantly
                if (progress != lastProgress)
                {
                    lastProgress = progress;
                    onProgress?.Invoke(minerUStatus, progress);
                }
            }
            else if (status.IsCompleted)
            {
                onProgress?.Invoke(MinerUParseStatus.Downloading, 70);
                return;
            }
            else if (status.IsFailed)
            {
                onProgress?.Invoke(MinerUParseStatus.Failed, -1);
                var errorMessage = status.GetErrorMessage() ?? "Unknown error";
                throw new MinerUServiceException($"MinerU task failed: {errorMessage}");
            }
            else
            {
                // Unknown status, treat as running
                var progress = 35;
                if (progress != lastProgress)
                {
                    lastProgress = progress;
                    onProgress?.Invoke(MinerUParseStatus.Processing, progress);
                }
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

    /// <summary>
    /// Gets the cached ZIP file path for a given PDF.
    /// </summary>
    private string GetCacheZipPath(string pdfPath)
    {
        var docId = Path.GetFileNameWithoutExtension(pdfPath);
        var zipFileName = $"{docId}_mineru.zip";
        return Path.Combine(_cacheDirectory, zipFileName);
    }

    /// <summary>
    /// Gets the extracted artifacts directory path for a given PDF.
    /// Uses a consistent naming scheme for easy cleanup.
    /// </summary>
    private string GetExtractedDirPath(string pdfPath)
    {
        var docId = Path.GetFileNameWithoutExtension(pdfPath);
        return Path.Combine(_cacheDirectory, $"extract_{docId}");
    }

    #region Task ID Persistence

    /// <summary>
    /// Gets the path to the task_id.json file within the project's mineru directory.
    /// Always uses the project path to ensure consistency across service instances.
    /// </summary>
    private string GetTaskIdFilePath(string pdfPath, string? projectPath)
    {
        var docId = Path.GetFileNameWithoutExtension(pdfPath);
        // Always save to project's mineru directory for consistency
        var baseDir = !string.IsNullOrEmpty(projectPath)
            ? Path.Combine(projectPath, "mineru")
            : _cacheDirectory;
        return Path.Combine(baseDir, $"{docId}_task_id.json");
    }

    /// <summary>
    /// Saves the current task ID to disk so it can be recovered on a subsequent launch.
    /// The task ID is saved to the project's mineru/ directory for consistency.
    /// </summary>
    /// <param name="pdfPath">Path to the PDF file.</param>
    /// <param name="taskId">The MinerU task ID to persist.</param>
    /// <param name="projectPath">The project directory path. Task ID is saved under {projectPath}/mineru/.</param>
    public void SaveTaskId(string pdfPath, string taskId, string? projectPath = null)
    {
        try
        {
            var path = GetTaskIdFilePath(pdfPath, projectPath);
            var dir = Path.GetDirectoryName(path);
            Directory.CreateDirectory(dir);
            var data = new
            {
                taskId = taskId,
                pdfPath = pdfPath,
                createdAt = DateTimeOffset.UtcNow.ToString("O")
            };
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(path, json);
        }
        catch
        {
            // Non-critical: task ID persistence failure is tolerable
        }
    }

    /// <summary>
    /// Loads a previously saved task ID for the given PDF.
    /// Returns null if no pending task exists.
    /// </summary>
    /// <param name="pdfPath">Path to the PDF file.</param>
    /// <param name="projectPath">The project directory path. Looks for task ID under {projectPath}/mineru/.</param>
    public string? LoadTaskId(string pdfPath, string? projectPath = null)
    {
        try
        {
            var path = GetTaskIdFilePath(pdfPath, projectPath);
            if (!File.Exists(path))
                return null;

            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("taskId", out var idElem)
                ? idElem.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Clears the saved task ID after the task completes or fails.
    /// </summary>
    /// <param name="pdfPath">Path to the PDF file.</param>
    /// <param name="projectPath">The project directory path. Removes task ID from {projectPath}/mineru/.</param>
    public void ClearTaskId(string pdfPath, string? projectPath = null)
    {
        try
        {
            var path = GetTaskIdFilePath(pdfPath, projectPath);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Non-critical
        }
    }

    /// <summary>
    /// Resumes a previously submitted task: checks status, polls if still running,
    /// downloads the result when complete, and builds the parse result.
    /// </summary>
    public async Task<MinerUParseResult> ResumeTaskAsync(
        string taskId,
        string pdfPath,
        Action<MinerUParseStatus, int>? onProgress = null,
        CancellationToken ct = default)
    {
        // Check current status
        var status = await GetTaskStatusAsync(taskId, ct);

        string resultZip;

        if (status.IsCompleted)
        {
            // Task already completed, just download
            onProgress?.Invoke(MinerUParseStatus.Downloading, 70);
            resultZip = await DownloadResultAsync(taskId, pdfPath, ct);
        }
        else if (status.IsFailed)
        {
            onProgress?.Invoke(MinerUParseStatus.Failed, -1);
            var errorMessage = status.GetErrorMessage() ?? "Unknown error";
            throw new MinerUServiceException($"MinerU task failed: {errorMessage}");
        }
        else
        {
            // Task is still running, continue polling
            onProgress?.Invoke(MinerUParseStatus.Processing, 50);
            await PollUntilCompleteAsync(taskId, onProgress, ct);

            // Download result
            onProgress?.Invoke(MinerUParseStatus.Downloading, 70);
            resultZip = await DownloadResultAsync(taskId, pdfPath, ct);
        }

        onProgress?.Invoke(MinerUParseStatus.Caching, 80);
        return await BuildParseResultAsync(resultZip, pdfPath, onProgress, ct);
    }

    /// <summary>
    /// Builds a parse result from an already-downloaded ZIP file.
    /// This is a public wrapper around the private BuildParseResultAsync for use by the ViewModel
    /// when orchestrating the parse flow step-by-step.
    /// </summary>
    public Task<MinerUParseResult> BuildParseResultFromZipAsync(
        string zipPath,
        string sourcePdfPath,
        Action<MinerUParseStatus, int>? onProgress,
        CancellationToken ct)
    {
        return BuildParseResultAsync(zipPath, sourcePdfPath, onProgress, ct);
    }

    #endregion

    #region Cache Operations

    /// <summary>
    /// Tries to load a previously cached parse result without making network requests.
    /// Returns the result if a valid cached ZIP and extracted files exist.
    /// </summary>
    public MinerUParseResult? TryLoadFromCache(string pdfPath)
    {
        var zipPath = GetCacheZipPath(pdfPath);
        if (!File.Exists(zipPath))
            return null;

        var extractedDir = GetExtractedDirPath(pdfPath);
        // If extracted dir doesn't exist, extract from zip
        if (!Directory.Exists(extractedDir))
        {
            try
            {
                Directory.CreateDirectory(extractedDir);
                ZipFile.ExtractToDirectory(zipPath, extractedDir, overwriteFiles: true);
            }
            catch
            {
                return null;
            }
        }

        var popoDoc = PopoJsonService.TryParseMinerUFromExtractedDir(extractedDir);
        if (popoDoc is null)
            return null;

        var markdownInfo = ExtractMarkdownFromDir(extractedDir);

        return new MinerUParseResult
        {
            PopoDocument = popoDoc,
            ZipPath = zipPath,
            Markdown = markdownInfo.markdown,
            PopoMarkdown = markdownInfo.popoMarkdown,
            ArtifactsDirectory = extractedDir
        };
    }

    /// <summary>
    /// Cleans up old extracted directories, keeping only the one for the current document.
    /// This prevents disk space from growing unbounded.
    /// </summary>
    public void ClearOldExtractedDirs(string currentDocId)
    {
        try
        {
            foreach (var dir in Directory.GetDirectories(_cacheDirectory))
            {
                var dirName = Path.GetFileName(dir);
                if (dirName.StartsWith("extract_") && !dirName.Contains(currentDocId))
                {
                    try
                    {
                        Directory.Delete(dir, recursive: true);
                    }
                    catch
                    {
                        // Ignore cleanup errors (directory might be in use)
                    }
                }
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    #endregion

    #region Result Processing

    /// <summary>
    /// Saves the raw zip result to the local cache directory.
    /// </summary>
    private string CacheResultZip(string sourcePdfPath, byte[] zipBytes)
    {
        var zipPath = GetCacheZipPath(sourcePdfPath);
        File.WriteAllBytes(zipPath, zipBytes);
        return zipPath;
    }

    /// <summary>
    /// Extracts the zip to a consistent cache directory, parses the MinerU output,
    /// and builds a MinerUParseResult. Uses the already-extracted directory to avoid
    /// redundant extraction in PopoJsonService.
    /// </summary>
    private async Task<MinerUParseResult> BuildParseResultAsync(
        string zipPath,
        string sourcePdfPath,
        Action<MinerUParseStatus, int>? onProgress,
        CancellationToken ct)
    {
        onProgress?.Invoke(MinerUParseStatus.ParsingResult, 85);

        // Extract to a consistent directory (not random) for cache reuse
        var extractedDir = GetExtractedDirPath(sourcePdfPath);

        // Clean old extracted dir if exists
        if (Directory.Exists(extractedDir))
        {
            try { Directory.Delete(extractedDir, recursive: true); }
            catch { }
        }
        Directory.CreateDirectory(extractedDir);

        try
        {
            ZipFile.ExtractToDirectory(zipPath, extractedDir, overwriteFiles: true);
        }
        catch (Exception ex)
        {
            throw new MinerUServiceException($"Failed to extract parse result: {ex.Message}", ex);
        }

        // Parse the extracted directory
        var popoDoc = await Task.Run(() =>
            PopoJsonService.TryParseMinerUFromExtractedDir(extractedDir), ct);

        // Extract markdown files
        var markdownInfo = ExtractMarkdownFromDir(extractedDir);

        return new MinerUParseResult
        {
            PopoDocument = popoDoc,
            ZipPath = zipPath,
            Markdown = markdownInfo.markdown,
            PopoMarkdown = markdownInfo.popoMarkdown,
            ArtifactsDirectory = extractedDir
        };
    }

    /// <summary>
    /// Extracts markdown content from a directory.
    /// </summary>
    private (string? markdown, string? popoMarkdown) ExtractMarkdownFromDir(string dirPath)
    {
        string? markdown = null;
        string? popoMarkdown = null;

        try
        {
            var mdFiles = Directory.GetFiles(dirPath, "*.md", SearchOption.AllDirectories);
            foreach (var mdFile in mdFiles)
            {
                var fileName = Path.GetFileNameWithoutExtension(mdFile).ToLowerInvariant();
                if (fileName.Contains("_popo"))
                {
                    popoMarkdown = File.ReadAllText(mdFile);
                }
                else if (popoMarkdown is null)
                {
                    markdown ??= File.ReadAllText(mdFile);
                }
            }
        }
        catch
        {
            // Ignore errors reading markdown files
        }

        return (markdown, popoMarkdown);
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