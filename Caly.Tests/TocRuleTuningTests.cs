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
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Caly.Core.Models;
using Caly.Core.Services;
using Caly.Core.Services.Agent;
using Caly.Core.Services.Interfaces;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Caly.Tests;

/// <summary>
/// Deterministic whitelist validation for the popo.toc-tune call site
/// (contract: narrative-operator-MinerU-Popo docs/toc-self-learning-loop.md §2.5)
/// plus its registration in the call-site registry. No network: the validator is pure
/// and the factory is resolved against a null MCP registry.
/// </summary>
public class TocRuleTuningTests
{
    private sealed class FakeMcpRegistry : IMcpRegistryService
    {
        public IReadOnlyList<McpServerConfig> GetServers() => [];
        public Task<IReadOnlyList<McpClientTool>> GetToolsAsync(string serverName, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<McpClientTool>>([]);
        public Task<McpClientTool?> FindToolAsync(string serverName, string toolName, CancellationToken ct = default)
            => Task.FromResult<McpClientTool?>(null);
        public Task<string?> TestConnectionAsync(string serverName, CancellationToken ct = default)
            => Task.FromResult<string?>("not reachable in tests");
        public void Invalidate(string serverName) { }
        public Task<CallToolResult> CallToolAsync(string serverName, string toolName,
            IReadOnlyDictionary<string, object> arguments, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private static JsonObject? ValidateOk(string patches)
    {
        var (normalized, errors) = TocRuleOverrideValidator.Validate(patches);
        Assert.Empty(errors);
        return normalized;
    }

    private static string[] ValidateErrors(string patches)
    {
        var (normalized, errors) = TocRuleOverrideValidator.Validate(patches);
        Assert.Null(normalized);
        return [.. errors];
    }

    // ---------------------------------------------------------- whitelist accept

    [Fact]
    public void Whitelisted_override_every_kind_is_accepted()
    {
        var patches = """
        {
          "signals.confidence.toc": 0.75,
          "signals.pattern.appendix_re": "(?:附录|Appendix)",
          "signals.pattern.back_matter_titles": ["参考文献", "附录"],
          "alignment.linked_score_min": 0.6,
          "alignment.max_toc_depth": 4,
          "alignment.back_matter_page_tolerance": 5,
          "levels.top_level_unnumbered_re": "^第[一二三四五六七八九十百]+[章节篇]",
          "levels.top_level_unnumbered_flags": ["IGNORECASE", "multiline"],
          "levels.fallback_depth_table": [
            {"mode": "search", "pattern": "^\\d+(\\.\\d+)*", "level": 2},
            {"mode": "match", "pattern": "^Chapter \\d+", "level": 1}
          ],
          "levels.fallback_default_level": 1,
          "linking.excluded_title_patterns": ["^图\\s*\\d+"],
          "linking.garbage_keywords": ["未知"],
          "linking.structural_headings": ["前言"],
          "levels.inference.mode": "viterbi",
          "levels.viterbi.max_depth": 6,
          "levels.viterbi.transition.deeper_one": 0.25,
          "levels.viterbi.transition.deep_jump": -1.5,
          "levels.viterbi.disagree_penalty": 2,
          "signals.bookmarks.enabled": true,
          "signals.confidence.bookmarks": 0.9,
          "signals.geometry.enabled": false,
          "signals.confidence.geometry": 0.6
        }
        """;

        var normalized = ValidateOk(patches);
        Assert.NotNull(normalized);
        Assert.Equal(22, normalized.Count);
    }

    [Fact]
    public void Empty_override_is_accepted_as_default_pack()
    {
        var normalized = ValidateOk("{}");
        Assert.NotNull(normalized);
        Assert.Empty(normalized);
    }

    [Fact]
    public void Number0To1_accepts_integral_boundaries()
    {
        Assert.NotNull(ValidateOk("""{"signals.confidence.demote": 0}"""));
        Assert.NotNull(ValidateOk("""{"signals.confidence.vlm": 1}"""));
    }

    // ---------------------------------------------------------- whitelist reject

    [Fact]
    public void Unknown_rule_id_is_rejected_with_allowed_list()
    {
        var errors = ValidateErrors("""{"signals.confidence.nonexistent": 0.5}""");

        Assert.Single(errors);
        Assert.Contains("not an allowed rule id", errors[0], StringComparison.Ordinal);
        Assert.Contains("alignment.max_toc_depth", errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Out_of_range_and_non_numeric_confidence_values_are_rejected()
    {
        var errors = ValidateErrors(
            """{"signals.confidence.toc": 1.5, "alignment.lcs_threshold": -0.2, "alignment.numbering_assist_score": true}""");

        Assert.Equal(3, errors.Length);
        Assert.All(errors, e => Assert.Contains("number in [0, 1]", e, StringComparison.Ordinal));
    }

    [Fact]
    public void Int_range_rules_reject_non_integral_and_out_of_range_values()
    {
        var errors = ValidateErrors(
            """{"alignment.max_toc_depth": 2.5, "alignment.back_matter_page_tolerance": 1001, "levels.fallback_default_level": 0}""");

        Assert.Equal(3, errors.Length);
        Assert.Contains("integer 1..12", errors[0], StringComparison.Ordinal);
        Assert.Contains("integer 0..1000", errors[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Viterbi_mode_enum_accepts_only_the_two_modes()
    {
        Assert.NotNull(ValidateOk("""{"levels.inference.mode": "pointwise"}"""));
        Assert.NotNull(ValidateOk("""{"levels.inference.mode": "viterbi"}"""));

        var errors = ValidateErrors("""{"levels.inference.mode": "global"}""");
        Assert.Single(errors);
        Assert.Contains("pointwise | viterbi", errors[0], StringComparison.Ordinal);

        Assert.NotEmpty(ValidateErrors("""{"levels.inference.mode": 1}"""));
    }

    [Fact]
    public void Bool_switches_accept_only_true_and_false()
    {
        Assert.NotNull(ValidateOk("""{"signals.bookmarks.enabled": false, "signals.geometry.enabled": true}"""));

        var errors = ValidateErrors("""{"signals.bookmarks.enabled": "yes", "signals.geometry.enabled": 1}""");
        Assert.Equal(2, errors.Length);
        Assert.All(errors, e => Assert.Contains("expected boolean", e, StringComparison.Ordinal));
    }

    [Fact]
    public void Viterbi_transition_weights_are_signed_number_ranges()
    {
        Assert.NotNull(ValidateOk(
            """{"levels.viterbi.transition.deeper_one": 0, "levels.viterbi.transition.deep_jump": -5, "levels.viterbi.disagree_penalty": 0}"""));

        var errors = ValidateErrors(
            """{"levels.viterbi.transition.deep_jump": -6, "levels.viterbi.disagree_penalty": 5.5, "levels.viterbi.max_depth": 9}""");

        Assert.Equal(3, errors.Length);
        Assert.Contains("number in [-5, 5]", errors[0], StringComparison.Ordinal);
        Assert.Contains("number in [0, 5]", errors[1], StringComparison.Ordinal);
        Assert.Contains("integer 1..8", errors[2], StringComparison.Ordinal);
    }

    [Fact]
    public void Non_compilable_regex_is_rejected()
    {
        var errors = ValidateErrors("""{"signals.pattern.appendix_re": "(?:unclosed"}""");

        Assert.Single(errors);
        Assert.Contains("does not compile", errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Back_matter_titles_must_be_non_empty_but_keyword_lists_may_be_empty()
    {
        Assert.NotEmpty(ValidateErrors("""{"signals.pattern.back_matter_titles": []}"""));
        Assert.NotNull(ValidateOk("""{"linking.garbage_keywords": []}"""));
    }

    [Fact]
    public void String_list_entries_must_be_strings()
    {
        var errors = ValidateErrors("""{"linking.structural_headings": ["前言", 42]}""");

        Assert.Single(errors);
        Assert.Contains("linking.structural_headings[1]", errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Regex_list_entries_must_each_compile()
    {
        var errors = ValidateErrors("""{"linking.excluded_title_patterns": ["^图\\s*\\d+", "[unterminated"]}""");

        Assert.Single(errors);
        Assert.Contains("linking.excluded_title_patterns[1]", errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Flag_list_accepts_flag_names_and_rejects_other_words()
    {
        Assert.NotNull(ValidateOk("""{"levels.top_level_unnumbered_flags": ["IgnoreCase"]}"""));

        var errors = ValidateErrors("""{"levels.top_level_unnumbered_flags": ["ignore_case", "34"]}""");
        Assert.Equal(2, errors.Length);
        Assert.All(errors, e => Assert.Contains("regex option flag name", e, StringComparison.Ordinal));
    }

    [Fact]
    public void Depth_table_validates_mode_pattern_level_and_unknown_fields()
    {
        Assert.NotNull(ValidateOk(
            """{"levels.fallback_depth_table": [{"mode": "search", "pattern": "^\\d+", "level": 3}]}"""));

        var errors = ValidateErrors(
            """{"levels.fallback_depth_table": [{"mode": "prefix", "pattern": "(", "level": 0, "extra": 1}]}""");

        var joined = string.Join(" | ", errors);
        Assert.Equal(4, errors.Length);
        Assert.Contains(".mode", joined, StringComparison.Ordinal);
        Assert.Contains(".pattern", joined, StringComparison.Ordinal);
        Assert.Contains(".level", joined, StringComparison.Ordinal);
        Assert.Contains(".extra", joined, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_depth_table_fields_are_rejected()
    {
        var errors = ValidateErrors("""{"levels.fallback_depth_table": [{}]}""");

        Assert.Equal(3, errors.Length);
    }

    [Fact]
    public void Non_object_and_malformed_patches_are_rejected()
    {
        Assert.Contains("must be a JSON object", ValidateErrors("[1,2]")[0], StringComparison.Ordinal);
        Assert.Contains("not valid JSON", ValidateErrors("{")[0], StringComparison.Ordinal);
        Assert.Contains("not valid JSON", TocRuleOverrideValidator.Validate(null).Errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public void All_problems_are_reported_in_one_pass()
    {
        var errors = ValidateErrors(
            """{"alignment.max_toc_depth": 99, "no.such.rule": 1, "levels.top_level_unnumbered_re": "("}""");

        Assert.Equal(3, errors.Length);
    }

    [Fact]
    public void Accepted_override_is_returned_normalized()
    {
        var normalized = ValidateOk("""{"alignment.max_toc_depth": 4, "signals.confidence.toc": 0.5}""");

        Assert.NotNull(normalized);
        Assert.Equal(4, normalized["alignment.max_toc_depth"]!.GetValue<int>());
        Assert.Equal(0.5, normalized["signals.confidence.toc"]!.GetValue<double>());
    }

    // ------------------------------------------------------------- registration

    [Fact]
    public void TocTune_call_site_is_registered_with_approval_only_on_retoc()
    {
        Assert.True(AgentCallSiteDefinitions.All.ContainsKey("popo.toc-tune"));

        var site = AgentCallSiteDefinitions.PopoTocTune;
        Assert.Equal("popo.toc-tune", site.Id);
        Assert.NotNull(site.LocalToolsFactory);
        Assert.Empty(site.McpToolBindings);
        Assert.Contains("retoc_document", site.ApprovalRequiredTools);
        Assert.Equal(1, site.ApprovalRequiredTools.Count);
        // The closed loop is encoded in the instructions, not in code paths.
        Assert.Contains("evaluate_rules", site.Instructions, StringComparison.Ordinal);
        Assert.Contains("zero regressions", site.Instructions, StringComparison.Ordinal);
        Assert.Contains("retoc_document", site.Instructions, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Factory_exposes_the_four_tools_and_wraps_only_retoc_for_approval()
    {
        var approvals = 0;
        AgentToolApprover approver = (_, _) => { approvals++; return Task.FromResult(true); };
        var context = new AgentToolContext { Document = new StructureDocument() };

        var resolved = await AgentCallSiteFactory.ResolveAsync(
            AgentCallSiteDefinitions.PopoTocTune, new FakeMcpRegistry(), approver, context);

        Assert.Equal(
            ["get_toc_quality", "propose_rule_override", "evaluate_rules", "retoc_document"],
            AgentCallSiteFactory.DescribeToolSurface(resolved));

        Assert.True(resolved.Tools.Single(t => t.Function.Name == "retoc_document").RequiresApproval);
        Assert.All(resolved.Tools.Where(t => t.Function.Name != "retoc_document"),
            t => Assert.False(t.RequiresApproval));

        // The session is parked for the invoking UI, with no task id configured.
        Assert.True(context.Items.TryGetValue(TocRuleTuningTools.SessionItemKey, out var session));
        Assert.IsType<TocRuleTuningSession>(session);

        // propose_rule_override is read-only: it must run without an approval round-trip.
        await resolved.Tools.Single(t => t.Function.Name == "propose_rule_override").Function
            .InvokeAsync(new AIFunctionArguments { ["patches"] = """{"alignment.max_toc_depth": 3}""" });
        Assert.Equal(0, approvals);
    }
}
