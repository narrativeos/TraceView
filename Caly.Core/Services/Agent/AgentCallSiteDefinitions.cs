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
using Caly.Core.Services.Agent;

namespace Caly.Core.Services;

/// <summary>
/// The registry of call sites for shipped columns. Adding a column feature means
/// adding a definition here plus its local tools — no glue code in the MCP layer.
/// </summary>
internal static class AgentCallSiteDefinitions
{
    // Declared before use: static field initializers run in textual order.
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> EmptyToolBindings { get; } =
        new Dictionary<string, IReadOnlyList<string>>(System.StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Popo column: TOC re-orchestration and body alignment.
    /// Generation-type site: the agent proposes a structured patch; validation,
    /// diff preview and application are deterministic code paths outside the
    /// tool loop (propose returns a validation report, the user confirms the diff).
    /// Binds no MCP tools — everything reads the local Popo artifacts.
    /// </summary>
    public static AgentCallSiteDefinition PopoTocAlign { get; } = new()
    {
        Id = "popo.toc-align",
        DisplayName = "TOC 二次编排与正文对齐",
        Instructions =
            "You are a document structure editor. Work in this order: get_document_overview, " +
            "get_toc, then get_page_blocks for every page with content (get_block_content for " +
            "blocks whose preview is not decisive). Then submit exactly one alignment patch via " +
            "propose_alignment_patch: a new TOC tree plus a blockId→tocNode mapping covering " +
            "every non-discarded block exactly once, nodes in reading order. If the patch is " +
            "rejected, fix the listed errors and propose again. Never assume content you did not read.",
        // Tools bind to the run's StructureDocument via PopoTocAlignTools; validation is
        // the deterministic TocAlignmentPatcher (coverage + tree soundness).
        LocalTools = [],
        LocalToolsFactory = PopoTocAlignTools.Create,
        McpToolBindings = EmptyToolBindings,
        // propose_alignment_patch is read-only (returns a diff); the apply step is a
        // UI action, not a tool, so no tool here gates on approval.
        ApprovalRequiredTools = new HashSet<string>(System.StringComparer.Ordinal),
    };

    /// <summary>
    /// Popo column: TOC rule-pack tuning loop (contract: narrative-operator-MinerU-Popo
    /// docs/toc-self-learning-loop.md §2.5). Overrides are validated deterministically
    /// offline, every change gates on /toc/evaluate replay against the gold set, and the
    /// single write (retoc_document) requires human approval. The agent can never touch the
    /// global rule pack files — promotion of an override to the global pack is a human
    /// code-review step outside this site (§3 write-domain table).
    /// </summary>
    public static AgentCallSiteDefinition PopoTocTune { get; } = new()
    {
        Id = "popo.toc-tune",
        DisplayName = "TOC 规则调优",
        Instructions =
            "You tune the TOC rule pack for the current document. Strict loop: " +
            "(1) call get_toc_quality first to see level jumps and low-confidence entries; " +
            "(2) draft the smallest override with propose_rule_override and fix any rejected " +
            "rule ids until it is accepted; (3) gate it with evaluate_rules passing the same " +
            "override — the gold-set replay is the only gate that counts; (4) call " +
            "retoc_document with that override only when evaluate_rules passed AND " +
            "aggregate.regressions is empty, and only after the user approves the prompt — " +
            "a regression means the override is rejected outright, tune further instead; " +
            "(5) report the candidate fingerprint and leave any promotion of the override " +
            "to the global rule pack to a human. You have no file tools and must never edit " +
            "global rule pack files; a single-document override never becomes global on its " +
            "own. Never call retoc_document before an evaluate_rules with the same override " +
            "showed zero regressions.",
        // Tools bind to the run's StructureDocument and the Popo HTTP service via
        // TocRuleTuningTools; evaluation and validation are deterministic and local.
        LocalTools = [],
        LocalToolsFactory = TocRuleTuningTools.Create,
        McpToolBindings = EmptyToolBindings,
        // retoc_document rewrites the stored task result — the only write in this loop,
        // deny-by-default and approval-gated (contract §3).
        ApprovalRequiredTools = new HashSet<string>(System.StringComparer.Ordinal) { RetocDocumentToolName },
    };

    private const string RetocDocumentToolName = "retoc_document";

    /// <summary>
    /// Popo column: submit the processed structure to the Jiulu spatiotemporal
    /// knowledge hub. Execution-type site: the hub has no single publish tool —
    /// content lands as drafts (book, then chapters) and enters a manual audit
    /// queue via submit_publications_book_revision. Every mutation is approval-gated.
    /// </summary>
    public static AgentCallSiteDefinition PopoPushJiulu { get; } = new()
    {
        Id = "popo.push-jiulu",
        DisplayName = "推送 Popo 结果至九录文华",
        Instructions =
            "You submit the finished Popo structure of the current document to the Jiulu " +
            "hub. Flow: search_publications_books first to check for duplicates; if the " +
            "title already exists, stop and report. Otherwise create exactly one " +
            "add_publications_book_draft, then one add_corpus_chapter_draft per section " +
            "in reading order (title, serialnumber, page span, both content_markdown and " +
            "content_text). Only call submit_publications_book_revision when the user " +
            "explicitly asks to enter manual approval. Never delete. Report the book id " +
            "and chapter count back to the user.",
        // Tool names verified against the live jiulu_mcp tools/list (2026-11-16).
        McpToolBindings = new Dictionary<string, IReadOnlyList<string>>(System.StringComparer.OrdinalIgnoreCase)
        {
            ["jiulu_mcp"] =
            [
                SearchBooksToolName,
                AddBookDraftToolName,
                AddChapterDraftToolName,
                SubmitBookRevisionToolName,
            ],
        },
        ApprovalRequiredTools = new HashSet<string>(System.StringComparer.Ordinal)
        {
            AddBookDraftToolName,
            AddChapterDraftToolName,
            SubmitBookRevisionToolName,
        },
        MaxToolIterations = 8,
    };

    // Jiulu hub tool names (verified against tools/list, 2026-11-16).
    private const string SearchBooksToolName = "search_publications_books";
    private const string AddBookDraftToolName = "add_publications_book_draft";
    private const string AddChapterDraftToolName = "add_corpus_chapter_draft";
    private const string SubmitBookRevisionToolName = "submit_publications_book_revision";

    /// <summary>
    /// Every shipped call site by id. Declared last: static initializers run in textual order.
    /// </summary>
    public static IReadOnlyDictionary<string, AgentCallSiteDefinition> All { get; } =
        new Dictionary<string, AgentCallSiteDefinition>(System.StringComparer.Ordinal)
        {
            [PopoTocAlign.Id] = PopoTocAlign,
            [PopoTocTune.Id] = PopoTocTune,
            [PopoPushJiulu.Id] = PopoPushJiulu,
        };
}
