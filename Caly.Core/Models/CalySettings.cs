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
using System.Text.Json.Serialization;

namespace Caly.Core.Models;

public sealed class CalySettings
{
    public static readonly CalySettings Default = new CalySettings()
    {
        Width = 1000,
        Height = 500,
        PaneSize = 350,
        Debug = null,
        MinerUBaseUrl = "http://192.168.1.100:8401",
        MinerUTier = "standard",
        MinerUOcrMode = "auto",
        MinerUEnabled = true,
        PopoBaseUrl = "http://192.168.1.100:8412",
        PopoOutputEpub = true
    };

    // TODO - Add version for compatibility checks

    public int Width { get; set; }

    public int Height { get; set; }

    public int Left { get; set; }

    public int Top { get; set; }

    public bool IsMaximised { get; set; }

    public int PaneSize { get; set; }

    public bool ShowPdfLogs { get; set; }

    /// <summary>
    /// When true, scanned pages whose paper has a warm/pink cast are color-corrected
    /// (per-page white balance) so their background reads as neutral white.
    /// </summary>
    public bool ScanWhiteBalance { get; set; } = false;

    public CalySettingsDebug? Debug { get; set; }

    // ========== MinerU Configuration ==========

    /// <summary>
    /// MinerU service base URL (e.g., "http://192.168.1.100:8401")
    /// </summary>
    public string MinerUBaseUrl { get; set; } = "http://192.168.1.100:8401";

    /// <summary>
    /// MinerU 4.1 V1 API tier: "flash", "basic", "standard" (default) or "advanced".
    /// </summary>
    public string MinerUTier { get; set; } = "standard";

    /// <summary>
    /// MinerU 4.1 V1 API parse engine (<c>ocr_mode</c>): "auto" (default, picks the
    /// parsing branch per page), "txt" (native text layer for the whole document) or
    /// "ocr" (forced OCR for the whole document).
    /// </summary>
    public string MinerUOcrMode { get; set; } = "auto";

    /// <summary>
    /// API key for the MinerU "pro" tier (sent as a Bearer token).
    /// </summary>
    public string MinerUApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Whether MinerU AI parsing is enabled
    /// </summary>
    public bool MinerUEnabled { get; set; } = true;

    // ========== Popo Configuration ==========

    /// <summary>
    /// Popo service base URL (e.g., "http://192.168.1.100:8412")
    /// </summary>
    public string PopoBaseUrl { get; set; } = "http://192.168.1.100:8412";

    /// <summary>
    /// Popo model name (e.g., "mineru", "monkeyocr", "PaddleOCR-VL-1.5", "dolphin", "glm-ocr")
    /// </summary>
    public string PopoModel { get; set; } = "mineru";

    /// <summary>
    /// Ask the Popo server to also render {doc_id}.epub into the result artifact
    /// (API parameter "output_epub"). Default on: the format is fixed at submission
    /// time, so turning it off means the EPUB can only be had by re-running the pipeline.
    /// </summary>
    /// <remarks>
    /// The settings file is written with DefaultIgnoreCondition = WhenWritingDefault, which
    /// drops any property holding its default value — for a setting that defaults to true that
    /// would silently flip a user's "off" back to "on" on the next start. Writing it always
    /// keeps the choice persistent.
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool PopoOutputEpub { get; set; } = true;

    // ========== NLP Pipeline Configuration ==========

    /// <summary>
    /// When true, semantic analysis consumes NLP units collected from the Popo
    /// tree (NlpUnitCollector) instead of the legacy MinerU block path.
    /// Default: false (legacy behavior).
    /// </summary>
    public bool UseNlpUnitPipeline { get; set; } = false;

    // ========== GeoLibre Configuration ==========

    /// <summary>
    /// GeoLibre base URL for map visualization (e.g., "https://web.geolibre.app")
    /// </summary>
    public string GeoLibreBaseUrl { get; set; } = "https://web.geolibre.app";

    // ========== Project Configuration ==========

    /// <summary>
    /// Root directory for all TraceView projects. Each PDF creates a project folder here.
    /// Default: ~/.TraceView
    /// </summary>
    public string ProjectHome { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".TraceView");

    public sealed class CalySettingsDebug
    {
        public bool Render { get; set; }
        public bool Layout { get; set; }
        public bool Fps { get; set; }
        public bool DirtyRects { get; set; }
        public bool LayoutAnalysis { get; set; }
    }
    
    public enum CalySettingsProperty
    {
        PaneSize = 0,
        ScanWhiteBalance = 1
    }
}
