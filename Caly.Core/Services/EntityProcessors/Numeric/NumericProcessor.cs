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
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Caly.Core.Models;
using Caly.Core.Models.EntityProcessors;

namespace Caly.Core.Services.EntityProcessors.Numeric;

/// <summary>
/// Entity processor for NUMBER entities.
/// Classifies numeric entities into types (quantity, percentage, code, etc.),
/// normalizes units, and extracts structured numeric data.
/// </summary>
public sealed class NumericProcessor : IEntityProcessor
{
    public string EntityType => "NUMBER";
    public IReadOnlySet<string> DependsOn { get; } = new HashSet<string>();
    public bool SupportsCrossBlockResolution => false;

    private enum NumberType
    {
        Unknown,
        Quantity,
        Percentage,
        Code,
        Ratio,
    }

    public EntityValidationResult Validate(SemanticEntity entity, SemanticBlockResult block)
    {
        var text = entity.Text;

        if (text.Length <= 1)
            return EntityValidationResult.Invalid("Single character, not a meaningful number");

        if (Regex.IsMatch(text, @"^\d{4}[—\-–]\d{4}$"))
            return EntityValidationResult.Invalid("Year range, not a numeric entity");

        if (Regex.IsMatch(text, @"^[A-Z]{2,5}[\s/]?[A-Z]?$"))
            return EntityValidationResult.Invalid("Standard code, not a numeric entity");

        var numType = ClassifyNumber(text);
        if (numType == NumberType.Unknown)
            return EntityValidationResult.Invalid("Cannot classify as a meaningful number");

        entity.EnrichedMetadata["NUMBER:number_type"] = new Dictionary<string, object>
        {
            ["type"] = numType.ToString(),
        };

        return EntityValidationResult.Valid($"Classified as {numType}");
    }

    public Task<EntityEnrichmentResult> EnrichAsync(
        SemanticEntity entity,
        EntityContext context,
        CancellationToken ct)
    {
        var numType = ClassifyNumber(entity.Text);
        var normalized = NormalizeNumber(entity.Text, numType);

        var result = new EntityEnrichmentResult
        {
            Source = "NumericNormalizer",
            Confidence = numType switch
            {
                NumberType.Quantity => 0.85,
                NumberType.Percentage => 0.90,
                NumberType.Code => 0.70,
                NumberType.Ratio => 0.80,
                _ => 0.50
            },
            EnrichedText = normalized,
            Metadata = new Dictionary<string, object>
            {
                ["number_type"] = numType.ToString(),
                ["original_text"] = entity.Text,
                ["normalized"] = normalized
            }
        };

        if (ExtractNumericValue(entity.Text, out var value, out var unit))
        {
            result.Metadata["numeric_value"] = value;
            result.Metadata["unit"] = unit ?? "";
        }

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

    private static NumberType ClassifyNumber(string text)
    {
        if (text.Contains('%') || text.Contains("百分之") || text.Contains("百分"))
            return NumberType.Percentage;

        if (Regex.IsMatch(text, @"^[A-Za-z]+\d+$"))
            return NumberType.Code;

        if (text.Contains(':') || (text.Contains('/') && !text.Contains('%')))
            return NumberType.Ratio;

        if (Regex.IsMatch(text, @"\d+[\u4e00-\u9fff]+"))
            return NumberType.Quantity;

        if (Regex.IsMatch(text, @"^\d+\.?\d*$"))
            return NumberType.Quantity;

        return NumberType.Unknown;
    }

    private static string NormalizeNumber(string text, NumberType type)
    {
        return type switch
        {
            NumberType.Percentage => NormalizePercentage(text),
            NumberType.Quantity => NormalizeQuantity(text),
            NumberType.Code => text.ToUpperInvariant(),
            NumberType.Ratio => text,
            _ => text
        };
    }

    private static string NormalizePercentage(string text)
    {
        var match = Regex.Match(text, @"(\d+(?:\.\d+)?)%?");
        if (match.Success)
            return $"{match.Groups[1].Value}%";

        match = Regex.Match(text, @"百分之(\d+(?:\.\d+)?)");
        if (match.Success)
            return $"{match.Groups[1].Value}%";

        return text;
    }

    private static string NormalizeQuantity(string text)
    {
        var units = new Dictionary<string, string>
        {
            ["km/h"] = "km/h", ["千米/小时"] = "km/h", ["公里/小时"] = "km/h",
            ["km"] = "km", ["公里"] = "km", ["千米"] = "km",
            ["m"] = "m", ["米"] = "m",
            ["t"] = "t", ["吨"] = "t",
            ["kg"] = "kg", ["千克"] = "kg",
        };

        foreach (var (old, @new) in units)
        {
            if (text.Contains(old))
            {
                text = text.Replace(old, @new);
                break;
            }
        }

        return text;
    }

    private static bool ExtractNumericValue(string text, out double? value, out string? unit)
    {
        value = null;
        unit = null;

        var match = Regex.Match(text, @"(\d+(?:\.\d+)?)\s*([\u4e00-\u9fff\w/]+)?");
        if (match.Success && double.TryParse(match.Groups[1].Value, out var num))
        {
            value = num;
            unit = match.Groups[2].Success ? match.Groups[2].Value : null;
            return true;
        }

        return false;
    }
}