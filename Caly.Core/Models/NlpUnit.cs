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

using System.Collections.Generic;

namespace Caly.Core.Models;

/// <summary>
/// A single unit of text sent to the NLP for analysis
/// (entity-extraction-quality-spec §13.3 / §14.2 B1).
///
/// The unit collector (<c>NlpUnitCollector</c>) produces these from the Popo
/// tree; <c>SemanticAnalysisService.AnalyzeAsync(NlpUnit)</c> consumes them.
/// The Popo tree is the single traceability trunk: every unit carries the
/// block-level provenance (source_block_ids / block_ids / locations) of the
/// content it covers, so entity spans can be traced back to PDF regions.
/// </summary>
public sealed class NlpUnit
{
    /// <summary>
    /// Final NLP request text: F0-sanitized content with the section path
    /// prepended as a 【...】 prefix (see <see cref="PrefixLen"/>).
    /// Ready to send — the service must not re-sanitize or re-prefix it.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Length of the 【section path】 prefix at the start of <see cref="Text"/>
    /// (0 when no prefix was applied). Entity/token spans in the NLP response
    /// are relative to <see cref="Text"/>; subtract this value to map them
    /// back onto the unit's content (Text[PrefixLen..]).
    /// </summary>
    public int PrefixLen { get; set; }

    /// <summary>
    /// Unit type: "text" | "table_data" | "table_kv" | "caption" | "toc".
    /// For units produced by the legacy MinerU block path, this is the MinerU
    /// block type (e.g. "text", "table", "title").
    /// </summary>
    public string UnitType { get; set; } = "text";

    /// <summary>
    /// Popo node id (NodeId) of the origin node, when the unit derives from a
    /// tree node. Null for TOC batch units and degraded block units whose
    /// origin is a MinerU block only.
    /// </summary>
    public string? OriginNodeId { get; set; }

    /// <summary>
    /// 0-based inclusive [start, end] range of &lt;|txt_split|&gt; segments of
    /// the origin node covered by this unit. Null = whole node (or a
    /// non-segmented unit such as table/toc/caption).
    /// </summary>
    public int[]? SegmentRange { get; set; }

    /// <summary>
    /// MinerU middle.json block UUIDs covered by this unit (traceability key
    /// for the PDF overlay cross-highlighting).
    /// </summary>
    public List<string> SourceBlockIds { get; set; } = new();

    /// <summary>
    /// Integer block ids (MinerU order ids) covered by this unit.
    /// </summary>
    public List<int> BlockIds { get; set; } = new();

    /// <summary>
    /// bbox + page (+ row_range for table sub-nodes) of the covered blocks.
    /// Parallel to <see cref="SourceBlockIds"/> when both are populated.
    /// </summary>
    public List<LocationEntry> Locations { get; set; } = new();

    /// <summary>
    /// Section path (e.g. "第5章 试验 &gt; 5.13 绝缘电阻试验") that was
    /// prepended to the NLP request text. Null when no prefix was applied.
    /// </summary>
    public string? SectionPath { get; set; }

    /// <summary>
    /// Row range [start, end] within the source table. Only populated for
    /// table sub-units (spec §13.4 row-level granularity); null for whole
    /// table units and all non-table units.
    /// </summary>
    public int[]? RowRange { get; set; }
}
