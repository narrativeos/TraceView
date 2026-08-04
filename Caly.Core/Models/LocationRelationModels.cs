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
using System.Linq;
using System.Text.Json.Serialization;

namespace Caly.Core.Models;

/// <summary>
/// Types of relationships between location entities extracted from NLP analysis.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<LocationRelationType>))]
public enum LocationRelationType
{
    /// <summary>
    /// Parent-child relationship from dependency parsing (nn relation).
    /// The Source is the parent/context location, Target is the child/sub-location.
    /// e.g., "北京北三环" → Source="北京", Target="北三环"
    /// </summary>
    ParentChild,

    /// <summary>
    /// Parallel/conjunction relationship (conj relation).
    /// Both locations appear at the same level.
    /// e.g., "北京和上海" → Source="北京", Target="上海"
    /// </summary>
    Parallel,

    /// <summary>
    /// Ownership/possession relationship (assmod/assm relation).
    /// Source owns or contains the Target.
    /// e.g., "海淀区的五道口" → Source="海淀区", Target="五道口"
    /// </summary>
    Ownership,

    /// <summary>
    /// Coreference relationship (from HanLP coreference resolution).
    /// Source and Target refer to the same real-world location.
    /// e.g., "帝都" and "北京" → Source="帝都", Target="北京" (canonical)
    /// </summary>
    Coreference,
}

/// <summary>
/// Represents a relationship between two location entities,
/// extracted from NLP dependency parsing or coreference resolution.
/// </summary>
public class LocationRelation
{
    /// <summary>Type of the relationship.</summary>
    [JsonPropertyName("relation_type")]
    public LocationRelationType RelationType { get; set; }

    /// <summary>
    /// The source location name (parent, owner, first conjunct, or alias).
    /// </summary>
    [JsonPropertyName("source_location")]
    public string SourceLocation { get; set; } = string.Empty;

    /// <summary>
    /// The target location name (child, owned, second conjunct, or canonical name).
    /// </summary>
    [JsonPropertyName("target_location")]
    public string TargetLocation { get; set; } = string.Empty;

    /// <summary>
    /// The original dependency relation label from HanLP.
    /// e.g., "nn", "conj", "assmod", "assm".
    /// Empty for coreference relations.
    /// </summary>
    [JsonPropertyName("dep_rel")]
    public string DepRel { get; set; } = string.Empty;

    /// <summary>
    /// Confidence score (0.0 - 1.0) for this relationship.
    /// For dep-based relations: derived from token confidence.
    /// For coreference: from HanLP coref chain confidence.
    /// </summary>
    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    /// <summary>
    /// Source block IDs where this relationship was found.
    /// </summary>
    [JsonPropertyName("source_block_ids")]
    public List<string> SourceBlockIds { get; set; } = new();

    /// <summary>
    /// The original text evidence that shows this relationship.
    /// </summary>
    [JsonPropertyName("evidence")]
    public string Evidence { get; set; } = string.Empty;
}

/// <summary>
/// A collection of location relations with query helpers.
/// </summary>
public class LocationRelationCollection
{
    [JsonPropertyName("relations")]
    public List<LocationRelation> Relations { get; set; } = new();

    /// <summary>
    /// Finds the parent location for a given location name.
    /// Returns null if no parent relationship exists.
    /// </summary>
    public LocationRelation? FindParentOf(string locationName)
    {
        return Relations.FirstOrDefault(r =>
            r.RelationType == LocationRelationType.ParentChild &&
            r.TargetLocation == locationName);
    }

    /// <summary>
    /// Finds the owner location for a given location name.
    /// Returns null if no ownership relationship exists.
    /// </summary>
    public LocationRelation? FindOwnerOf(string locationName)
    {
        return Relations.FirstOrDefault(r =>
            r.RelationType == LocationRelationType.Ownership &&
            r.TargetLocation == locationName);
    }

    /// <summary>
    /// Finds any providing-context relation (ParentChild or Ownership) for a location.
    /// </summary>
    public LocationRelation? FindContextProvider(string locationName)
    {
        return Relations.FirstOrDefault(r =>
            (r.RelationType == LocationRelationType.ParentChild ||
             r.RelationType == LocationRelationType.Ownership) &&
            r.TargetLocation == locationName);
    }

    /// <summary>
    /// Finds parallel locations for a given location name.
    /// </summary>
    public List<LocationRelation> FindParallels(string locationName)
    {
        return Relations.Where(r =>
            r.RelationType == LocationRelationType.Parallel &&
            (r.SourceLocation == locationName || r.TargetLocation == locationName)
        ).ToList();
    }

    /// <summary>
    /// Finds the canonical name for a given alias via coreference.
    /// Returns null if no coreference relationship exists.
    /// </summary>
    public string? FindCanonicalName(string locationName)
    {
        var coref = Relations.FirstOrDefault(r =>
            r.RelationType == LocationRelationType.Coreference &&
            r.SourceLocation == locationName);
        return coref?.TargetLocation;
    }

    /// <summary>
    /// Gets all related location names for a given location (any relation type).
    /// </summary>
    public List<string> GetRelatedLocations(string locationName)
    {
        var related = new HashSet<string>();
        foreach (var rel in Relations)
        {
            if (rel.SourceLocation == locationName)
                related.Add(rel.TargetLocation);
            if (rel.TargetLocation == locationName)
                related.Add(rel.SourceLocation);
        }
        return related.ToList();
    }

    /// <summary>
    /// Returns true if there are any relations at all.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasRelations => Relations.Count > 0;

    /// <summary>
    /// Summary of relation counts by type.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Summary
    {
        get
        {
            if (Relations.Count == 0) return "无关系";
            var counts = Relations.GroupBy(r => r.RelationType)
                .Select(g => $"{g.Count()} {RelationTypeToDisplay(g.Key)}");
            return string.Join("，", counts);
        }
    }

    /// <summary>
    /// Converts a relation type to its Chinese display name.
    /// </summary>
    public static string RelationTypeToDisplay(LocationRelationType type) => type switch
    {
        LocationRelationType.ParentChild => "父子",
        LocationRelationType.Parallel => "并列",
        LocationRelationType.Ownership => "所属",
        LocationRelationType.Coreference => "共指",
        _ => "未知"
    };
}
