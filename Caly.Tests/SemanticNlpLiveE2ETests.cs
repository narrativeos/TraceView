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

using Caly.Core.Models;
using Caly.Core.Services;

namespace Caly.Tests;

/// <summary>
/// Live end-to-end check for the NLP entity-quality + noun-signal integration:
/// drives the real <see cref="SemanticAnalysisService.AnalyzeAsync"/> against a
/// running NLP at http://localhost:8000 and verifies keep/filter/noun_signals
/// flow through to the <see cref="SemanticBlockResult"/>. Skips when the NLP is
/// not reachable (CI-safe, mirrors the fixture-based integration tests).
/// </summary>
public class SemanticNlpLiveE2ETests
{
    private const string BaseUrl = "http://localhost:8000";

    private static bool NlpAvailable()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var resp = http.GetAsync($"{BaseUrl}/health").GetAwaiter().GetResult();
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    [Fact]
    public async Task AnalyzeAsync_FlowsKeepAndNounSignals_EndToEnd()
    {
        if (!NlpAvailable())
        {
            // NLP not running — this is a live integration check, skip gracefully.
            return;
        }

        var service = new SemanticAnalysisService(BaseUrl);
        var node = new AnalysisTreeNode
        {
            Type = "text",
            Title = "test",
            Content = "考虑到交接试验工作量较大，通常仅进行10kV下的介损测量。第5章 绝缘电阻应大于100MΩ。",
            BlockIds = new List<int> { 1 },
        };

        var result = await service.AnalyzeAsync(node);

        // Noun signals (Step A + B) should be populated and carry a syntactic role.
        Assert.NotEmpty(result.NounSignals);
        Assert.All(result.NounSignals, s => Assert.False(string.IsNullOrEmpty(s.SyntacticRole)));
        Assert.Contains(result.NounSignals, s => s.Text == "测量");

        // Every demoted entity must carry a filter tag (soft demotion, auditable).
        Assert.All(result.Entities, e =>
        {
            if (!e.Keep)
                Assert.False(string.IsNullOrEmpty(e.Filter));
        });

        // F1: "5章" should be relabeled to SECTION_REF when present.
        var sectionRef = result.Entities.FirstOrDefault(e => e.Text.Contains("5章"));
        if (sectionRef is not null)
            Assert.Equal("SECTION_REF", sectionRef.Category);
    }
}
