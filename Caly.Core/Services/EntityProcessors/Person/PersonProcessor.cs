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
/// Pure rule engine (Phase C v1 — no external Wikidata API):
/// - Validates via <see cref="PersonValidator"/> (name structure + surname check + title filter).
/// - Enriches with extracted core name and name type metadata.
/// - Extracts intra-block relations via <see cref="PersonRelationExtractor"/> (alias, family, colleague, title).
/// - Resolves cross-block coreference chains.
///
/// Wikidata disambiguation service is planned for Phase C+ (v2).
/// </summary>
public sealed class PersonProcessor : IEntityProcessor
{
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
    public Task<EntityEnrichmentResult> EnrichAsync(
        SemanticEntity entity,
        EntityContext context,
        CancellationToken ct)
    {
        var (_, nameType, coreName, _) = PersonValidator.Validate(entity);

        var result = new EntityEnrichmentResult
        {
            Source = "PersonValidator",
            Confidence = nameType switch
            {
                PersonNameType.FullChineseName => 0.90,
                PersonNameType.TitleSuffixed => 0.80,
                PersonNameType.ForeignName => 0.85,
                PersonNameType.SingleName => 0.40,
                _ => 0.30
            },
            EnrichedText = coreName ?? entity.Text,
            Metadata = new Dictionary<string, object>
            {
                ["person_name_type"] = nameType.ToString(),
                ["core_name"] = coreName ?? entity.Text,
                ["original_text"] = entity.Text,
                ["has_title_suffix"] = nameType == PersonNameType.TitleSuffixed,
                ["has_known_surname"] = coreName != null && PersonValidator.IsKnownSurname(coreName.Length >= 1 ? coreName[..1] : ""),
                // Wikidata fields (populated in Phase C+)
                ["wikidata_qid"] = "",
                ["wikidata_label"] = "",
                ["wikidata_description"] = ""
            }
        };

        return Task.FromResult(result);
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
}
