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
using Caly.Core.Models.EntityProcessors;

namespace Caly.Core.Models;

/// <summary>
/// Builds an <see cref="EnrichedResultFile"/> from the pipeline-processed entities
/// and their <see cref="EnrichedMetadata"/> dictionaries.
/// </summary>
public static class EnrichedResultBuilder
{
    /// <summary>
    /// Build enriched result from a semantic result file that has been processed
    /// by the EntityProcessorPipeline (entities now have EnrichedMetadata populated).
    /// </summary>
    public static EnrichedResultFile Build(
        SemanticResultFile semanticResult,
        List<EntityRelation> crossBlockRelations,
        string outputDir)
    {
        var enriched = new EnrichedResultFile
        {
            Version = "1.0",
            Timestamp = DateTime.UtcNow.ToString("O"),
            SourceSemanticResult = System.IO.Path.Combine(outputDir, "semantic_result.json"),
        };

        foreach (var block in semanticResult.Blocks)
        {
            // Prefer BlockIds (int) when available; otherwise try to parse
            // SourceBlockIds as integers (e.g., Popo assigns integer string IDs).
            // MinerU SourceBlockIds are UUIDs and won't parse as int.
            var blockIds = new List<int>();
            if (block.BlockIds.Count > 0)
            {
                blockIds = block.BlockIds;
            }
            else
            {
                foreach (var id in block.SourceBlockIds)
                {
                    if (int.TryParse(id, out var numId))
                        blockIds.Add(numId);
                }
            }

            var enrichedBlock = new EnrichedBlockResult
            {
                BlockIds = blockIds
            };

            foreach (var entity in block.Entities)
            {
                var entry = BuildEntityEntry(entity);
                if (entry != null)
                    enrichedBlock.Entities.Add(entry);
            }

            // Collect relations from block-level enriched metadata
            CollectBlockRelations(block, enrichedBlock);

            enriched.Blocks.Add(enrichedBlock);
        }

        // Add cross-block relations (one entry per relation, tagged by entity category)
        foreach (var rel in crossBlockRelations)
        {
            enriched.CrossBlockRelations.Add(new EnrichedRelation
            {
                RelationType = rel.RelationType,
                SourceEntityId = rel.SourceEntityId,
                TargetEntityId = rel.TargetEntityId,
                EntityCategory = "CROSS_BLOCK",
                Confidence = rel.Confidence,
                Evidence = rel.Evidence ?? ""
            });
        }

        return enriched;
    }

    private static EnrichedEntityEntry? BuildEntityEntry(SemanticEntity entity)
    {
        // Skip noise entities: pure numeric/symbol, LaTeX artifacts, single non-CJK chars
        if (IsNoiseEntity(entity.Text))
            return null;

        // Build validation status respecting the full severity spectrum:
        // - invalid when is_valid == false
        // - ambiguous when severity == "Warning" (valid but needs disambiguation)
        // - valid when is_valid == true and no warning
        // - unknown when validation data is missing
        var isValid = GetValidationField(entity, "is_valid");
        var severity = GetValidationField(entity, "severity") as string;
        var validationStatus = isValid is bool v && !v
            ? "invalid"
            : (severity is not null && severity == "Warning")
                ? "ambiguous"
                : (isValid is bool bv && bv)
                    ? "valid"
                    : "unknown";

        var entry = new EnrichedEntityEntry
        {
            Id = entity.Id,
            Text = entity.Text,
            Category = entity.Category,
            Normalized = entity.Normalized ?? "",
            ValidationStatus = validationStatus,
            ValidationReason = GetValidationField(entity, "reason") as string ?? "",
            EnrichedText = GetEnrichmentField(entity, "enriched_text") as string ?? entity.Text,
            ExternalId = GetEnrichmentField(entity, "external_id") as string ?? "",
            EnrichmentSource = GetEnrichmentField(entity, "source") as string ?? "",
            EnrichmentConfidence = GetEnrichmentField(entity, "confidence") is double d ? d : 0.0
        };

        // Flatten type-specific metadata from enrichment
        var enrichment = GetEnrichmentDict(entity);
        if (enrichment != null)
        {
            // Extract nested metadata dict
            if (enrichment.TryGetValue("metadata", out var metaObj) &&
                metaObj is Dictionary<string, object> metaDict)
            {
                foreach (var kvp in metaDict)
                    entry.Metadata.Add($"{kvp.Key}={kvp.Value}");
            }

            // Add top-level enrichment fields that aren't in metadata
            foreach (var kvp in enrichment)
            {
                if (kvp.Key is "metadata" or "enriched_text" or "external_id" or "source" or "confidence")
                    continue;
                if (kvp.Value is Dictionary<string, object>)
                    continue;
                entry.Metadata.Add($"{kvp.Key}={kvp.Value}");
            }
        }

        return entry;
    }

    private static void CollectBlockRelations(SemanticBlockResult block, EnrichedBlockResult enrichedBlock)
    {
        // Collect LOCATION relations (stored in dedicated list)
        foreach (var lr in block.LocationRelations)
        {
            enrichedBlock.Relations.Add(new EnrichedRelation
            {
                RelationType = lr.RelationType.ToString(),
                SourceEntityId = lr.SourceLocation,
                TargetEntityId = lr.TargetLocation,
                EntityCategory = "LOCATION",
                Confidence = lr.Confidence,
                Evidence = lr.Evidence ?? ""
            });
        }

        // Collect relations from other entity types (stored in EnrichedMetadata)
        foreach (var kvp in block.EnrichedMetadata)
        {
            if (!kvp.Key.EndsWith(":relations")) continue;
            var category = kvp.Key.Split(':')[0];

            if (kvp.Value is List<Dictionary<string, object>> relList)
            {
                foreach (var relDict in relList)
                {
                    enrichedBlock.Relations.Add(new EnrichedRelation
                    {
                        RelationType = relDict.TryGetValue("relation_type", out var rt) ? rt?.ToString() ?? "" : "",
                        SourceEntityId = relDict.TryGetValue("source_entity_id", out var sid) ? sid?.ToString() ?? "" : "",
                        TargetEntityId = relDict.TryGetValue("target_entity_id", out var tid) ? tid?.ToString() ?? "" : "",
                        EntityCategory = category,
                        Confidence = relDict.TryGetValue("confidence", out var conf) && conf is double cd ? cd : 0.0,
                        Evidence = relDict.TryGetValue("evidence", out var ev) ? ev?.ToString() ?? "" : ""
                    });
                }
            }
        }
    }

    private static object? GetValidationField(SemanticEntity entity, string field)
    {
        var key = $"{entity.Category}:validation";
        if (entity.EnrichedMetadata.TryGetValue(key, out var v) &&
            v is Dictionary<string, object> dict &&
            dict.TryGetValue(field, out var val))
            return val;
        return null;
    }

    private static object? GetEnrichmentField(SemanticEntity entity, string field)
    {
        var key = $"{entity.Category}:enrichment";
        if (entity.EnrichedMetadata.TryGetValue(key, out var v) &&
            v is Dictionary<string, object> dict &&
            dict.TryGetValue(field, out var val))
            return val;
        return null;
    }

    private static Dictionary<string, object>? GetEnrichmentDict(SemanticEntity entity)
    {
        var key = $"{entity.Category}:enrichment";
        return entity.EnrichedMetadata.TryGetValue(key, out var v) &&
               v is Dictionary<string, object> d ? d : null;
    }

    /// <summary>
    /// Returns true if the entity text is noise that should not appear in the KG:
    /// pure numbers/symbols, LaTeX artifacts, single non-CJK characters,
    /// and single CJK characters that are common function words or segmentation noise.
    /// </summary>
    private static bool IsNoiseEntity(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return true;

        // Single character that isn't a CJK ideograph
        if (text.Length == 1)
        {
            var c = text[0];
            if ((c >= 0x4E00 && c <= 0x9FFF) || (c >= 0x3400 && c <= 0x4DBF))
            {
                // Reject common CJK noise chars: function words, measure words,
                // cardinal directions, geographic generics, segmentation artifacts
                if (IsSingleCharCjkNoise(c))
                    return true;
                return false;
            }
            if (c >= 0xF900 && c <= 0xFAFF)
                return false;
            return true;
        }

        // LaTeX artifacts
        if (text.StartsWith("\\") || text.Contains("\\frac") || text.Contains("\\mathrm") ||
            text.Contains("\\mathbf") || text.Contains("\\begin") || text.Contains("\\array"))
            return true;
        if (text == "frac" || text == "mathrm" || text == "array" || text == "begin" ||
            text == "mathbf" || text == "textrm")
            return true;

        // Pure numeric/symbol (with optional dashes, dots, brackets)
        if (System.Text.RegularExpressions.Regex.IsMatch(text,
                @"^[\d\s\-\.\+/\\\(\)\[\]\{\},;:'""\\|@#$%^&*=~`±×÷≤≥<>]+$"))
            return true;

        return false;
    }

    /// <summary>
    /// Returns true if a single CJK character is common noise.
    /// Mirrors GeoJsonService.IsSingleCharCjkNoise.
    /// </summary>
    private static bool IsSingleCharCjkNoise(char c)
    {
        return c switch
        {
            // Function words / particles
            '的' or '了' or '是' or '在' or '和' or '与' or '或' => true,
            '但' or '而' or '则' or '也' or '就' or '要' or '会' => true,
            '能' or '可' or '以' or '被' or '把' or '从' or '对' => true,
            '向' or '往' or '到' or '于' or '着' or '过' or '得' => true,
            '之' or '其' or '所' or '者' or '此' or '该' or '各' => true,
            '又' or '再' or '还' or '已' or '将' or '未' or '非' => true,
            // Measure words / classifiers
            '个' or '只' or '条' or '件' or '种' or '次' or '回' => true,
            '遍' or '趟' or '张' or '本' or '支' or '块' or '片' => true,
            '句' or '位' or '双' or '台' or '辆' or '艘' or '架' => true,
            '号' or '余' or '章' or '节' or '页' or '段' or '行' => true,
            '册' or '篇' or '份' or '类' or '款' or '项' => true,
            // CJK numerals (standalone almost never meaningful entities)
            '零' or '一' or '二' or '三' or '四' or '五' => true,
            '六' or '七' or '八' or '九' or '十' or '百' => true,
            '千' or '万' or '亿' or '两' or '几' or '半' => true,
            // Cardinal directions
            '东' or '南' or '西' or '北' or '中' or '上' or '下' => true,
            '左' or '右' or '前' or '后' or '内' or '外' or '里' => true,
            // Generic geographic terms
            '河' or '山' or '海' or '江' or '湖' or '岛' or '湾' => true,
            '港' or '城' or '镇' or '村' or '庄' or '市' or '县' => true,
            '区' or '省' or '路' or '街' or '桥' or '门' or '关' => true,
            '岭' or '峰' or '谷' or '原' or '漠' or '林' or '泉' => true,
            '溪' or '潭' or '池' or '寺' or '庙' or '塔' or '楼' => true,
            // Known segmentation noise
            '柱' or '际' or '杆' or '草' or '方' or '间' or '处' => true,
            '系' or '杠' or '模' or '途' or '层' or '端' or '侧' => true,
            '边' or '角' or '底' or '顶' or '头' or '尾' or '面' => true,
            _ => false
        };
    }
}
