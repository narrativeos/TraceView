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

namespace Caly.Core.Models;

/// <summary>
/// Represents the progress stages of a MinerU document parsing operation.
/// Used to provide fine-grained progress feedback to the user during the async parse flow.
/// </summary>
public enum MinerUParseStatus
{
    /// <summary>Not started.</summary>
    Idle,

    /// <summary>Uploading PDF to MinerU service via POST /tasks (10%).</summary>
    Submitting,

    /// <summary>Task is queued in MinerU, waiting for GPU resources (15%).</summary>
    Queued,

    /// <summary>MinerU is actively parsing the document with AI models (35%).</summary>
    Processing,

    /// <summary>Downloading the parsed result zip from MinerU (70%).</summary>
    Downloading,

    /// <summary>Saving result to local cache directory (80%).</summary>
    Caching,

    /// <summary>Parsing MinerU output (middle.json) into PopoDocument (85%).</summary>
    ParsingResult,

    /// <summary>Parse completed successfully, PopoDocument is ready (100%).</summary>
    Completed,

    /// <summary>Parse failed with an error.</summary>
    Failed
}

/// <summary>
/// Helper extensions for <see cref="MinerUParseStatus"/>.
/// </summary>
public static class MinerUParseStatusExtensions
{
    /// <summary>
    /// Gets the progress percentage (0-100) for a given parse status.
    /// </summary>
    public static int ToProgressPercent(this MinerUParseStatus status)
    {
        return status switch
        {
            MinerUParseStatus.Idle => 0,
            MinerUParseStatus.Submitting => 10,
            MinerUParseStatus.Queued => 15,
            MinerUParseStatus.Processing => 35,
            MinerUParseStatus.Downloading => 70,
            MinerUParseStatus.Caching => 80,
            MinerUParseStatus.ParsingResult => 85,
            MinerUParseStatus.Completed => 100,
            MinerUParseStatus.Failed => -1,
            _ => 0
        };
    }

    /// <summary>
    /// Gets a user-friendly description of the parse status.
    /// </summary>
    public static string ToDisplayName(this MinerUParseStatus status)
    {
        return status switch
        {
            MinerUParseStatus.Idle => "Ready",
            MinerUParseStatus.Submitting => "Uploading document...",
            MinerUParseStatus.Queued => "Waiting in queue...",
            MinerUParseStatus.Processing => "AI parsing in progress...",
            MinerUParseStatus.Downloading => "Downloading results...",
            MinerUParseStatus.Caching => "Caching results...",
            MinerUParseStatus.ParsingResult => "Building document structure...",
            MinerUParseStatus.Completed => "Parse completed",
            MinerUParseStatus.Failed => "Parse failed",
            _ => "Unknown"
        };
    }

    /// <summary>
    /// Determines whether the status represents a terminal state (completed or failed).
    /// </summary>
    public static bool IsTerminal(this MinerUParseStatus status)
    {
        return status is MinerUParseStatus.Completed or MinerUParseStatus.Failed;
    }
}