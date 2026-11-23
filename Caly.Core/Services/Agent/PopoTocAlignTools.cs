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
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Caly.Core.Models;
using Microsoft.Extensions.AI;

namespace Caly.Core.Services.Agent;

/// <summary>
/// Per-run scratch space for the popo.toc-align call site: serves the read tools and
/// holds the last accepted patch + validation so the UI can render a diff and apply.
/// All output is built with <see cref="JsonNode"/> — AOT-safe, no anonymous types.
/// </summary>
internal sealed class PopoTocAlignSession
{
    private const int PreviewChars = 140;
    private const int MaxContentChars = 12_000;

    private readonly StructureDocument _document;

    public PopoTocAlignSession(StructureDocument document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
    }

    /// <summary>Last patch that passed validation, or null.</summary>
    public TocAlignmentPatch? ProposedPatch { get; private set; }

    /// <summary>Validation of the last proposal (accepted or not).</summary>
    public TocAlignmentValidationResult? LastValidation { get; private set; }

    // ------------------------------------------------------------------ tools

    public string GetDocumentOverview()
    {
        var blocks = _document.GetAllBlocks();
        return new JsonObject
        {
            ["docId"] = _document.DocId,
            ["modelName"] = _document.ModelName,
            ["pageCount"] = _document.PagesBlocks.Keys.DefaultIfEmpty(0).Max(),
            ["blockCount"] = blocks.Count,
            ["discardedBlockCount"] = blocks.Count(b =>
                string.Equals(b.BlockSource, "discarded", StringComparison.OrdinalIgnoreCase)),
            ["currentTocEntryCount"] = _document.TocEntries.Count,
        }.ToJsonString();
    }

    public string GetToc()
    {
        var entries = new JsonArray();
        for (var i = 0; i < _document.TocEntries.Count; i++)
        {
            var e = _document.TocEntries[i];
            entries.Add(new JsonObject
            {
                ["index"] = i,
                ["title"] = e.Title,
                ["level"] = e.Level,
                ["page"] = e.Page,
                ["type"] = e.Type,
            });
        }

        return entries.ToJsonString();
    }

    public string GetPageBlocks(int page)
    {
        if (!_document.PagesBlocks.TryGetValue(page, out var blocks))
        {
            return new JsonObject
            {
                ["error"] = $"Page {page} does not exist (valid: 1..{_document.PagesBlocks.Keys.DefaultIfEmpty(0).Max()}).",
            }.ToJsonString();
        }

        var items = new JsonArray();
        foreach (var block in blocks)
        {
            var preview = block.Content.Length <= PreviewChars
                ? block.Content
                : block.Content[..PreviewChars] + "…";
            items.Add(new JsonObject
            {
                ["id"] = block.Id,
                ["type"] = block.Type,
                ["source"] = block.BlockSource,
                ["chars"] = block.Content.Length,
                ["preview"] = preview,
            });
        }

        return new JsonObject { ["page"] = page, ["blocks"] = items }.ToJsonString();
    }

    public string GetBlockContent(int blockId)
    {
        var block = _document.GetAllBlocks().FirstOrDefault(b => b.Id == blockId);
        if (block is null)
        {
            return new JsonObject { ["error"] = $"Block {blockId} not found." }.ToJsonString();
        }

        var truncated = block.Content.Length > MaxContentChars;
        return new JsonObject
        {
            ["id"] = block.Id,
            ["page"] = block.Page,
            ["type"] = block.Type,
            ["content"] = truncated ? block.Content[..MaxContentChars] + "…" : block.Content,
            ["truncated"] = truncated,
        }.ToJsonString();
    }

    /// <summary>
    /// Accepts a patch (serialized <see cref="TocAlignmentPatch"/> JSON). Malformed or
    /// invalid patches are refused with the concrete error list so the model can
    /// correct and re-propose; only a valid patch is stored for the UI to apply.
    /// </summary>
    public string ProposeAlignmentPatch(string patchJson)
    {
        TocAlignmentPatch? patch;
        try
        {
            patch = JsonSerializer.Deserialize(patchJson, SourceGenerationContext.Default.TocAlignmentPatch);
        }
        catch (JsonException ex)
        {
            return new JsonObject
            {
                ["accepted"] = false,
                ["errors"] = new JsonArray($"patchJson is not valid JSON: {ex.Message}"),
            }.ToJsonString();
        }

        if (patch is null)
        {
            return new JsonObject
            {
                ["accepted"] = false,
                ["errors"] = new JsonArray("patchJson was empty."),
            }.ToJsonString();
        }

        var result = TocAlignmentPatcher.Validate(_document, patch);
        LastValidation = result;

        if (result.IsValid)
        {
            ProposedPatch = patch;
        }

        var response = new JsonObject
        {
            ["accepted"] = result.IsValid,
            ["stats"] = new JsonObject
            {
                ["totalBlocks"] = result.TotalBlocks,
                ["assignedBlocks"] = result.AssignedBlocks,
            },
        };

        if (result.Errors.Count > 0)
        {
            var errors = new JsonArray();
            foreach (var error in result.Errors)
            {
                errors.Add(error);
            }

            response["errors"] = errors;
        }

        if (result.Warnings.Count > 0)
        {
            var warnings = new JsonArray();
            foreach (var warning in result.Warnings)
            {
                warnings.Add(warning);
            }

            response["warnings"] = warnings;
        }

        return response.ToJsonString();
    }
}

/// <summary>
/// Builds the popo.toc-align tool surface for one run. Tools are bound to the run's
/// StructureDocument; the produced session is parked in the context so the invoking
/// UI can read ProposedPatch/LastValidation once the run finishes.
/// </summary>
internal static class PopoTocAlignTools
{
    /// <summary>Key of the <see cref="PopoTocAlignSession"/> in AgentToolContext.Items.</summary>
    public const string SessionItemKey = "popo.toc_align.session";

    public static IReadOnlyList<AIFunction> Create(AgentToolContext context)
    {
        var document = context.Document
            ?? throw new InvalidOperationException(
                "The popo.toc-align call site requires a StructureDocument in the run context.");

        var session = new PopoTocAlignSession(document);
        context.Items[SessionItemKey] = session;

        return
        [
            AIFunctionFactory.Create(session.GetDocumentOverview,
                name: "get_document_overview",
                description: "Document id, model, page count, block counts and current TOC size. Call first."),
            AIFunctionFactory.Create(session.GetToc,
                name: "get_toc",
                description: "The current table of contents as a JSON array: index, title, level, page, type."),
            AIFunctionFactory.Create(session.GetPageBlocks,
                name: "get_page_blocks",
                description: "JSON list of the blocks on one page: id, type, source (para/discarded), chars, content preview."),
            AIFunctionFactory.Create(session.GetBlockContent,
                name: "get_block_content",
                description: "Full text content of one block by id (may be truncated)."),
            AIFunctionFactory.Create(session.ProposeAlignmentPatch,
                name: "propose_alignment_patch",
                description: "Submit the alignment patch: a JSON TocAlignmentPatch object (schemaVersion 1.0, " +
                             "nodes with unique id/title/level/page in reading order, blockAssignments giving " +
                             "every non-discarded block exactly one node). Returns accepted true/false; on " +
                             "false, fix the listed errors and propose again."),
        ];
    }
}
