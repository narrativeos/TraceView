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

namespace Caly.Core.Models.EntityProcessors;

/// <summary>
/// Severity level for entity validation results.
/// </summary>
public enum EntityValidationSeverity
{
    /// <summary>Informational — entity passes validation with minor notes.</summary>
    Info,

    /// <summary>Warning — entity is ambiguous and may need disambiguation.</summary>
    Warning,

    /// <summary>Error — entity is clearly invalid and should be discarded.</summary>
    Error
}

/// <summary>
/// Result of entity validation.
/// Determines whether an entity passes the pattern-based filter for its type.
/// </summary>
public class EntityValidationResult
{
    /// <summary>Whether the entity is considered valid (passes pattern filter).</summary>
    public bool IsValid { get; set; }

    /// <summary>Human-readable reason for the validation result.</summary>
    public string? Reason { get; set; }

    /// <summary>Severity level of the result.</summary>
    public EntityValidationSeverity Severity { get; set; } = EntityValidationSeverity.Info;

    /// <summary>Convenience: creates a valid result.</summary>
    public static EntityValidationResult Valid(string? reason = null) =>
        new() { IsValid = true, Reason = reason, Severity = EntityValidationSeverity.Info };

    /// <summary>Convenience: creates an invalid result with Error severity.</summary>
    public static EntityValidationResult Invalid(string reason) =>
        new() { IsValid = false, Reason = reason, Severity = EntityValidationSeverity.Error };

    /// <summary>Convenience: creates an ambiguous result with Warning severity.</summary>
    public static EntityValidationResult Ambiguous(string reason) =>
        new() { IsValid = true, Reason = reason, Severity = EntityValidationSeverity.Warning };
}

/// <summary>
/// Result of entity enrichment — the output of calling an external API or rule engine
/// to enhance an entity with authoritative metadata.
/// </summary>
public class EntityEnrichmentResult
{
    /// <summary>Canonical/normalized name for the entity (e.g., Wikidata label, OSM display_name).</summary>
    public string? EnrichedText { get; set; }

    /// <summary>External identifier (e.g., Wikidata Q-ID, OSM node/way/relation ID).</summary>
    public string? ExternalId { get; set; }

    /// <summary>Type-specific metadata key-value pairs.</summary>
    /// <example>For Location: { "lat": 39.9, "lon": 116.4, "osm_type": "relation" }</example>
    /// <example>For Person: { "birth_year": 1904, "occupation": "writer" }</example>
    /// <example>For Temporal: { "iso_8601": "2024-03-15", "calendar": "gregorian" }</example>
    public Dictionary<string, object> Metadata { get; set; } = new();

    /// <summary>Confidence score for the enrichment result (0.0 – 1.0).</summary>
    public double Confidence { get; set; }

    /// <summary>The processor that produced this result (e.g., "Nominatim", "Wikidata").</summary>
    public string? Source { get; set; }
}

/// <summary>
/// Context provided to an entity processor during enrichment.
/// Contains related entities that have already been processed and cross-entity relationships.
/// </summary>
public class EntityContext
{
    /// <summary>
    /// Parent/dependency entities that have already been enriched.
    /// Keyed by entity category for fast lookup.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<SemanticEntity>> EnrichedEntities { get; set; }
        = new Dictionary<string, IReadOnlyList<SemanticEntity>>();

    /// <summary>Entity relations extracted so far (across all types).</summary>
    public IReadOnlyList<EntityRelation> Relations { get; set; } = new List<EntityRelation>();

    /// <summary>The block this entity belongs to.</summary>
    public SemanticBlockResult Block { get; set; } = null!;

    /// <summary>
    /// Pre-parsed GeoJSON location features from locations.geojson, keyed by normalized location name.
    /// Null if GeoJSON file is not available. Used by LocationProcessor to avoid re-calling Nominatim.
    /// </summary>
    public IReadOnlyDictionary<string, GeoJsonFeature>? GeoJsonLookup { get; set; }
}

/// <summary>
/// Base class for entity-type-specific relations.
/// Each entity type defines its own relation types, but they share this common shape
/// so the pipeline can store and query them uniformly.
/// </summary>
public class EntityRelation
{
    /// <summary>Type-specific relation type string (e.g., "ParentChild", "Alias", "Before").</summary>
    public string RelationType { get; set; } = string.Empty;

    /// <summary>The entity ID that is the source of this relation.</summary>
    public string SourceEntityId { get; set; } = string.Empty;

    /// <summary>The entity ID that is the target of this relation.</summary>
    public string TargetEntityId { get; set; } = string.Empty;

    /// <summary>Confidence score for this relation (0.0 – 1.0).</summary>
    public double Confidence { get; set; }

    /// <summary>Evidence string — the dependency parse or rule that produced this relation.</summary>
    public string? Evidence { get; set; }
}
