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
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Caly.Core.Models;
using Caly.Core.Services;
using Caly.Core.Services.EntityProcessors;
using Caly.Core.Services.EntityProcessors.Location;
using Caly.Core.Services.EntityProcessors.Person;
using Caly.Core.Services.EntityProcessors.Temporal;
using Caly.Core.Services.EntityProcessors.Organization;
using Xunit;
using Xunit.Abstractions;

namespace Caly.Tests;

/// <summary>
/// Integration test: runs the full entity processor pipeline on a real semantic_result.json
/// and saves enriched_result.json. External APIs (Nominatim, Wikidata) may fail gracefully.
/// </summary>
public class EnrichedPipelineIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public EnrichedPipelineIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task RunPipeline_OnRealData_ProducesEnrichedResult()
    {
        // ── 1. Find the test data ──
        var semanticDir = "/Users/futuremeng/.TraceView/978-7-111-74324-8_1-1_2/semantic";
        var inputPath = Path.Combine(semanticDir, "semantic_result.json");
        var outputPath = Path.Combine(semanticDir, "enriched_result.json");

        if (!File.Exists(inputPath))
        {
            _output.WriteLine($"SKIP: File not found: {inputPath}");
            return;
        }

        _output.WriteLine($"Loading: {inputPath}");

        // ── 2. Load semantic_result.json ──
        var json = await File.ReadAllTextAsync(inputPath);
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = SourceGenerationContext.Default,
            PropertyNameCaseInsensitive = true
        };
        var semanticResult = JsonSerializer.Deserialize<SemanticResultFile>(json, options);

        if (semanticResult == null || semanticResult.Blocks.Count == 0)
        {
            _output.WriteLine("SKIP: Empty or invalid semantic_result.json");
            return;
        }

        _output.WriteLine($"Loaded {semanticResult.Blocks.Count} blocks");

        // Entity count summary
        var counts = new System.Collections.Generic.Dictionary<string, int>();
        foreach (var block in semanticResult.Blocks)
        {
            foreach (var entity in block.Entities)
            {
                var cat = string.IsNullOrEmpty(entity.Category) ? "UNKNOWN" : entity.Category;
                counts[cat] = counts.GetValueOrDefault(cat, 0) + 1;
            }
        }

        foreach (var kvp in counts.OrderByDescending(k => k.Value))
            _output.WriteLine($"  {kvp.Key}: {kvp.Value}");

        // ── 3. Run the pipeline ──
        var sw = Stopwatch.StartNew();
        var pipeline = new EntityProcessorPipeline();
        pipeline.AddProcessor(new LocationProcessor());
        pipeline.AddProcessor(new TemporalProcessor());
        pipeline.AddProcessor(new PersonProcessor());
        pipeline.AddProcessor(new OrganizationProcessor());

        _output.WriteLine("\nRunning pipeline...");
        var enrichedResult = await pipeline.ProcessAsync(semanticResult);

        sw.Stop();
        _output.WriteLine($"Pipeline completed in {sw.Elapsed.TotalSeconds:F1}s");

        if (enrichedResult == null)
        {
            _output.WriteLine("ERROR: Pipeline returned null");
            return;
        }

        // ── 4. Show enrichment statistics ──
        int validatedCount = 0, enrichedCount = 0, failedCount = 0;
        foreach (var block in enrichedResult.Blocks)
        {
            foreach (var entity in block.Entities)
            {
                switch (entity.ValidationStatus)
                {
                    case "valid": validatedCount++; break;
                    case "invalid": failedCount++; break;
                }
                if (!string.IsNullOrEmpty(entity.EnrichmentSource))
                    enrichedCount++;
            }
        }

        _output.WriteLine($"\nEnrichment Results:");
        _output.WriteLine($"  Total enriched entities: {enrichedResult.Blocks.Sum(b => b.Entities.Count)}");
        _output.WriteLine($"  Validated: {validatedCount}");
        _output.WriteLine($"  Failed validation: {failedCount}");
        _output.WriteLine($"  Enriched (has source): {enrichedCount}");
        _output.WriteLine($"  Total relations: {enrichedResult.Blocks.Sum(b => b.Relations.Count)}");
        _output.WriteLine($"  Cross-block relations: {enrichedResult.CrossBlockRelations.Count}");

        // Category breakdown
        var catStats = new System.Collections.Generic.Dictionary<string, (int total, int enriched, int valid)>();
        foreach (var block in enrichedResult.Blocks)
        {
            foreach (var entity in block.Entities)
            {
                var cat = entity.Category;
                if (!catStats.ContainsKey(cat))
                    catStats[cat] = (0, 0, 0);
                var (t, e, v) = catStats[cat];
                t++;
                if (!string.IsNullOrEmpty(entity.EnrichmentSource)) e++;
                if (entity.ValidationStatus == "valid") v++;
                catStats[cat] = (t, e, v);
            }
        }

        _output.WriteLine("\nCategory Breakdown:");
        _output.WriteLine($"  {{'Category',-16}} {{'Total',6}} {{'Enriched',9}} {{'Valid',6}}");
        foreach (var kvp in catStats.OrderByDescending(k => k.Value.total))
        {
            var (t, e, v) = kvp.Value;
            _output.WriteLine($"  {kvp.Key,-16} {t,6} {e,9} {v,6}");
        }

        // ── 5. Save enriched_result.json ──
        var outputJson = JsonSerializer.Serialize(enrichedResult, SourceGenerationContext.Default.EnrichedResultFile);
        await File.WriteAllTextAsync(outputPath, outputJson);
        var sizeKb = new FileInfo(outputPath).Length / 1024.0;
        _output.WriteLine($"\nSaved: {outputPath} ({sizeKb:F0} KB)");

        // ── 6. Show sample enriched entities ──
        _output.WriteLine($"\nSample Enriched Entities:");
        int shown = 0;
        foreach (var block in enrichedResult.Blocks)
        {
            foreach (var entity in block.Entities)
            {
                if (!string.IsNullOrEmpty(entity.EnrichmentSource) || entity.ValidationStatus == "invalid")
                {
                    _output.WriteLine($"  [{entity.Category}] {entity.Text}");
                    _output.WriteLine($"    → {entity.EnrichedText} ({entity.EnrichmentSource}, {entity.EnrichmentConfidence:P0})");
                    _output.WriteLine($"    status={entity.ValidationStatus}, ext_id={entity.ExternalId}");
                    if (entity.Metadata.Count > 0)
                        _output.WriteLine($"    meta: {string.Join(", ", entity.Metadata.Take(3))}");
                    shown++;
                    if (shown >= 10) break;
                }
            }
            if (shown >= 10) break;
        }
    }
}
