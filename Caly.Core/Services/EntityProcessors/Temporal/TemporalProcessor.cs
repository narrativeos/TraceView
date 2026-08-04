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
using System.Threading;
using System.Threading.Tasks;
using Caly.Core.Models;
using Caly.Core.Models.EntityProcessors;
using Caly.Core.Services.EntityProcessors.Temporal.Models;

namespace Caly.Core.Services.EntityProcessors.Temporal;

/// <summary>
/// Temporal entity processor — implements <see cref="IEntityProcessor"/> for the "DATE" entity type.
///
/// Pure rule engine (no external API):
/// - Validates via <see cref="TemporalValidator"/> (5-type classification).
/// - Normalizes via <see cref="TemporalNormalizer"/> (HanLP passthrough + era table lookup + relative offset).
/// - Extracts intra-block relations via <see cref="TemporalRelationExtractor"/> (dep parse → Before/After/Contains).
/// - Builds cross-block timeline via <see cref="TemporalRelationExtractor.BuildTimeline"/>.
/// </summary>
public sealed class TemporalProcessor : IEntityProcessor
{
    private readonly TemporalNormalizer _normalizer = new();

    /// <inheritdoc/>
    public string EntityType => "DATE";

    /// <inheritdoc/>
    public IReadOnlySet<string> DependsOn { get; } = new HashSet<string>();

    /// <inheritdoc/>
    public bool SupportsCrossBlockResolution => true;

    /// <inheritdoc/>
    public EntityValidationResult Validate(SemanticEntity entity, SemanticBlockResult block)
    {
        var (isValid, type, reason) = TemporalValidator.Validate(entity);

        // Store temporal type in entity metadata
        entity.EnrichedMetadata["DATE:temporal_type"] = new Dictionary<string, object>
        {
            ["type"] = type.ToString(),
            ["reason"] = reason ?? ""
        };

        if (!isValid)
            return EntityValidationResult.Invalid(reason ?? "Not a valid temporal expression");

        if (type == TemporalType.Relative)
            return EntityValidationResult.Ambiguous(reason ?? "Relative time (needs anchor)");

        return EntityValidationResult.Valid(reason);
    }

    /// <inheritdoc/>
    public Task<EntityEnrichmentResult> EnrichAsync(
        SemanticEntity entity,
        EntityContext context,
        CancellationToken ct)
    {
        var normalization = _normalizer.Normalize(entity);

        var result = new EntityEnrichmentResult
        {
            Source = normalization.Source ?? "TemporalNormalizer",
            Confidence = normalization.Type switch
            {
                TemporalType.Absolute => 0.95,
                TemporalType.Era => 0.85,
                TemporalType.Range => 0.80,
                TemporalType.Relative => 0.40,
                TemporalType.Vague => 0.10,
                _ => 0.0
            },
            EnrichedText = normalization.Iso8601 ?? normalization.Description,
            Metadata = new Dictionary<string, object>
            {
                ["iso_8601"] = normalization.Iso8601 ?? "",
                ["iso_start"] = normalization.IsoStart ?? "",
                ["iso_end"] = normalization.IsoEnd ?? "",
                ["temporal_type"] = normalization.Type.ToString(),
                ["precision"] = normalization.Precision.ToString(),
                ["needs_anchor"] = normalization.NeedsAnchor,
                ["source"] = normalization.Source ?? "",
                ["description"] = normalization.Description ?? "",
                ["original_text"] = entity.Text
            }
        };

        // Add era-specific metadata
        if (normalization.Type == TemporalType.Era)
        {
            result.Metadata["era_name"] = normalization.EraName ?? "";
            result.Metadata["era_year"] = normalization.EraYear ?? 0;
        }

        // Add relative offset metadata
        if (normalization.RelativeOffset != null)
        {
            result.Metadata["relative_offset"] = normalization.RelativeOffset;
        }

        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public IReadOnlyList<EntityRelation> ExtractRelations(
        IReadOnlyList<SemanticEntity> entities,
        SemanticBlockResult block)
    {
        return TemporalRelationExtractor.ExtractRelations(entities, block, _normalizer);
    }

    /// <inheritdoc/>
    public IReadOnlyList<EntityRelation> ResolveCrossBlock(
        IReadOnlyList<(SemanticEntity Entity, SemanticBlockResult Block)> allEntities,
        IReadOnlyList<SemanticBlockResult> allBlocks)
    {
        return TemporalRelationExtractor.BuildTimeline(allEntities, _normalizer);
    }
}
