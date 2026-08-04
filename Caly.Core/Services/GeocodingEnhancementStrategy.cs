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
using Caly.Core.Models;

namespace Caly.Core.Services;

/// <summary>
/// Defines how geocoding queries should be enhanced before calling Nominatim.
/// </summary>
public enum QueryEnhancement
{
    /// <summary>No enhancement — query the location name as-is.</summary>
    None,

    /// <summary>
    /// Text augmentation — prepend parent/context location name to the query.
    /// e.g., query "北京 北三环" instead of just "北三环".
    /// </summary>
    TextAugment,

    /// <summary>
    /// Spatial bias — geocode the parent location first, then use its bounding box
    /// to constrain the child location query. Requires sequential processing.
    /// </summary>
    SpatialBias,
}

/// <summary>
/// Defines how geocoding results should be filtered/reranked after receiving them from Nominatim.
/// </summary>
public enum ResultFiltering
{
    /// <summary>No filtering — return results as-is.</summary>
    None,

    /// <summary>
    /// Boost results that match the parent/context location's area.
    /// Results matching the parent get a confidence bonus; others get a penalty.
    /// No results are discarded.
    /// </summary>
    ParentAreaBoost,

    /// <summary>
    /// Strict filtering — only keep results whose display_name contains the parent
    /// location name. All other results are discarded.
    /// Used when the location is highly dependent on context (e.g., Attributive role).
    /// </summary>
    StrictParentFilter,
}

/// <summary>
/// A complete geocoding strategy for a single location, combining query enhancement
/// and result filtering decisions.
/// </summary>
public class GeocodingStrategy
{
    /// <summary>How to enhance the query before calling Nominatim.</summary>
    public QueryEnhancement QueryEnhancement { get; set; } = QueryEnhancement.None;

    /// <summary>How to filter/rerank results after receiving them.</summary>
    public ResultFiltering ResultFiltering { get; set; } = ResultFiltering.None;

    /// <summary>
    /// The parent/context location name (if applicable).
    /// Used for TextAugment query building and ParentAreaBoost filtering.
    /// </summary>
    public string? ContextLocationName { get; set; }

    /// <summary>
    /// Whether this location needs to wait for its parent location's geocoding
    /// to complete before it can be queried (for SpatialBias).
    /// </summary>
    public bool DependsOnParent => QueryEnhancement == QueryEnhancement.SpatialBias;

    /// <summary>
    /// Whether this strategy requires any enhancement at all.
    /// </summary>
    public bool RequiresEnhancement =>
        QueryEnhancement != QueryEnhancement.None || ResultFiltering != ResultFiltering.None;
}

/// <summary>
/// Determines geocoding enhancement strategies based on a location entity's
/// syntactic role and its NLP-derived relationships with other locations.
///
/// Strategy matrix:
/// ┌─────────────────────┬──────────────────┬──────────────────────┬──────────────────────────┐
/// │ Syntactic Role      │ Has Parent/Owner │ Query Enhancement    │ Result Filtering         │
/// ├─────────────────────┼──────────────────┼──────────────────────┼──────────────────────────┤
/// │ Attributive (定语)  │ Yes              │ TextAugment +        │ StrictParentFilter       │
/// │                     │                  │ SpatialBias          │ (discard non-parent area)│
/// │ Object/Predicative  │ Yes              │ TextAugment          │ ParentAreaBoost          │
/// │                     │                  │ (parallel OK)        │ (+0.3 / -0.2)            │
/// │ Adverbial/Subject   │ Yes              │ TextAugment          │ ParentAreaBoost          │
/// │                     │                  │ (parallel OK)        │ (+0.2 / -0.1, milder)    │
/// │ Any                 │ Coreference      │ Normalize to         │ Merge results            │
/// │                     │                  │ canonical name       │                          │
/// │ Any                 │ Parallel         │ None                 │ Cross-validate distance  │
/// │ Any                 │ None             │ None                 │ None                     │
/// └─────────────────────┴──────────────────┴──────────────────────┴──────────────────────────┘
/// </summary>
public static class GeocodingEnhancementStrategy
{
    /// <summary>
    /// Determines the geocoding strategy for a given location entity,
    /// based on its syntactic role and relationships with other locations.
    /// </summary>
    /// <param name="entity">The location entity to geocode.</param>
    /// <param name="relations">All location relations (from both intra-block and cross-block).</param>
    /// <returns>A GeocodingStrategy with enhancement and filtering decisions.</returns>
    public static GeocodingStrategy DecideStrategy(
        SemanticEntity entity,
        LocationRelationCollection? relations)
    {
        var strategy = new GeocodingStrategy();

        if (relations == null || !relations.HasRelations)
            return strategy;

        // Check for coreference normalization first (highest priority)
        var canonicalName = relations.FindCanonicalName(entity.Text);
        if (canonicalName != null)
        {
            strategy.QueryEnhancement = QueryEnhancement.TextAugment;
            strategy.ResultFiltering = ResultFiltering.None;
            strategy.ContextLocationName = canonicalName;
            return strategy;
        }

        // Find context-providing relation (ParentChild or Ownership)
        var contextRel = relations.FindContextProvider(entity.Text);

        if (contextRel == null)
        {
            // Check for parallel relationships (cross-validation only)
            var parallels = relations.FindParallels(entity.Text);
            if (parallels.Count > 0)
            {
                // Parallel: no query enhancement, but flag for cross-validation later
                strategy.QueryEnhancement = QueryEnhancement.None;
                strategy.ResultFiltering = ResultFiltering.None;
            }
            return strategy;
        }

        // Has a parent/owner context — decide strategy based on syntactic role
        var role = entity.SyntacticRole;
        var parentName = contextRel.SourceLocation;

        strategy.ContextLocationName = parentName;

        switch (role)
        {
            case LocationSyntacticRole.Attributive:
                // Attributive (定语): highly dependent on parent context
                // e.g., "北三环" modifying "北京" in "北京的北三环"
                // → SpatialBias: geocode parent first, use parent bbox, strict filter
                strategy.QueryEnhancement = QueryEnhancement.SpatialBias;
                strategy.ResultFiltering = ResultFiltering.StrictParentFilter;
                break;

            case LocationSyntacticRole.Object:
            case LocationSyntacticRole.Predicative:
                // Object/Predicative: action target or destination
                // → Text augment, moderate boost
                strategy.QueryEnhancement = QueryEnhancement.TextAugment;
                strategy.ResultFiltering = ResultFiltering.ParentAreaBoost;
                break;

            case LocationSyntacticRole.Adverbial:
            case LocationSyntacticRole.Subject:
                // Adverbial/Subject: more independent, but still benefits from context
                // → Text augment, mild boost (parallel OK, don't wait for parent)
                strategy.QueryEnhancement = QueryEnhancement.TextAugment;
                strategy.ResultFiltering = ResultFiltering.ParentAreaBoost;
                break;

            default:
                // Unknown role: conservative — text augment only
                strategy.QueryEnhancement = QueryEnhancement.TextAugment;
                strategy.ResultFiltering = ResultFiltering.ParentAreaBoost;
                break;
        }

        return strategy;
    }

    /// <summary>
    /// Builds the enhanced query string for a location with context.
    /// </summary>
    /// <param name="locationName">The original location name.</param>
    /// <param name="contextName">The parent/context location name.</param>
    /// <returns>Enhanced query string like "北京 北三环".</returns>
    public static string BuildEnhancedQuery(string locationName, string contextName)
    {
        if (string.IsNullOrWhiteSpace(contextName))
            return locationName;

        return $"{contextName} {locationName}";
    }

    /// <summary>
    /// Computes the confidence adjustment for results based on ParentAreaBoost filtering.
    /// Positive values boost results that match the parent context; negative values penalize those that don't.
    /// </summary>
    /// <param name="syntacticRole">The syntactic role of the location.</param>
    /// <returns>A (matchBoost, mismatchPenalty) tuple.</returns>
    public static (double MatchBoost, double MismatchPenalty) GetConfidenceAdjustment(string syntacticRole)
    {
        return syntacticRole switch
        {
            LocationSyntacticRole.Attributive => (0.3, -0.3),     // Strongest: Attributive needs context most
            LocationSyntacticRole.Object => (0.3, -0.2),           // Object: moderate
            LocationSyntacticRole.Predicative => (0.3, -0.2),      // Predicative: moderate
            LocationSyntacticRole.Adverbial => (0.2, -0.1),        // Adverbial: milder
            LocationSyntacticRole.Subject => (0.2, -0.1),          // Subject: milder
            _ => (0.15, -0.1)                                       // Unknown: conservative
        };
    }
}
