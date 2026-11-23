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
using Caly.Core.Models;

namespace Caly.Core.Services.Agent;

/// <summary>
/// Deterministic verdict on a <see cref="TocAlignmentPatch"/>: hard errors block
/// acceptance (and are fed back to the agent for correction), warnings never do.
/// </summary>
/// <param name="IsValid">True when the patch can be applied.</param>
/// <param name="Errors">Blocking problems, human readable.</param>
/// <param name="Warnings">Non-blocking observations (e.g. an empty node).</param>
/// <param name="TotalBlocks">Non-discarded block count the coverage is computed over.</param>
/// <param name="AssignedBlocks">Non-discarded blocks covered by the patch.</param>
/// <param name="UnassignedBlockIds">Up to 40 uncovered non-discarded block ids.</param>
public sealed record TocAlignmentValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    int TotalBlocks,
    int AssignedBlocks,
    IReadOnlyList<int> UnassignedBlockIds);

/// <summary>
/// Validates and applies TOC alignment patches. This is the contract the agent is
/// held to: a patch is only accepted when it re-trees the TOC soundly and aligns
/// every non-discarded block exactly once — the agent never applies anything itself.
/// </summary>
internal static class TocAlignmentPatcher
{
    private const int MaxReportedUnassigned = 40;

    public static TocAlignmentValidationResult Validate(StructureDocument document, TocAlignmentPatch patch)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(patch);

        var errors = new List<string>();
        var warnings = new List<string>();

        if (!string.Equals(patch.SchemaVersion, TocAlignmentPatch.SchemaVersionValue, StringComparison.Ordinal))
        {
            errors.Add($"Unsupported schemaVersion '{patch.SchemaVersion}' (expected '{TocAlignmentPatch.SchemaVersionValue}').");
        }

        var maxPage = document.PagesBlocks.Keys.DefaultIfEmpty(0).Max();

        // --- nodes -----------------------------------------------------------
        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        var nodesById = new Dictionary<string, ProposedTocNode>(StringComparer.Ordinal);
        var previousLevel = 0;

        if (patch.Nodes.Count == 0)
        {
            errors.Add("The patch proposes no TOC nodes.");
        }

        foreach (var node in patch.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Id))
            {
                errors.Add("A node has an empty id.");
                continue;
            }

            if (!nodeIds.Add(node.Id))
            {
                errors.Add($"Duplicate node id '{node.Id}'.");
                continue;
            }

            nodesById[node.Id] = node;

            if (string.IsNullOrWhiteSpace(node.Title))
            {
                errors.Add($"Node '{node.Id}' has an empty title.");
            }

            if (node.Level < 1)
            {
                errors.Add($"Node '{node.Id}' has invalid level {node.Level} (levels are 1-based).");
            }
            else if (node.Level > previousLevel + 1)
            {
                errors.Add($"Node '{node.Id}' jumps from level {previousLevel} to {node.Level}; a tree may nest at most one level at a time.");
            }

            if (node.Page < 1 || node.Page > maxPage)
            {
                errors.Add($"Node '{node.Id}' starts at page {node.Page}, outside the document range 1..{maxPage}.");
            }

            if (node.Level >= 1)
            {
                previousLevel = node.Level;
            }
        }

        // --- assignments -----------------------------------------------------
        var blocksById = document.GetAllBlocks()
            .GroupBy(b => b.Id)
            .ToDictionary(g => g.Key, g => g.First());

        var assignedOnce = new HashSet<int>();
        var assignedByNode = new Dictionary<string, int>(StringComparer.Ordinal);
        var discardedAssignments = 0;

        foreach (var assignment in patch.BlockAssignments)
        {
            if (!blocksById.TryGetValue(assignment.BlockId, out var block))
            {
                errors.Add($"Assignment references unknown block {assignment.BlockId}.");
                continue;
            }

            if (!nodesById.ContainsKey(assignment.NodeId))
            {
                errors.Add($"Block {assignment.BlockId} references unknown node '{assignment.NodeId}'.");
                continue;
            }

            if (!assignedOnce.Add(assignment.BlockId))
            {
                errors.Add($"Block {assignment.BlockId} is assigned more than once.");
                continue;
            }

            assignedByNode.TryGetValue(assignment.NodeId, out var count);
            assignedByNode[assignment.NodeId] = count + 1;

            if (IsDiscarded(block))
            {
                discardedAssignments++;
            }
        }

        if (discardedAssignments > 0)
        {
            warnings.Add($"{discardedAssignments} discarded block(s) were assigned to nodes; they will still render as content under those entries.");
        }

        // --- coverage --------------------------------------------------------
        var requiredBlocks = blocksById.Values.Where(b => !IsDiscarded(b)).ToList();
        var unassigned = requiredBlocks.Where(b => !assignedOnce.Contains(b.Id)).Select(b => b.Id).ToList();

        foreach (var blockId in unassigned.Take(MaxReportedUnassigned))
        {
            errors.Add($"Block {blockId} is not assigned to any node.");
        }

        if (unassigned.Count > MaxReportedUnassigned)
        {
            errors.Add($"… and {unassigned.Count - MaxReportedUnassigned} more unassigned blocks.");
        }

        foreach (var node in patch.Nodes.Where(n => !string.IsNullOrWhiteSpace(n.Id) && !assignedByNode.ContainsKey(n.Id)))
        {
            warnings.Add($"Node '{node.Id}' ('{node.Title}') has no blocks under it.");
        }

        return new TocAlignmentValidationResult(
            errors.Count == 0,
            errors,
            warnings,
            requiredBlocks.Count,
            requiredBlocks.Count - unassigned.Count,
            unassigned.Take(MaxReportedUnassigned).ToList());
    }

    /// <summary>
    /// Deterministically applies an accepted patch: replaces the TOC and keeps the
    /// block alignment out-of-band (the mapping lives in the patch artifact itself;
    /// re-wiring TreeRoot/AggregationMap is a separate, riskier migration).
    /// </summary>
    public static void Apply(StructureDocument document, TocAlignmentPatch patch)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(patch);

        // Stable OrderBy: author order within one page is preserved.
        document.TocEntries = [.. patch.Nodes
            .OrderBy(n => n.Page)
            .Select(n => new TocEntry
            {
                Title = n.Title,
                Level = n.Level,
                Page = n.Page,
                Type = "title",
                Source = "agent",
            })];
    }

    private static bool IsDiscarded(MinerUBlock block) =>
        string.Equals(block.BlockSource, "discarded", StringComparison.OrdinalIgnoreCase);
}
