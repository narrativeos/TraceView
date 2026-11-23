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
/// An agent-proposed re-orchestration of the document TOC plus the body alignment:
/// a new flat TOC tree and a blockId -> toc-node mapping covering every non-discarded
/// block exactly once. Round-trips through the agent via propose_alignment_patch;
/// application is a deterministic UI action (see TocAlignmentPatcher).
/// </summary>
public sealed class TocAlignmentPatch
{
    /// <summary>Current patch wire format.</summary>
    public const string SchemaVersionValue = "1.0";

    /// <summary>Patch format version.</summary>
    public string SchemaVersion { get; set; } = SchemaVersionValue;

    /// <summary>Proposed TOC nodes in reading order (stable within a page).</summary>
    public List<ProposedTocNode> Nodes { get; set; } = [];

    /// <summary>Body alignment: each block assigned to exactly one node.</summary>
    public List<BlockAssignment> BlockAssignments { get; set; } = [];
}

/// <summary>
/// One proposed TOC entry. <see cref="Id"/> is a patch-local handle used only to
/// reference the node from <see cref="BlockAssignment.NodeId"/>; it is not persisted.
/// </summary>
public sealed class ProposedTocNode
{
    /// <summary>Patch-local unique id (e.g. "n1").</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Entry title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Hierarchical level, 1-based; may nest at most one level per node.</summary>
    public int Level { get; set; }

    /// <summary>Starting page (1-based).</summary>
    public int Page { get; set; }
}

/// <summary>
/// Maps one MinerU block (by its integer <see cref="BlockId"/>) to a proposed TOC node.
/// </summary>
public sealed class BlockAssignment
{
    /// <summary>MinerUBlock.Id.</summary>
    public int BlockId { get; set; }

    /// <summary><see cref="ProposedTocNode.Id"/> this block belongs under.</summary>
    public string NodeId { get; set; } = string.Empty;
}
