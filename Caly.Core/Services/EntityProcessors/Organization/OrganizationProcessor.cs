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
using Caly.Core.Services.EntityProcessors.Organization.Models;

namespace Caly.Core.Services.EntityProcessors.Organization;

/// <summary>
/// Organization entity processor — implements <see cref="IEntityProcessor"/> for the "ORGANIZATION" entity type.
///
/// Hybrid approach:
/// - Validates via <see cref="OrganizationValidator"/> (suffix detection + abbreviation classification).
/// - Enriches via <see cref="OrganizationDisambiguationService"/> (Wikidata API with RateLimiter).
/// - Extracts relations via <see cref="OrganizationRelationExtractor"/> (dep parse → FullName/Abbreviation/Sibling/Subsidiary).
/// - Resolves cross-block coreference chains.
/// </summary>
public sealed class OrganizationProcessor : IEntityProcessor, IDisposable
{
    private readonly OrganizationDisambiguationService _wikidataService = new();

    /// <inheritdoc/>
    public string EntityType => "ORGANIZATION";

    /// <inheritdoc/>
    public IReadOnlySet<string> DependsOn { get; } = new HashSet<string>();

    /// <inheritdoc/>
    public bool SupportsCrossBlockResolution => true;

    /// <inheritdoc/>
    public EntityValidationResult Validate(SemanticEntity entity, SemanticBlockResult block)
    {
        var (isValid, nameType, reason) = OrganizationValidator.Validate(entity);

        entity.EnrichedMetadata["ORGANIZATION:name_analysis"] = new Dictionary<string, object>
        {
            ["org_name_type"] = nameType.ToString(),
            ["reason"] = reason ?? ""
        };

        if (!isValid)
            return EntityValidationResult.Invalid(reason ?? "Not a valid organization name");

        if (nameType == OrganizationNameType.Abbreviation)
            return EntityValidationResult.Ambiguous(reason ?? "Likely abbreviation, needs disambiguation");

        return EntityValidationResult.Valid(reason);
    }

    /// <inheritdoc/>
    public async Task<EntityEnrichmentResult> EnrichAsync(
        SemanticEntity entity,
        EntityContext context,
        CancellationToken ct)
    {
        var (_, nameType, _) = OrganizationValidator.Validate(entity);

        var localConfidence = nameType switch
        {
            OrganizationNameType.FullFormalName => 0.90,
            OrganizationNameType.GeographicPrefix => 0.80,
            OrganizationNameType.Abbreviation => 0.50,
            OrganizationNameType.Generic => 0.40,
            _ => 0.30
        };

        var result = new EntityEnrichmentResult
        {
            Source = "OrganizationValidator",
            Confidence = localConfidence,
            EnrichedText = entity.Text,
            Metadata = new Dictionary<string, object>
            {
                ["org_name_type"] = nameType.ToString(),
                ["original_text"] = entity.Text
            }
        };

        // Wikidata lookup (non-fatal) — when found, it becomes the authoritative source
        try
        {
            var searchResults = await _wikidataService.SearchAsync(entity.Text, ct);
            if (searchResults.Count > 0)
            {
                var best = searchResults[0];
                result.Source = "Wikidata";
                result.ExternalId = best.QId;
                result.EnrichedText = best.Label;
                // Wikidata API confidence is authoritative; use it directly rather than
                // taking the max with the local heuristic. This ensures that a high-quality
                // Wikidata match properly elevates the overall confidence.
                result.Confidence = best.Confidence;

                result.Metadata["wikidata_qid"] = best.QId;
                result.Metadata["wikidata_label"] = best.Label;
                result.Metadata["wikidata_description"] = best.Description;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Log internally but don't leak details to the output
            System.Diagnostics.Debug.WriteLine($"[OrganizationProcessor] Wikidata query failed for '{entity.Text}': {ex.Message}");
            // Keep local validation result as fallback
        }

        return result;
    }

    /// <inheritdoc/>
    public IReadOnlyList<EntityRelation> ExtractRelations(
        IReadOnlyList<SemanticEntity> entities,
        SemanticBlockResult block)
    {
        return OrganizationRelationExtractor.ExtractRelations(entities, block);
    }

    /// <inheritdoc/>
    public IReadOnlyList<EntityRelation> ResolveCrossBlock(
        IReadOnlyList<(SemanticEntity Entity, SemanticBlockResult Block)> allEntities,
        IReadOnlyList<SemanticBlockResult> allBlocks)
    {
        return OrganizationRelationExtractor.ResolveCrossBlock(allEntities, allBlocks);
    }

    public void Dispose()
    {
        _wikidataService.Dispose();
    }
}
