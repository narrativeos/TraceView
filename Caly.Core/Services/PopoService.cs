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
    /// Default timeout for Popo task processing. MPS processing can take 60-180 minutes
    /// for a full document, so we allow 10 hours (600 minutes) to accommodate very large documents.
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(600);

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
            Timeout = TimeSpan.FromMinutes(630)
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
    /// <param name="docId">Optional document ID. Sent explicitly because MinerU 4.0 ZIPs
    /// are flat (middle_json.json + images/ at the top level), so the Popo server
    /// cannot infer the doc id from a single top-level directory anymore.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<string> SubmitTaskAsync(string zipPath, string model, string? docId = null, CancellationToken ct = default)
    {
        if (!File.Exists(zipPath))
            throw new PopoServiceException($"ZIP file not found: {zipPath}");

        var zipBytes = File.ReadAllBytes(zipPath);
        var zipFileName = Path.GetFileName(zipPath);

        // Manually build multipart body to ensure non-ASCII filenames are sent as raw UTF-8,
        // avoiding .NET's RFC 2047 encoding which python-multipart cannot decode.
        var boundary = "----CalyBoundary" + Guid.NewGuid().ToString("N");
        using var bodyStream = new MemoryStream();
        var utf8 = System.Text.Encoding.UTF8;

        var fileHeader = $"--{boundary}\r\n" +
                         $"Content-Disposition: form-data; name=\"file\"; filename=\"{zipFileName}\"\r\n" +
                         $"Content-Type: application/zip\r\n\r\n";
        bodyStream.Write(utf8.GetBytes(fileHeader));
        bodyStream.Write(zipBytes, 0, zipBytes.Length);
        bodyStream.Write(utf8.GetBytes("\r\n"));

        var fieldBlock = $"--{boundary}\r\n" +
                         $"Content-Disposition: form-data; name=\"model\"\r\n\r\n" +
                         $"{model}\r\n";
        bodyStream.Write(utf8.GetBytes(fieldBlock));

        if (!string.IsNullOrEmpty(docId))
        {
            var docIdBlock = $"--{boundary}\r\n" +
                             $"Content-Disposition: form-data; name=\"doc_id\"\r\n\r\n" +
                             $"{docId}\r\n";
            bodyStream.Write(utf8.GetBytes(docIdBlock));
        }

        bodyStream.Write(utf8.GetBytes($"--{boundary}--\r\n"));

        using var content = new ByteArrayContent(bodyStream.ToArray());
        content.Headers.TryAddWithoutValidation("Content-Type", $"multipart/form-data; boundary={boundary}");

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
    /// Progress callback that includes detailed status information.
    /// </summary>
    public interface IPopoProgressCallback
    {
        void OnProgress(PopoProcessStatus status, int progress, string? progressMessage = null);
    }

    /// <summary>
    /// Polls the task status until it completes or fails.
    /// Uses the actual progress percentage from the API's progress field.
    /// Includes timeout protection and detailed logging.
    /// Saves processing records to disk for debugging and recovery.
    /// </summary>
    /// <param name="taskId">Task ID to poll.</param>
    /// <param name="onProgress">Simple progress callback (status, percent).</param>
    /// <param name="detailedCallback">Optional callback with progress message details.</param>
    /// <param name="timeout">Maximum polling timeout. Default 600 minutes (10 hours).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task PollUntilCompleteAsync(
        string taskId,
        Action<PopoProcessStatus, int>? onProgress = null,
        IPopoProgressCallback? detailedCallback = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var effectiveTimeout = timeout ?? DefaultTimeout;
        var deadline = DateTime.UtcNow.Add(effectiveTimeout);
        int lastProgress = -1;
        int pollCount = 0;
        var progressRecords = new List<PopoProgressRecord>();

        // Initialize the first record
        var currentRecord = new PopoProgressRecord
        {
            TaskId = taskId,
            StartedAt = DateTime.UtcNow,
            Status = "pending"
        };
        progressRecords.Add(currentRecord);
        SaveProgressRecords(taskId, progressRecords);

        while (!ct.IsCancellationRequested)
        {
            // Check timeout
            if (DateTime.UtcNow > deadline)
            {
                currentRecord.EndedAt = DateTime.UtcNow;
                currentRecord.Status = "timeout";
                currentRecord.ErrorMessage = $"Timed out after {effectiveTimeout.TotalMinutes:F1} minutes (poll #{pollCount}, last progress: {lastProgress}%)";
                SaveProgressRecords(taskId, progressRecords);
                
                throw new PopoServiceException(
                    $"Popo task timed out after {effectiveTimeout.TotalMinutes:F1} minutes "
                    + $"(poll #{pollCount}, last progress: {lastProgress}%). "
                    + $"The server may be stuck processing your document. "
                    + $"Processing record saved to: {GetProgressRecordPath(taskId)}");
            }

            var status = await GetTaskStatusAsync(taskId, ct);
            pollCount++;

            if (status.IsRunning)
            {
                // Parse actual progress percentage from API response
                // Format: "[60%] Image-text association (1 chunks)"
                var apiProgress = status.ParseProgressPercent() ?? 35;
                var progressMessage = status.Progress;

                // Log polling activity
                System.Diagnostics.Debug.WriteLine(
                    $"[Popo] Poll #{pollCount}: status={status.Status}, progress={progressMessage ?? "N/A"}");

                // Update progress record
                currentRecord.Status = status.Status;
                currentRecord.Progress = apiProgress;
                currentRecord.ProgressMessage = progressMessage;
                currentRecord.UpdatedAt = DateTime.UtcNow;
                
                // Save progress record periodically (every 5% progress change or every 30 polls)
                if (apiProgress != lastProgress || pollCount % 30 == 0)
                {
                    SaveProgressRecords(taskId, progressRecords);
                }

                if (apiProgress != lastProgress)
                {
                    lastProgress = apiProgress;
                    onProgress?.Invoke(PopoProcessStatus.Processing, apiProgress);
                    detailedCallback?.OnProgress(PopoProcessStatus.Processing, apiProgress, progressMessage);
                }
            }
            else if (status.IsCompleted)
            {
                System.Diagnostics.Debug.WriteLine($"[Popo] Poll #{pollCount}: task completed!");
                
                // Finalize progress record
                currentRecord.Status = status.Status;
                currentRecord.Progress = 100;
                currentRecord.ProgressMessage = "Task completed";
                currentRecord.EndedAt = DateTime.UtcNow;
                SaveProgressRecords(taskId, progressRecords);
                
                onProgress?.Invoke(PopoProcessStatus.Downloading, 70);
                detailedCallback?.OnProgress(PopoProcessStatus.Downloading, 70, "Task completed, downloading result...");
                return;
            }
            else if (status.IsFailed)
            {
                System.Diagnostics.Debug.WriteLine($"[Popo] Poll #{pollCount}: task failed!");
                
                // Finalize progress record with error
                currentRecord.Status = status.Status;
                currentRecord.EndedAt = DateTime.UtcNow;
                currentRecord.ErrorMessage = status.GetErrorMessage() ?? "Unknown error";
                SaveProgressRecords(taskId, progressRecords);
                
                onProgress?.Invoke(PopoProcessStatus.Failed, -1);
                var errorMessage = status.GetErrorMessage() ?? "Unknown error";
                throw new PopoServiceException($"Popo task failed: {errorMessage}");
            }

            await Task.Delay(DefaultPollInterval, ct);
        }
    }

    /// <summary>
    /// Gets the file path for progress records for a given task.
    /// </summary>
    private string GetProgressRecordPath(string taskId)
    {
        var logsDir = Path.Combine(_cacheDirectory, "logs");
        return Path.Combine(logsDir, $"task_{taskId}_progress.json");
    }

    /// <summary>
    /// Saves progress records to disk as JSON.
    /// </summary>
    private void SaveProgressRecords(string taskId, List<PopoProgressRecord> records)
    {
        try
        {
            var logsDir = Path.Combine(_cacheDirectory, "logs");
            Directory.CreateDirectory(logsDir);
            
            var outputPath = GetProgressRecordPath(taskId);
            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                // Reflection-based serialization is disabled by default in .NET 10;
                // use the source generator resolver (see JsonSettingsService.cs).
                TypeInfoResolver = SourceGenerationContext.Default
            };
            var json = JsonSerializer.Serialize(records, jsonOptions);
            File.WriteAllText(outputPath, json);
            System.Diagnostics.Debug.WriteLine($"[Popo] Saved progress record to {outputPath}");
        }
        catch (Exception ex)
        {
            // Log but don't fail the main process
            System.Diagnostics.Debug.WriteLine($"[Popo] Failed to save progress record: {ex.Message}");
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
        // Save directly to popo/popo_result.json (no intermediate result_{docId} directory)
        var jsonPath = Path.Combine(_cacheDirectory, "popo_result.json");
        Directory.CreateDirectory(_cacheDirectory);
        File.WriteAllText(jsonPath, json);

        // Parse the wrapped response using JsonDocument + manual tree parsing.
        // IMPORTANT: Do NOT use JsonSerializer.Deserialize<PopoTaskResultResponse> with
        // reflection-based resolver (DefaultJsonTypeInfoResolver) because it fails to
        // correctly deserialize nested AnalysisTreeNode properties like img_path.
        // This is the same approach used by TryParsePopoResultJson for consistency.
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string taskIdResult = string.Empty;
        if (root.TryGetProperty("task_id", out var taskIdElem))
            taskIdResult = taskIdElem.GetString() ?? string.Empty;

        if (!root.TryGetProperty("result", out var resultElem))
        {
            throw new PopoServiceException("Popo result missing 'result' field.");
        }

        // Get status and error from result
        string status = string.Empty;
        string? error = null;
        if (resultElem.TryGetProperty("status", out var statusElem))
            status = statusElem.GetString() ?? string.Empty;
        if (resultElem.TryGetProperty("error", out var errorElem))
            error = errorElem.GetString();

        // Parse tree using ParseTreeNode (same as TryParsePopoResultJson)
        if (!resultElem.TryGetProperty("tree", out var treeElem))
        {
            throw new PopoServiceException(
                $"Popo result missing tree. Status: {status}, Error: {error}");
        }

        var treeRoot = PopoJsonService.ParseTreeNode(treeElem);
        if (treeRoot is null)
        {
            throw new PopoServiceException(
                $"Popo result tree is null. Status: {status}, Error: {error}");
        }

        // Build StructureDocument from the API tree
        var minerUDoc = PopoJsonService.BuildStructureDocumentFromTree(
            treeRoot,
            sourceDocId,
            taskIdResult);

        // Parse result.toc.entries (structured table of contents) — same as TryParsePopoResultJson
        if (resultElem.TryGetProperty("toc", out var tocElem) &&
            tocElem.ValueKind == JsonValueKind.Object &&
            tocElem.TryGetProperty("entries", out var entriesElem) &&
            entriesElem.ValueKind == JsonValueKind.Array)
        {
            var tocEntries = new List<TocEntry>();
            foreach (var entry in entriesElem.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object)
                    continue;

                string title = string.Empty;
                if (entry.TryGetProperty("linked_body_title", out var linkedTitleElem) &&
                    !string.IsNullOrEmpty(linkedTitleElem.GetString()))
                {
                    title = linkedTitleElem.GetString()!;
                }
                else if (entry.TryGetProperty("title", out var titleElem))
                {
                    title = titleElem.GetString() ?? string.Empty;
                }

                if (!string.IsNullOrEmpty(title))
                {
                    int level = 1;
                    if (entry.TryGetProperty("level", out var lvlElem) && lvlElem.ValueKind == JsonValueKind.Number)
                        level = lvlElem.GetInt32();

                    int page = 0;
                    if (entry.TryGetProperty("linked_body_page", out var pageElem) && pageElem.ValueKind == JsonValueKind.Number)
                    {
                        page = pageElem.GetInt32();
                    }
                    else if (entry.TryGetProperty("page", out var fallbackPageElem) && fallbackPageElem.ValueKind == JsonValueKind.Number)
                    {
                        page = fallbackPageElem.GetInt32();
                    }

                    tocEntries.Add(new TocEntry
                    {
                        Title = title,
                        Level = level,
                        Page = page,
                        Type = "toc"
                    });
                }
            }
            minerUDoc.TocEntries = tocEntries;
            System.Diagnostics.Debug.WriteLine($"[Popo] result.toc.entries parsed: {tocEntries.Count} entries for doc '{sourceDocId}'");
        }
        else
        {
            // Log toc availability for debugging — helps track whether API returns toc
            string tocStatus = "missing";
            if (resultElem.TryGetProperty("toc", out var tocCheck))
            {
                if (tocCheck.ValueKind == JsonValueKind.Null)
                    tocStatus = "present but null";
                else if (tocCheck.ValueKind == JsonValueKind.Object)
                {
                    if (!tocCheck.TryGetProperty("entries", out _))
                        tocStatus = "present but no entries field";
                    else
                        tocStatus = "present but entries is not an array";
                }
                else
                    tocStatus = $"present but unexpected type: {tocCheck.ValueKind}";
            }
            System.Diagnostics.Debug.WriteLine($"[Popo] result.toc: {tocStatus} for doc '{sourceDocId}' (no TOC entries available)");
        }

        // Supplement TocEntries from the tree so downstream consumers
        // (SectionPathResolver, NLP TOC unit, SECTION_REF anchors) see a
        // complete TOC even when the API returned a partial one.
        PopoJsonService.SupplementTocFromTree(minerUDoc);

        return minerUDoc;
    }

    /// <summary>
    /// Full processing flow: submit -> poll -> download result -> build StructureDocument.
    /// </summary>
    /// <param name="zipPath">Path to the MinerU ZIP file.</param>
    /// <param name="sourceDocId">Document ID for result caching.</param>
    /// <param name="model">Model name (e.g., "mineru"). Required by Popo API.</param>
    /// <param name="onProgress">Progress callback (status, percent 0-100).</param>
    /// <param name="detailedCallback">Optional callback with progress message details.</param>
    /// <param name="timeout">Maximum polling timeout. Default 600 minutes (10 hours).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<PopoProcessResult> ProcessAsync(
        string zipPath,
        string sourceDocId,
        string model = "mineru",
        Action<PopoProcessStatus, int>? onProgress = null,
        IPopoProgressCallback? detailedCallback = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default,
        string? projectPath = null)
    {
        // Step 1: Submit task (doc_id sent explicitly for MinerU 4.0 flat ZIPs)
        onProgress?.Invoke(PopoProcessStatus.Submitting, 10);
        var taskId = await SubmitTaskAsync(zipPath, model, sourceDocId, ct);

        // Step 2: Poll until complete (progress from API parsed automatically, with timeout protection)
        onProgress?.Invoke(PopoProcessStatus.Queued, 15);
        await PollUntilCompleteAsync(taskId, onProgress, detailedCallback, timeout, ct);

        // Step 3: Download and parse result (builds MinerUDocument from the API tree)
        onProgress?.Invoke(PopoProcessStatus.Downloading, 70);
        var minerUDoc = await DownloadAndParseResultAsync(taskId, sourceDocId, ct);

        onProgress?.Invoke(PopoProcessStatus.ParsingResult, 85);

        // Step 4: Use MinerU extracted directory as the image source instead of re-extracting from ZIP.
        // The popo_result.json contains img_path like "images/xxx.jpg", which is relative to the
        // hybrid_auto directory. By pointing to the MinerU extract directory, we avoid duplicating
        // image files and save disk space.
        // Directory structure: {projectPath}/mineru/{docId}/hybrid_auto/images/
        var artifactsDir = FindMinerUImagesDirectory(sourceDocId, projectPath);

        // Step 5: Save popo_result.json to project directory for persistence.
        // DownloadAndParseResultAsync saves to cache directory, but TryLoadPopoData reads from
        // project directory. Without this copy, reloading the project won't find popo_result.json
        // and will fall back to other parsing methods that may not have img_path populated.
        // Use read-then-write instead of File.Copy to avoid file lock issues on macOS.
        // Also inject _artifactsDir so image paths can be resolved without directory scanning.
        if (!string.IsNullOrEmpty(projectPath))
        {
            try
            {
                var projectPopoDir = Path.Combine(projectPath, "popo");
                var projectResultPath = Path.Combine(projectPopoDir, "popo_result.json");
                var cacheResultPath = Path.Combine(_cacheDirectory, "popo_result.json");
                if (File.Exists(cacheResultPath))
                {
                    Directory.CreateDirectory(projectPopoDir);
                    var content = File.ReadAllText(cacheResultPath);

                    // Inject _artifactsDir into the JSON for fast image resolution on reload
                    if (!string.IsNullOrEmpty(artifactsDir))
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(content);
                            var ms = new System.IO.MemoryStream();
                            using var writer = new System.Text.Json.Utf8JsonWriter(
                                ms,
                                new System.Text.Json.JsonWriterOptions { Indented = true });
                            writer.WriteStartObject();

                            // Write _artifactsDir first (relative path for portability)
                            var relativePath = artifactsDir;
                            if (artifactsDir.StartsWith(projectPath, StringComparison.OrdinalIgnoreCase))
                            {
                                relativePath = Path.GetRelativePath(projectPath, artifactsDir);
                            }
                            writer.WriteString("_artifactsDir", relativePath);

                            // Copy all other properties from the original JSON
                            foreach (var property in doc.RootElement.EnumerateObject())
                            {
                                writer.WritePropertyName(property.Name);
                                property.Value.WriteTo(writer);
                            }
                            writer.WriteEndObject();
                            writer.Flush();

                            content = System.Text.Encoding.UTF8.GetString(ms.ToArray());
                            System.Diagnostics.Debug.WriteLine($"[Popo] Injected _artifactsDir='{relativePath}' into popo_result.json");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[Popo] Failed to inject _artifactsDir: {ex.Message}");
                            // Continue with original content - _artifactsDir is optional
                        }
                    }

                    File.WriteAllText(projectResultPath, content);
                    System.Diagnostics.Debug.WriteLine($"[Popo] Saved popo_result.json to project: {projectResultPath}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Popo] Failed to save popo_result.json to project: {ex.Message}");
            }
        }

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
    /// Resolves the MinerU artifacts directory (the directory that contains images/)
    /// for a candidate doc directory. Supports both the legacy 3.x layout
    /// ({docDir}/hybrid_auto/images/) and the flat MinerU 4.0 layout
    /// ({docDir}/images/). Returns null when no image files are found.
    /// </summary>
    private static string? ResolveArtifactsDirWithImages(string docDir)
    {
        // Legacy 3.x first (hybrid_auto/), then flat MinerU 4.0 (images/ at doc root).
        foreach (var candidate in new[] { Path.Combine(docDir, "hybrid_auto"), docDir })
        {
            var imagesDir = Path.Combine(candidate, "images");
            if (!Directory.Exists(imagesDir))
                continue;
            var imageFiles = Directory.GetFiles(imagesDir, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg")
                .ToList();
            if (imageFiles.Count > 0)
                return candidate;
        }
        return null;
    }

    /// <summary>
    /// Finds the MinerU extracted images directory for a given document ID.
    /// Search order (optimized for speed):
    ///   1. {projectPath}/mineru/{subDir}/ (project extraction)
    ///   2. %LocalAppData%/Caly/mineru/{docId}/ (MinerUService cache)
    ///   3. ~/.TraceView/{docId}/mineru/{subDir}/ (legacy cache)
    /// Each candidate supports both the legacy 3.x layout ({dir}/hybrid_auto/images/)
    /// and the flat MinerU 4.0 layout ({dir}/images/).
    /// No AllDirectories fallback to avoid scanning thousands of files.
    /// </summary>
    /// <param name="docId">Document ID.</param>
    /// <param name="projectPath">Optional project path. If provided, searches {projectPath}/mineru/ first.</param>
    private string? FindMinerUImagesDirectory(string docId, string? projectPath = null)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[Popo] FindMinerUImagesDirectory: docId={docId}, projectPath={projectPath}");

            // 1: If projectPath is provided, search the project's mineru/ directory first
            // This is where MinerUService extracts the ZIP when a project is open.
            if (!string.IsNullOrEmpty(projectPath))
            {
                var mineruDir = Path.Combine(projectPath, "mineru");
                System.Diagnostics.Debug.WriteLine($"[Popo] Searching project mineru dir: {mineruDir}, exists={Directory.Exists(mineruDir)}");

                if (Directory.Exists(mineruDir))
                {
                    var subDirs = Directory.GetDirectories(mineruDir);
                    System.Diagnostics.Debug.WriteLine($"[Popo] Found {subDirs.Length} subdirectories in {mineruDir}");

                    // Scan all subdirectories (ZIP internal name may differ from docId)
                    foreach (var subDir in subDirs)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Popo] Checking subDir: {subDir}");
                        var artifactsDir = ResolveArtifactsDirWithImages(subDir);
                        if (artifactsDir is not null)
                        {
                            System.Diagnostics.Debug.WriteLine($"[Popo] SUCCESS: Returning {artifactsDir}");
                            return artifactsDir;
                        }
                    }
                }
            }

            // 2: Check MinerUService cache directory %LocalAppData%/Caly/mineru/{docId}/
            // This is where MinerUService extracts the ZIP files.
            {
                var minerUCacheBase = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var minerUCacheDir = Path.Combine(minerUCacheBase, "Caly", "mineru", docId);
                System.Diagnostics.Debug.WriteLine($"[Popo] Searching MinerUService cache: {minerUCacheDir}, exists={Directory.Exists(minerUCacheDir)}");
                var cacheArtifactsDir = ResolveArtifactsDirWithImages(minerUCacheDir);
                if (cacheArtifactsDir is not null)
                {
                    System.Diagnostics.Debug.WriteLine($"[Popo] SUCCESS (MinerUService cache): Returning {cacheArtifactsDir}");
                    return cacheArtifactsDir;
                }
            }

            // 3: Fallback to legacy cache directory ~/.TraceView/{docId}/mineru/
            var cacheBase = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".TraceView", docId);
            var mineruCacheDir = Path.Combine(cacheBase, "mineru");
            System.Diagnostics.Debug.WriteLine($"[Popo] Searching cache dir: {mineruCacheDir}, exists={Directory.Exists(mineruCacheDir)}");

            if (Directory.Exists(mineruCacheDir))
            {
                foreach (var subDir in Directory.GetDirectories(mineruCacheDir))
                {
                    var legacyArtifactsDir = ResolveArtifactsDirWithImages(subDir);
                    if (legacyArtifactsDir is not null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Popo] SUCCESS (legacy cache): Returning {legacyArtifactsDir}");
                        return legacyArtifactsDir;
                    }
                }
            }

            System.Diagnostics.Debug.WriteLine($"[Popo] FAILED: No images found anywhere");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Popo] ERROR in FindMinerUImagesDirectory: {ex}");
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

/// <summary>
/// Records the progress of a Popo processing task for debugging and recovery.
/// Saved to disk periodically during long-running tasks.
/// </summary>
public class PopoProgressRecord
{
    /// <summary>
    /// The task ID being processed.
    /// </summary>
    public string TaskId { get; set; } = string.Empty;

    /// <summary>
    /// Current status of the task (e.g., "pending", "processing", "completed", "failed", "timeout").
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Progress percentage (0-100).
    /// </summary>
    public int Progress { get; set; }

    /// <summary>
    /// Detailed progress message from the API.
    /// </summary>
    public string? ProgressMessage { get; set; }

    /// <summary>
    /// When the task started processing.
    /// </summary>
    public DateTime StartedAt { get; set; }

    /// <summary>
    /// When the task was last updated.
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// When the task completed or failed.
    /// </summary>
    public DateTime? EndedAt { get; set; }

    /// <summary>
    /// Error message if the task failed or timed out.
    /// </summary>
    public string? ErrorMessage { get; set; }
}
