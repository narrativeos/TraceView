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

namespace Caly.Core.Models;

public sealed class CalySettings
{
    public static readonly CalySettings Default = new CalySettings()
    {
        Width = 1000,
        Height = 500,
        PaneSize = 350,
        Debug = null,
        MinerUBaseUrl = "http://localhost:8401",
        MinerUBackend = "hybrid-engine",
        MinerUEnabled = true,
        PopoBaseUrl = "http://localhost:8440"
    };

    // TODO - Add version for compatibility checks

    public int Width { get; set; }

    public int Height { get; set; }

    public int Left { get; set; }

    public int Top { get; set; }

    public bool IsMaximised { get; set; }

    public int PaneSize { get; set; }

    public bool ShowPdfLogs { get; set; }

    public CalySettingsDebug? Debug { get; set; }

    // ========== MinerU Configuration ==========

    /// <summary>
    /// MinerU service base URL (e.g., "http://localhost:8401")
    /// </summary>
    public string MinerUBaseUrl { get; set; } = "http://localhost:8401";

    /// <summary>
    /// MinerU backend engine name (e.g., "hybrid-engine", "docling", "marker")
    /// </summary>
    public string MinerUBackend { get; set; } = "hybrid-engine";

    /// <summary>
    /// Whether MinerU AI parsing is enabled
    /// </summary>
    public bool MinerUEnabled { get; set; } = true;

    // ========== Popo Configuration ==========

    /// <summary>
    /// Popo service base URL (e.g., "http://localhost:8440")
    /// </summary>
    public string PopoBaseUrl { get; set; } = "http://localhost:8440";

    /// <summary>
    /// Popo model name (e.g., "mineru", "monkeyocr", "PaddleOCR-VL-1.5", "dolphin", "glm-ocr")
    /// </summary>
    public string PopoModel { get; set; } = "mineru";

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
        PaneSize = 0
    }
}
