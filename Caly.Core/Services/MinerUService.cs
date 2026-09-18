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
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Caly.Core.Models;
using Caly.Core.Utilities;

namespace Caly.Core.Services;

/// <summary>
/// HTTP client for the MinerU 4.0 external parsing service (V1 API).
/// Uses the async "upload → parse job → poll → download" flow:
/// POST /v1/uploads → PUT {upload_url} → POST /v1/uploads/{id}/complete →
/// POST /v1/parse/jobs → GET /v1/parse/jobs/{id} (poll) → GET /v1/files/{file_id}/content.
/// </summary>
public sealed class MinerUService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string? _apiKey;
    private readonly string _cacheDirectory;

    /// <summary>
    /// Default MinerU API base URL.
    /// </summary>
    public const string DefaultBaseUrl = "http://localhost:8401";

    /// <summary>
    /// Default parse tier (MinerU 4.0: flash / basic / standard / advanced).
    /// </summary>
    public const string DefaultTier = "standard";

    /// <summary>
    /// Polling interval for parse job status checks.
    /// </summary>
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Creates a new MinerUService instance.
    /// </summary>
    /// <param name="baseUrl">MinerU API base URL, i.e. the root address before /v1 (default: http://localhost:8401).</param>
    /// <param name="cacheDirectory">Local directory for caching parse results. If null, uses project's mineru/ dir or default cache.</param>
    /// <param name="apiKey">Optional API key. Required when the service is started with --api-key.</param>
    public MinerUService(string? baseUrl = null, string? cacheDirectory = null, string? apiKey = null)
    {
        _baseUrl = (baseUrl ?? DefaultBaseUrl).TrimEnd('/');
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        _cacheDirectory = cacheDirectory ?? GetDefaultCacheDirectory();

        _httpClient = new HttpClient
        {
            // MinerU GPU inference can take a long time
            Timeout = TimeSpan.FromMinutes(30)
        };

        // Ensure cache directory exists
        Directory.CreateDirectory(_cacheDirectory);
    }

    /// <summary>
    /// Adds the Bearer auth header to a request when an API key is configured.
    /// For cross-origin targets (e.g. presigned object-storage upload URLs) the
    /// MinerU API key is deliberately NOT attached, to avoid leaking credentials.
    /// </summary>
    private void AddAuthHeader(HttpRequestMessage request, Uri? targetUri = null)
    {
        if (_apiKey is null)
            return;

        if (targetUri is not null && !IsSameOrigin(targetUri))
            return;

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
    }

    /// <summary>
    /// Determines whether a URL is same-origin (scheme + host + effective port)
    /// as the MinerU API base URL.
    /// </summary>
    private bool IsSameOrigin(Uri uri)
    {
        if (!Uri.TryCreate(_baseUrl, UriKind.Absolute, out var baseUri))
            return false;

        return string.Equals(uri.Scheme, baseUri.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(uri.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase)
            && uri.Port == baseUri.Port;
    }

    private static string GetDefaultCacheDirectory()
    {
        var baseCache = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(baseCache, "Caly", "mineru");
    }

    #region Health Check

    /// <summary>
    /// Checks if the MinerU service is reachable via GET /v1/health.
    /// </summary>
    public async Task<bool> HealthCheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/v1/health");
            AddAuthHeader(request);
            using var response = await _httpClient.SendAsync(request, ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region V1 Upload

    /// <summary>
    /// Uploads a file to the MinerU V1 API and returns the resulting file_id.
    /// Flow: POST /v1/uploads → PUT {upload_url} (raw bytes) → POST /v1/uploads/{id}/complete.
    /// When the sha256sum hits an already-stored blob, the server responds with
    /// status "completed" and an embedded file object, skipping the byte upload.
    /// </summary>
    public async Task<string> UploadFileAsync(string filePath, CancellationToken ct = default)
    {
        var pdfBytes = File.ReadAllBytes(filePath);
        var sha256 = ComputeSha256Hex(pdfBytes);

        // Step 1: Create the upload
        var createBody = new MinerUUploadCreateRequest
        {
            Filename = Path.GetFileName(filePath),
            Bytes = pdfBytes.Length,
            MimeType = GetMimeType(filePath),
            Purpose = "parse",
            Sha256Sum = sha256
        };
        using var createRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v1/uploads")
        {
            Content = CreateJsonContent(createBody, SourceGenerationContext.Default.MinerUUploadCreateRequest)
        };
        AddAuthHeader(createRequest);
        using var createResponse = await _httpClient.SendAsync(createRequest, ct);
        var createJson = await ReadJsonOrThrowAsync(createResponse, "POST /v1/uploads", ct);
        var upload = DeserializeOrThrow<MinerUUploadResponse>(createJson, "POST /v1/uploads", SourceGenerationContext.Default.MinerUUploadResponse);

        if (string.IsNullOrEmpty(upload.Id))
            throw new MinerUServiceException("MinerU upload response is missing the upload id.");

        // Instant completion (sha256 hit): the file object is embedded directly
        if (upload.Status == "completed" && upload.File is not null)
            return upload.File.Id;

        if (upload.Status != "pending" || string.IsNullOrEmpty(upload.UploadUrl))
            throw new MinerUServiceException(
                $"Unexpected MinerU upload status '{upload.Status}' without an upload_url.");

        // Step 2: Upload the raw bytes (same-origin URLs carry the API auth, presigned URLs do not)
        var uploadUri = new Uri(upload.UploadUrl, UriKind.RelativeOrAbsolute);
        if (!uploadUri.IsAbsoluteUri)
            uploadUri = new Uri(new Uri(_baseUrl, UriKind.Absolute), uploadUri);

        using var putRequest = new HttpRequestMessage(HttpMethod.Put, uploadUri)
        {
            Content = new ByteArrayContent(pdfBytes)
        };
        putRequest.Content.Headers.ContentType =
            new MediaTypeHeaderValue("application/octet-stream");
        if (upload.UploadHeaders is not null)
        {
            foreach (var (key, value) in upload.UploadHeaders)
            {
                putRequest.Headers.TryAddWithoutValidation(key, value);
            }
        }
        AddAuthHeader(putRequest, uploadUri);

        using var putResponse = await _httpClient.SendAsync(putRequest, ct);
        if (!putResponse.IsSuccessStatusCode)
        {
            var body = await putResponse.Content.ReadAsStringAsync(ct);
            throw new MinerUServiceException(
                $"MinerU byte upload failed ({(int)putResponse.StatusCode}): {Truncate(body, 500)}");
        }

        // Step 3: Complete the upload and get the file_id
        using var completeRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v1/uploads/{upload.Id}/complete")
        {
            Content = CreateJsonContent(new MinerUUploadCompleteRequest { Sha256Sum = sha256 }, SourceGenerationContext.Default.MinerUUploadCompleteRequest)
        };
        AddAuthHeader(completeRequest);
        using var completeResponse = await _httpClient.SendAsync(completeRequest, ct);
        var completeJson = await ReadJsonOrThrowAsync(completeResponse, "POST /v1/uploads/{id}/complete", ct);
        var completed = DeserializeOrThrow<MinerUUploadResponse>(completeJson, "POST /v1/uploads/{id}/complete", SourceGenerationContext.Default.MinerUUploadResponse);

        if (completed.File is null || string.IsNullOrEmpty(completed.File.Id))
            throw new MinerUServiceException(
                $"MinerU upload completion did not return a file id. Raw: {Truncate(completeJson, 500)}");

        return completed.File.Id;
    }

    /// <summary>
    /// Computes the lowercase hex SHA-256 of a byte array.
    /// </summary>
    private static string ComputeSha256Hex(byte[] data)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Gets a best-effort MIME type for the upload request.
    /// </summary>
    private static string GetMimeType(string filePath)
    {
        return Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".html" or ".htm" => "text/html",
            ".epub" => "application/epub+zip",
            ".csv" => "text/csv",
            ".txt" => "text/plain",
            _ => "application/octet-stream"
        };
    }

    /// <summary>
    /// Creates an application/json request content from an object.
    /// </summary>
    private static StringContent CreateJsonContent<T>(T payload, JsonTypeInfo<T> typeInfo)
    {
        var json = JsonSerializer.Serialize(payload, typeInfo);
        return new StringContent(json, System.Text.Encoding.UTF8, "application/json");
    }

    /// <summary>
    /// Reads the response body and throws a MinerUServiceException with a preview
    /// when the request did not succeed.
    /// </summary>
    private static async Task<string> ReadJsonOrThrowAsync(HttpResponseMessage response, string operation, CancellationToken ct)
    {
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new MinerUServiceException(
                $"{operation} failed ({(int)response.StatusCode}): {Truncate(json, 500)}");
        }
        return json;
    }

    /// <summary>
    /// Deserializes a MinerU V1 API response, attaching the raw JSON to errors
    /// to make protocol mismatches diagnosable.
    /// </summary>
    private static T DeserializeOrThrow<T>(string json, string operation, JsonTypeInfo<T> typeInfo)
    {
        if (string.IsNullOrEmpty(json) || !json.TrimStart().StartsWith("{"))
        {
            var preview = json?.Length > 200 ? json.Substring(0, 200) + "..." : json;
            throw new MinerUServiceException($"Invalid JSON response from MinerU {operation}. Response: {preview}");
        }

        try
        {
            return JsonSerializer.Deserialize(json, typeInfo)
                ?? throw new MinerUServiceException($"JsonSerializer returned null for {operation} response. Raw: {Truncate(json, 500)}");
        }
        catch (JsonException ex)
        {
            var errorMsg = $"Failed to deserialize MinerU {operation} response. Error: {ex.Message}. Raw JSON: {Truncate(json, 2000)}";
            System.Diagnostics.Debug.WriteLine($"[MinerU] {errorMsg}");
            System.Console.Error.WriteLine($"[MinerU] {errorMsg}");
            throw new MinerUServiceException(errorMsg, ex);
        }
    }

    /// <summary>
    /// Truncates a string for error messages.
    /// </summary>
    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        return value.Length > maxLength ? value.Substring(0, maxLength) + "..." : value;
    }

    #endregion

    #region Async Parse (V1 jobs)

    /// <summary>
    /// Asynchronously parses a PDF file using the MinerU 4.0 V1 API.
    /// Uploads the file, submits a parse job, polls for completion,
    /// downloads the result zip, and builds the MinerUDocument.
    /// </summary>
    public async Task<MinerUParseResult> ParseAsync(
        string pdfPath,
        string tier = DefaultTier,
        Action<MinerUParseStatus, int>? onProgress = null,
        CancellationToken ct = default)
    {
        // Step 1: Submit the parse job (includes the file upload)
        onProgress?.Invoke(MinerUParseStatus.Submitting, 10);
        var jobId = await SubmitTaskAsync(pdfPath, tier, ct);

        // Step 2: Poll until complete
        onProgress?.Invoke(MinerUParseStatus.Queued, 15);
        await PollUntilCompleteAsync(jobId, onProgress, ct);

        // Step 3: Download result
        onProgress?.Invoke(MinerUParseStatus.Downloading, 70);
        var zipPath = await DownloadResultAsync(jobId, pdfPath, ct);

        // Step 4: Cache
        onProgress?.Invoke(MinerUParseStatus.Caching, 80);

        // Step 5: Build result
        return await BuildParseResultAsync(zipPath, pdfPath, onProgress, ct);
    }

    /// <summary>
    /// Uploads a PDF and submits it for async parsing via POST /v1/parse/jobs.
    /// Returns the job ID for status polling.
    /// </summary>
    public async Task<string> SubmitTaskAsync(string pdfPath, string tier = DefaultTier, CancellationToken ct = default)
    {
        // Upload the file first to obtain a file_id
        var fileId = await UploadFileAsync(pdfPath, ct);

        // Create the parse job
        var jobBody = new MinerUParseJobRequest
        {
            Files = new List<MinerUParseJobFile>
            {
                new MinerUParseJobFile
                {
                    Source = new MinerUParseJobFileSource { Type = "file_id", FileId = fileId }
                }
            },
            Tier = tier,
            OutputFormats = new[] { "markdown", "middle_json", "zip" }.ToList()
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v1/parse/jobs")
        {
            Content = CreateJsonContent(jobBody, SourceGenerationContext.Default.MinerUParseJobRequest)
        };
        AddAuthHeader(request);
        using var response = await _httpClient.SendAsync(request, ct);
        var json = await ReadJsonOrThrowAsync(response, "POST /v1/parse/jobs", ct);

        var result = DeserializeOrThrow<MinerUJobResponse>(json, "POST /v1/parse/jobs", SourceGenerationContext.Default.MinerUJobResponse);

        if (string.IsNullOrEmpty(result.JobId))
        {
            throw new MinerUServiceException(
                $"Failed to get job ID from MinerU response. Raw: {Truncate(json, 500)}");
        }

        return result.JobId;
    }

    /// <summary>
    /// Gets the current status of a parse job via GET /v1/parse/jobs/{job_id}.
    /// </summary>
    public async Task<MinerUJobResponse> GetTaskStatusAsync(string jobId, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/v1/parse/jobs/{jobId}");
        AddAuthHeader(request);
        using var response = await _httpClient.SendAsync(request, ct);
        var json = await ReadJsonOrThrowAsync(response, "GET /v1/parse/jobs/{id}", ct);

        return DeserializeOrThrow<MinerUJobResponse>(json, "GET /v1/parse/jobs/{id}", SourceGenerationContext.Default.MinerUJobResponse);
    }

    /// <summary>
    /// Polls the parse job status until it reaches a terminal state
    /// (completed, partial, failed or canceled).
    /// Calls onProgress at each status change.
    /// The V1 API only reports file-level counters, so progress is coarse.
    /// </summary>
    public async Task PollUntilCompleteAsync(
        string jobId,
        Action<MinerUParseStatus, int>? onProgress = null,
        CancellationToken ct = default)
    {
        int lastProgress = 15;

        while (!ct.IsCancellationRequested)
        {
            var status = await GetTaskStatusAsync(jobId, ct);

            if (status.IsRunning)
            {
                // Map MinerU job status to our progress
                var minerUStatus = status.Status == "queued"
                    ? MinerUParseStatus.Queued
                    : MinerUParseStatus.Processing;

                var progress = minerUStatus == MinerUParseStatus.Queued ? 20 : 50;

                // Only update if progress changed
                if (progress != lastProgress)
                {
                    lastProgress = progress;
                    onProgress?.Invoke(minerUStatus, progress);
                }
            }
            else if (status.IsCompleted || status.IsPartial)
            {
                // partial: at least one file succeeded; the download step below
                // fails with a clear error when the requested file has no output.
                onProgress?.Invoke(MinerUParseStatus.Downloading, 70);
                return;
            }
            else if (status.IsFailed || status.IsCanceled)
            {
                onProgress?.Invoke(MinerUParseStatus.Failed, -1);
                var errorMessage = status.GetErrorMessage() ?? "Unknown error";
                throw new MinerUServiceException($"MinerU parse job {status.Status}: {errorMessage}");
            }
            else
            {
                // Unknown status, treat as running
                if (35 != lastProgress)
                {
                    lastProgress = 35;
                    onProgress?.Invoke(MinerUParseStatus.Processing, 35);
                }
            }

            await Task.Delay(DefaultPollInterval, ct);
        }
    }

    /// <summary>
    /// Polls the parse job status until it reaches a terminal state, with detailed progress info.
    /// The MinerU 4.0 V1 API does not report page-level progress, so the
    /// MinerUTaskProgress detail is always null and the UI falls back to the
    /// coarse status text.
    /// </summary>
    public async Task PollUntilCompleteAsync(
        string jobId,
        Action<MinerUParseStatus, int, MinerUTaskProgress?>? onProgress = null,
        CancellationToken ct = default)
    {
        int lastProgress = 15;
        int pollCount = 0;

        while (!ct.IsCancellationRequested)
        {
            var status = await GetTaskStatusAsync(jobId, ct);
            pollCount++;

            // Debug logging: log every API response to see real return values
            var jobProgress = status.Progress;
            var progressStr = jobProgress != null
                ? $"files={jobProgress.Completed}/{jobProgress.Total} completed, {jobProgress.Failed} failed"
                : "null";
            var logMsg = $"[MinerU Poll #{pollCount}] Status={status.Status}, Progress={progressStr}";
            System.Diagnostics.Debug.WriteLine(logMsg);
            System.Console.WriteLine(logMsg);

            if (status.IsRunning)
            {
                // Map MinerU job status to our progress
                var minerUStatus = status.Status == "queued"
                    ? MinerUParseStatus.Queued
                    : MinerUParseStatus.Processing;

                var progress = minerUStatus == MinerUParseStatus.Queued ? 20 : 50;

                if (progress != lastProgress)
                {
                    lastProgress = progress;
                    onProgress?.Invoke(minerUStatus, progress, null);
                }
            }
            else if (status.IsCompleted || status.IsPartial)
            {
                onProgress?.Invoke(MinerUParseStatus.Downloading, 70, null);
                return;
            }
            else if (status.IsFailed || status.IsCanceled)
            {
                onProgress?.Invoke(MinerUParseStatus.Failed, -1, null);
                var errorMessage = status.GetErrorMessage() ?? "Unknown error";
                throw new MinerUServiceException($"MinerU parse job {status.Status}: {errorMessage}");
            }
            else
            {
                // Unknown status, treat as running
                if (35 != lastProgress)
                {
                    lastProgress = 35;
                    onProgress?.Invoke(MinerUParseStatus.Processing, 35, null);
                }
            }

            await Task.Delay(DefaultPollInterval, ct);
        }
    }

    /// <summary>
    /// Downloads the parse result zip via GET /v1/files/{file_id}/content.
    /// The zip file_id is resolved from the job's files[].output_files.zip.
    /// </summary>
    public async Task<string> DownloadResultAsync(string jobId, string sourcePdfPath, CancellationToken ct = default)
    {
        var job = await GetTaskStatusAsync(jobId, ct);

        var zipRef = job.Files
            .Select(f => f.OutputFiles?.Zip)
            .FirstOrDefault(z => z is not null && !string.IsNullOrEmpty(z.FileId));

        if (zipRef is null)
        {
            var error = job.GetErrorMessage();
            throw new MinerUServiceException(
                $"MinerU job '{jobId}' has no downloadable zip output (status: {job.Status})." +
                (error is null ? string.Empty : $" Error: {error}"));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/v1/files/{zipRef.FileId}/content");
        AddAuthHeader(request);
        using var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new MinerUServiceException(
                $"Failed to download MinerU result file (status: {(int)response.StatusCode}): {Truncate(body, 500)}");
        }

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
        return Path.Combine(_cacheDirectory, $"{docId}.zip");
    }

    /// <summary>
    /// Gets the extracted artifacts directory path for a given PDF.
    /// Artifacts are normalized to {cache}/{docId}/ by ExtractZipToDocDir.
    /// </summary>
    private string GetExtractedDirPath(string pdfPath)
    {
        return Path.Combine(_cacheDirectory, Path.GetFileNameWithoutExtension(pdfPath));
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
            if (dir is not null)
            {
                Directory.CreateDirectory(dir);
            }
            var data = new MinerUTaskIdRecord
            {
                TaskId = taskId,
                PdfPath = pdfPath,
                CreatedAt = DateTimeOffset.UtcNow.ToString("O")
            };
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                WriteIndented = true,
                TypeInfoResolver = SourceGenerationContext.Default
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

        if (status.IsCompleted || status.IsPartial)
        {
            // Job already finished (all or some files succeeded), just download
            onProgress?.Invoke(MinerUParseStatus.Downloading, 70);
            resultZip = await DownloadResultAsync(taskId, pdfPath, ct);
        }
        else if (status.IsFailed || status.IsCanceled)
        {
            onProgress?.Invoke(MinerUParseStatus.Failed, -1);
            var errorMessage = status.GetErrorMessage() ?? "Unknown error";
            throw new MinerUServiceException($"MinerU parse job {status.Status}: {errorMessage}");
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

        var docId = Path.GetFileNameWithoutExtension(pdfPath);
        // Artifacts are normalized to {cache}/{docId}/ (see ExtractZipToDocDir)
        var artifactsDir = Path.Combine(_cacheDirectory, docId);

        // If artifacts dir doesn't exist, extract from zip
        if (!Directory.Exists(artifactsDir))
        {
            try
            {
                artifactsDir = ExtractZipToDocDir(zipPath, docId);
            }
            catch
            {
                return null;
            }
        }

        var minerUDoc = MinerUJsonService.TryParseMinerUFromExtractedDir(artifactsDir);
        if (minerUDoc is null)
            return null;

        var markdownInfo = ExtractMarkdownFromDir(artifactsDir);

        return new MinerUParseResult
        {
            StructureDocument = minerUDoc,
            ZipPath = zipPath,
            Markdown = markdownInfo.markdown,
            PopoMarkdown = markdownInfo.popoMarkdown,
            ArtifactsDirectory = artifactsDir
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
                // Skip .zip files and the current document directory
                if (!dirName.EndsWith(".zip") && dirName != currentDocId)
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
    /// Extracts a result zip to {cache}/{docId}/, normalizing both the flat
    /// MinerU 4.0 layout and the legacy 3.x single-top-level-folder layout.
    /// Returns the artifacts directory path.
    /// </summary>
    private string ExtractZipToDocDir(string zipPath, string docId)
    {
        Directory.CreateDirectory(_cacheDirectory);

        var tempDir = Path.Combine(_cacheDirectory, $".extract_tmp_{Guid.NewGuid():N}");
        try
        {
            ZipFile.ExtractToDirectory(zipPath, tempDir, overwriteFiles: true);
        }
        catch (Exception ex)
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
            throw new MinerUServiceException($"Failed to extract parse result: {ex.Message}", ex);
        }

        // Legacy 3.x zips contain a single top-level folder (e.g. {docId}/hybrid_auto/...);
        // use that folder as the content root. Flat 4.0 zips use the extraction root.
        var entries = Directory.GetFileSystemEntries(tempDir);
        var contentRoot = tempDir;
        if (entries.Length == 1 && Directory.Exists(entries[0]))
        {
            contentRoot = entries[0];
        }

        var targetDir = Path.Combine(_cacheDirectory, docId);
        if (Directory.Exists(targetDir))
        {
            Directory.Delete(targetDir, recursive: true);
        }

        try
        {
            Directory.Move(contentRoot, targetDir);
        }
        catch (Exception ex)
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
            throw new MinerUServiceException($"Failed to move extracted parse result: {ex.Message}", ex);
        }

        // When the content root was a subfolder (legacy layout), remove the empty temp dir
        if (!ReferenceEquals(contentRoot, tempDir))
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }

        return targetDir;
    }

    /// <summary>
    /// Extracts the result zip to {cache}/{docId}/ and builds a MinerUParseResult.
    /// MinerU 4.0 zips are flat (markdown.md, middle_json.json, images at the root);
    /// legacy 3.x zips contain a single top-level {docId}/ folder. Both layouts are
    /// normalized to {cache}/{docId}/... so downstream code sees one consistent path.
    /// </summary>
    private async Task<MinerUParseResult> BuildParseResultAsync(
        string zipPath,
        string sourcePdfPath,
        Action<MinerUParseStatus, int>? onProgress,
        CancellationToken ct)
    {
        onProgress?.Invoke(MinerUParseStatus.ParsingResult, 85);

        var docId = Path.GetFileNameWithoutExtension(sourcePdfPath);
        var artifactsDir = await Task.Run(() => ExtractZipToDocDir(zipPath, docId), ct);

        // Parse the extracted directory
        var minerUDoc = await Task.Run(() =>
            MinerUJsonService.TryParseMinerUFromExtractedDir(artifactsDir), ct);

        // Extract markdown files
        var markdownInfo = ExtractMarkdownFromDir(artifactsDir);

        return new MinerUParseResult
        {
            StructureDocument = minerUDoc,
            ZipPath = zipPath,
            Markdown = markdownInfo.markdown,
            PopoMarkdown = markdownInfo.popoMarkdown,
            ArtifactsDirectory = artifactsDir
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