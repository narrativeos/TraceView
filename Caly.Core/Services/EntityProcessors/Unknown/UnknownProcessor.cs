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

namespace Caly.Core.Services.EntityProcessors.Unknown;

/// <summary>
/// Entity processor for UNKNOWN category entities.
/// Attempts to re-classify them using all available validators,
/// and falls back to a simple validation-only result.
/// </summary>
public sealed class UnknownProcessor : IEntityProcessor
{
    public string EntityType => "UNKNOWN";
    public IReadOnlySet<string> DependsOn { get; } = new HashSet<string>();
    public bool SupportsCrossBlockResolution => false;

    public EntityValidationResult Validate(SemanticEntity entity, SemanticBlockResult block)
    {
        var text = entity.Text;
        if (string.IsNullOrWhiteSpace(text) || text.Length <= 1)
            return EntityValidationResult.Invalid("Too short to be meaningful");

        // Try to re-classify: check if it looks like a person, org, or location
        var reclassified = TryReclassify(text);
        if (reclassified != null)
        {
            entity.EnrichedMetadata["UNKNOWN:reclassification"] = new Dictionary<string, object>
            {
                ["suggested_category"] = reclassified,
            };
        }

        return EntityValidationResult.Valid("Unknown entity, kept for reference");
    }

    public Task<EntityEnrichmentResult> EnrichAsync(
        SemanticEntity entity,
        EntityContext context,
        CancellationToken ct)
    {
        var reclassified = entity.EnrichedMetadata
            .TryGetValue("UNKNOWN:reclassification", out var metaObj) &&
            metaObj is Dictionary<string, object> meta &&
            meta.TryGetValue("suggested_category", out var catObj)
            ? catObj as string
            : null;

        var result = new EntityEnrichmentResult
        {
            Source = "UnknownValidator",
            EnrichedText = entity.Text,
            Confidence = reclassified != null ? 0.50 : 0.30,
            Metadata = new Dictionary<string, object>
            {
                ["original_text"] = entity.Text,
                ["suggested_category"] = reclassified ?? "",
            }
        };

        return Task.FromResult(result);
    }

    public IReadOnlyList<EntityRelation> ExtractRelations(
        IReadOnlyList<SemanticEntity> entities,
        SemanticBlockResult block)
    {
        return Array.Empty<EntityRelation>();
    }

    public IReadOnlyList<EntityRelation> ResolveCrossBlock(
        IReadOnlyList<(SemanticEntity Entity, SemanticBlockResult Block)> allEntities,
        IReadOnlyList<SemanticBlockResult> allBlocks)
    {
        return Array.Empty<EntityRelation>();
    }

    /// <summary>
    /// Attempts to re-classify an UNKNOWN entity by checking patterns
    /// for other entity types.
    /// </summary>
    private static string? TryReclassify(string text)
    {
        // Check if it looks like a person name (2-4 CJK chars with common surname)
        var surnames = new[] { "张", "王", "李", "赵", "刘", "陈", "杨", "黄", "周", "吴",
                                "徐", "孙", "胡", "朱", "高", "林", "何", "郭", "马", "罗" };
        if (text.Length >= 2 && text.Length <= 4 && surnames.Any(s => text.StartsWith(s)))
            return "PERSON";

        // Check if it looks like an organization (contains common suffixes)
        var orgSuffixes = new[] { "公司", "集团", "局", "部", "院", "所", "中心", "协会", "社", "厂" };
        if (orgSuffixes.Any(s => text.EndsWith(s)))
            return "ORGANIZATION";

        // Check if it looks like a location (contains common suffixes)
        var locSuffixes = new[] { "省", "市", "区", "县", "镇", "村", "路", "街", "桥" };
        if (locSuffixes.Any(s => text.EndsWith(s)))
            return "LOCATION";

        return null;
    }
}
