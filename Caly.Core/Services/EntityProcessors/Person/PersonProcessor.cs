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
using Caly.Core.Services.EntityProcessors.Person.Models;

namespace Caly.Core.Services.EntityProcessors.Person;

/// <summary>
/// Person entity processor — implements <see cref="IEntityProcessor"/> for the "PERSON" entity type.
///
/// Hybrid approach:
/// - Validates via <see cref="PersonValidator"/> (name structure + surname check + title filter).
/// - Enriches via <see cref="PersonDisambiguationService"/> (Wikidata API with RateLimiter).
/// - Extracts intra-block relations via <see cref="PersonRelationExtractor"/> (alias, family, colleague, title).
/// - Resolves cross-block coreference chains.
/// </summary>
public sealed class PersonProcessor : IEntityProcessor, IDisposable
{
    private readonly PersonDisambiguationService _wikidataService = new();
    /// <inheritdoc/>
    public string EntityType => "PERSON";

    /// <inheritdoc/>
    public IReadOnlySet<string> DependsOn { get; } = new HashSet<string>();

    /// <inheritdoc/>
    public bool SupportsCrossBlockResolution => true;

    /// <inheritdoc/>
    public EntityValidationResult Validate(SemanticEntity entity, SemanticBlockResult block)
    {
        var (isValid, nameType, coreName, reason) = PersonValidator.Validate(entity);

        // Store person name analysis in entity metadata
        entity.EnrichedMetadata["PERSON:name_analysis"] = new Dictionary<string, object>
        {
            ["person_name_type"] = nameType.ToString(),
            ["core_name"] = coreName ?? entity.Text,
            ["reason"] = reason ?? ""
        };

        if (!isValid)
        {
            return nameType == PersonNameType.TitleOnly
                ? EntityValidationResult.Invalid(reason ?? "Pure title word, not a person name")
                : EntityValidationResult.Invalid(reason ?? "Cannot identify as a person name");
        }

        if (nameType == PersonNameType.SingleName)
            return EntityValidationResult.Ambiguous(reason ?? "Single character name");

        return EntityValidationResult.Valid(reason);
    }

    /// <inheritdoc/>
    public async Task<EntityEnrichmentResult> EnrichAsync(
        SemanticEntity entity,
        EntityContext context,
        CancellationToken ct)
    {
        var (_, nameType, coreName, _) = PersonValidator.Validate(entity);

        var localConfidence = nameType switch
        {
            PersonNameType.FullChineseName => 0.90,
            PersonNameType.TitleSuffixed => 0.80,
            PersonNameType.ForeignName => 0.85,
            PersonNameType.SingleName => 0.40,
            _ => 0.30
        };

        var result = new EntityEnrichmentResult
        {
            Source = "PersonValidator",
            Confidence = localConfidence,
            EnrichedText = coreName ?? entity.Text,
            Metadata = new Dictionary<string, object>
            {
                ["person_name_type"] = nameType.ToString(),
                ["core_name"] = coreName ?? entity.Text,
                ["original_text"] = entity.Text,
                ["has_title_suffix"] = nameType == PersonNameType.TitleSuffixed,
                ["has_known_surname"] = coreName != null && PersonValidator.IsKnownSurname(coreName.Length >= 1 ? coreName[..1] : ""),
                ["wikidata_qid"] = "",
                ["wikidata_label"] = "",
                ["wikidata_description"] = ""
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
                // Wikidata may return JSON-encoded labels like '{"value":"Lao She"}'
                // Parse them to extract the clean string value.
                result.EnrichedText = UnwrapJsonValue(best.Label) ?? best.Label;
                result.Confidence = best.Confidence;

                result.Metadata["wikidata_qid"] = best.QId;
                result.Metadata["wikidata_label"] = best.Label;
                result.Metadata["wikidata_description"] = best.Description;
            }
        }
        catch (OperationCanceledException ex)
        {
            // Timeout or cancellation — treat as non-fatal, keep local validation result
            System.Diagnostics.Debug.WriteLine($"[PersonProcessor] Wikidata query timed out/cancelled for '{entity.Text}': {ex.Message}");
        }
        catch (Exception ex)
        {
            // Log internally but don't leak details to the output
            System.Diagnostics.Debug.WriteLine($"[PersonProcessor] Wikidata query failed for '{entity.Text}': {ex.Message}");
            // Keep local validation result as fallback
        }

        return result;
    }

    /// <summary>
    /// Unwrap a JSON-encoded string value (e.g., '{"value":"Lao She"}')
    /// into a plain string. Returns null if the input is not valid JSON with a "value" field.
    /// </summary>
    private static string? UnwrapJsonValue(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return null;

        var trimmed = input.Trim();
        if (trimmed.Length < 2 || trimmed[0] != '{')
            return input; // Not JSON, return as-is

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(trimmed);
            if (doc.RootElement.TryGetProperty("value", out var val) && val.ValueKind == System.Text.Json.JsonValueKind.String)
                return val.GetString();
        }
        catch
        {
            // Not valid JSON, return as-is
        }

        return input;
    }

    /// <inheritdoc/>
    public IReadOnlyList<EntityRelation> ExtractRelations(
        IReadOnlyList<SemanticEntity> entities,
        SemanticBlockResult block)
    {
        return PersonRelationExtractor.ExtractRelations(entities, block);
    }

    /// <inheritdoc/>
    public IReadOnlyList<EntityRelation> ResolveCrossBlock(
        IReadOnlyList<(SemanticEntity Entity, SemanticBlockResult Block)> allEntities,
        IReadOnlyList<SemanticBlockResult> allBlocks)
    {
        return PersonRelationExtractor.ResolveCrossBlock(allEntities, allBlocks);
    }

    public void Dispose()
    {
        _wikidataService.Dispose();
    }
}
