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
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Caly.Core.Models;
using Microsoft.Extensions.AI;

namespace Caly.Core.Services.Agent;

/// <summary>
/// Client-side deterministic gate for the popo.toc-tune call site
/// (contract: narrative-operator-MinerU-Popo docs/toc-self-learning-loop.md §2.5).
/// Only the dotted rule ids in <see cref="Whitelist"/> may be overridden, and only with a
/// value matching the recorded kind (numeric range, compilable regex, typed list). The
/// validator is pure: no network, no filesystem — an invalid override is refused before
/// anything reaches the Popo service.
/// </summary>
internal static class TocRuleOverrideValidator
{
    private enum OverrideKind
    {
        Number0To1,
        RegexPattern,
        StringList,
        NonEmptyStringList,
        RegexList,
        RegexFlagList,
        IntRange,
        DepthTable,
        Bool,
        EnumValues,
        NumberRange,
    }

    private sealed record RuleSpec(OverrideKind Kind, long Min = 0, long Max = 0, double Low = 0, double High = 0, string[]? Values = null)
    {
        public string Allowed => Kind switch
        {
            OverrideKind.Number0To1 => "number in [0, 1]",
            OverrideKind.RegexPattern => "string with a compilable regular expression",
            OverrideKind.StringList => "array of strings",
            OverrideKind.NonEmptyStringList => "non-empty array of strings",
            OverrideKind.RegexList => "array of strings, each a compilable regular expression",
            OverrideKind.RegexFlagList => "array of regex option flag names (" + AllowedFlagNames + ")",
            OverrideKind.IntRange => $"integer {Min}..{Max}",
            OverrideKind.DepthTable =>
                "array of {\"mode\": \"search\"|\"match\", \"pattern\": compilable regex, \"level\": integer >= 1}",
            OverrideKind.Bool => "boolean",
            OverrideKind.EnumValues => "one of " + string.Join(" | ", Values!),
            OverrideKind.NumberRange => FormattableString.Invariant($"number in [{Low}, {High}]"),
            _ => "unknown",
        };
    }

    private const string AllowedFlagNames = "IgnoreCase, Multiline, ExplicitCapture, Singleline, " +
                                            "IgnorePatternWhitespace, RightToLeft, ECMAScript,CultureInvariant";

    /// <summary>Whitelist: dotted rule id → accepted value kind (contract §2.1 rule-pack fields).</summary>
    private static readonly Dictionary<string, RuleSpec> Whitelist = new(StringComparer.Ordinal)
    {
        ["signals.confidence.toc"] = new(OverrideKind.Number0To1),
        ["signals.confidence.numbering"] = new(OverrideKind.Number0To1),
        ["signals.confidence.pattern"] = new(OverrideKind.Number0To1),
        ["signals.confidence.consistency"] = new(OverrideKind.Number0To1),
        ["signals.confidence.vlm"] = new(OverrideKind.Number0To1),
        ["signals.confidence.demote"] = new(OverrideKind.Number0To1),
        ["signals.pattern.appendix_re"] = new(OverrideKind.RegexPattern),
        ["signals.pattern.standard_number_re"] = new(OverrideKind.RegexPattern),
        ["signals.pattern.back_matter_titles"] = new(OverrideKind.NonEmptyStringList),
        ["alignment.linked_score_min"] = new(OverrideKind.Number0To1),
        ["alignment.lcs_threshold"] = new(OverrideKind.Number0To1),
        ["alignment.numbering_assist_floor"] = new(OverrideKind.Number0To1),
        ["alignment.numbering_assist_score"] = new(OverrideKind.Number0To1),
        ["alignment.max_toc_depth"] = new(OverrideKind.IntRange, Min: 1, Max: 12),
        ["alignment.back_matter_page_tolerance"] = new(OverrideKind.IntRange, Min: 0, Max: 1000),
        ["levels.top_level_unnumbered_re"] = new(OverrideKind.RegexPattern),
        ["levels.top_level_unnumbered_flags"] = new(OverrideKind.RegexFlagList),
        ["levels.fallback_depth_table"] = new(OverrideKind.DepthTable),
        ["levels.fallback_default_level"] = new(OverrideKind.IntRange, Min: 1, Max: 12),
        ["linking.excluded_title_patterns"] = new(OverrideKind.RegexList),
        ["linking.garbage_keywords"] = new(OverrideKind.StringList),
        ["linking.structural_headings"] = new(OverrideKind.StringList),
        // M2: global level inference + S6 bookmark / S7 geometry signals.
        ["levels.inference.mode"] = new(OverrideKind.EnumValues, Values: ["pointwise", "viterbi"]),
        ["levels.viterbi.max_depth"] = new(OverrideKind.IntRange, Min: 1, Max: 8),
        ["levels.viterbi.transition.deeper_one"] = new(OverrideKind.NumberRange, Low: -5, High: 5),
        ["levels.viterbi.transition.deep_jump"] = new(OverrideKind.NumberRange, Low: -5, High: 5),
        ["levels.viterbi.disagree_penalty"] = new(OverrideKind.NumberRange, Low: 0, High: 5),
        ["signals.bookmarks.enabled"] = new(OverrideKind.Bool),
        ["signals.confidence.bookmarks"] = new(OverrideKind.Number0To1),
        ["signals.geometry.enabled"] = new(OverrideKind.Bool),
        ["signals.confidence.geometry"] = new(OverrideKind.Number0To1),
    };

    /// <summary>Every rule id an override may name, sorted for stable error text.</summary>
    public static IReadOnlyList<string> AllowedRuleIds { get; } =
        Whitelist.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

    /// <summary>
    /// Validate a patch shaped as <c>{"&lt;rule_id&gt;": value, ...}</c>. Returns the normalized
    /// override when every entry is valid; otherwise returns every problem found, in input
    /// order, so the model can fix them all in one round.
    /// </summary>
    public static (JsonObject? Override, IReadOnlyList<string> Errors) Validate(string? patchesJson)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(patchesJson ?? string.Empty);
        }
        catch (JsonException ex)
        {
            return (null, [$"patches is not valid JSON: {ex.Message}"]);
        }

        if (node is not JsonObject patches)
        {
            return (null,
                ["patches must be a JSON object mapping rule ids to values, e.g. {\"alignment.max_toc_depth\": 4}."]);
        }

        var errors = new List<string>();
        var normalized = new JsonObject();
        foreach (var (key, value) in patches)
        {
            if (!Whitelist.TryGetValue(key, out var spec))
            {
                errors.Add($"'{key}' is not an allowed rule id. Allowed ids: {string.Join(", ", AllowedRuleIds)}.");
                continue;
            }

            var valueErrors = ValidateValue(key, value, spec);
            if (valueErrors.Count > 0)
            {
                errors.AddRange(valueErrors);
                continue;
            }

            normalized[key] = value?.DeepClone();
        }

        if (errors.Count > 0)
        {
            return (null, errors);
        }

        return (normalized, []);
    }

    private static List<string> ValidateValue(string key, JsonNode? value, RuleSpec spec)
    {
        var errors = new List<string>();
        switch (spec.Kind)
        {
            case OverrideKind.Number0To1:
                if (!TryGetNumber(value, out var fraction) || fraction < 0 || fraction > 1)
                {
                    errors.Add($"'{key}': expected {spec.Allowed}.");
                }

                break;

            case OverrideKind.RegexPattern:
                CheckRegex(key, value, errors);
                break;

            case OverrideKind.StringList:
            case OverrideKind.NonEmptyStringList:
                CheckStrings(key, value, nonEmpty: spec.Kind == OverrideKind.NonEmptyStringList, errors);
                break;

            case OverrideKind.RegexList:
                CheckList(key, value, errors, static (itemKey, item, sink) => CheckRegex(itemKey, item, sink));
                break;

            case OverrideKind.RegexFlagList:
                CheckList(key, value, errors, static (itemKey, item, sink) => CheckFlag(itemKey, item, sink));
                break;

            case OverrideKind.IntRange:
                if (!TryGetNumber(value, out var number) ||
                    number != Math.Floor(number) || number < spec.Min || number > spec.Max)
                {
                    errors.Add($"'{key}': expected {spec.Allowed}.");
                }

                break;

            case OverrideKind.Bool:
                if (value is not JsonValue boolValue || !boolValue.TryGetValue(out bool _))
                {
                    errors.Add($"'{key}': expected {spec.Allowed}.");
                }

                break;

            case OverrideKind.EnumValues:
                if (value is not JsonValue enumValue ||
                    !enumValue.TryGetValue(out string? text) ||
                    Array.IndexOf(spec.Values!, text) < 0)
                {
                    errors.Add($"'{key}': expected {spec.Allowed}.");
                }

                break;

            case OverrideKind.NumberRange:
                if (!TryGetNumber(value, out var amount) || amount < spec.Low || amount > spec.High)
                {
                    errors.Add($"'{key}': expected {spec.Allowed}.");
                }

                break;

            case OverrideKind.DepthTable:
                CheckDepthTable(key, value, spec, errors);
                break;
        }

        return errors;
    }

    private static bool TryGetNumber(JsonNode? value, out double number)
    {
        number = 0;
        return value is JsonValue jsonValue && jsonValue.TryGetValue(out number);
    }

    private static void CheckRegex(string key, JsonNode? value, List<string> errors)
    {
        if (value is not JsonValue jsonValue || !jsonValue.TryGetValue(out string? pattern))
        {
            errors.Add($"'{key}': expected a regular expression string.");
            return;
        }

        try
        {
            _ = new Regex(pattern);
        }
        catch (ArgumentException ex)
        {
            errors.Add($"'{key}': '{pattern}' does not compile as a regular expression: {ex.Message}");
        }
    }

    private static void CheckStrings(string key, JsonNode? value, bool nonEmpty, List<string> errors)
    {
        if (value is not JsonArray array)
        {
            errors.Add($"'{key}': expected an array of strings.");
            return;
        }

        if (nonEmpty && array.Count == 0)
        {
            errors.Add($"'{key}': array must not be empty.");
        }

        for (var i = 0; i < array.Count; i++)
        {
            if (array[i] is not JsonValue item || !item.TryGetValue(out string? _))
            {
                errors.Add($"'{key}[{i}]': expected a string.");
            }
        }
    }

    private static void CheckList(
        string key,
        JsonNode? value,
        List<string> errors,
        Action<string, JsonNode?, List<string>> checkItem)
    {
        if (value is not JsonArray array)
        {
            errors.Add($"'{key}': expected an array.");
            return;
        }

        for (var i = 0; i < array.Count; i++)
        {
            checkItem($"{key}[{i}]", array[i], errors);
        }
    }

    private static void CheckFlag(string key, JsonNode? value, List<string> errors)
    {
        if (value is not JsonValue jsonValue ||
            !jsonValue.TryGetValue(out string? name) ||
            !Regex.IsMatch(name, "^[A-Za-z]+$") ||
            !Enum.TryParse<RegexOptions>(name, ignoreCase: true, out _))
        {
            errors.Add($"'{key}': expected one regex option flag name ({AllowedFlagNames}).");
        }
    }

    private static void CheckDepthTable(string key, JsonNode? value, RuleSpec spec, List<string> errors)
    {
        if (value is not JsonArray array)
        {
            errors.Add($"'{key}': expected {spec.Allowed}.");
            return;
        }

        for (var i = 0; i < array.Count; i++)
        {
            var itemKey = $"{key}[{i}]";
            if (array[i] is not JsonObject entry)
            {
                errors.Add($"'{itemKey}': expected an object with exactly the fields mode, pattern, level.");
                continue;
            }

            foreach (var (field, _) in entry)
            {
                if (field is not ("mode" or "pattern" or "level"))
                {
                    errors.Add($"'{itemKey}.{field}': unexpected field; only mode, pattern and level are allowed.");
                }
            }

            if (!entry.ContainsKey("mode"))
            {
                errors.Add($"'{itemKey}.mode': missing; expected \"search\" or \"match\".");
            }
            else if (entry["mode"] is not JsonValue modeValue ||
                     !modeValue.TryGetValue(out string? mode) ||
                     mode is not ("search" or "match"))
            {
                errors.Add($"'{itemKey}.mode': expected \"search\" or \"match\".");
            }

            if (!entry.ContainsKey("pattern"))
            {
                errors.Add($"'{itemKey}.pattern': missing; expected a regular expression string.");
            }
            else
            {
                CheckRegex($"{itemKey}.pattern", entry["pattern"], errors);
            }

            if (!entry.ContainsKey("level"))
            {
                errors.Add($"'{itemKey}.level': missing; expected an integer >= 1.");
            }
            else if (!TryGetNumber(entry["level"], out var level) || level != Math.Floor(level) || level < 1)
            {
                errors.Add($"'{itemKey}.level': expected an integer >= 1.");
            }
        }
    }
}

/// <summary>
/// Per-run scratch space and HTTP surface for the popo.toc-tune call site
/// (contract: narrative-operator-MinerU-Popo docs/toc-self-learning-loop.md §2.5).
/// Read tools work off the local StructureDocument; the gate and write tools hit the
/// Popo service's /toc/evaluate and /tasks/{id}/retoc endpoints. retoc_document is the
/// only write and is approval-gated by the call site definition. All output is built with
/// <see cref="JsonNode"/> — AOT-safe, no anonymous types, no reflection-based serialization.
/// </summary>
internal sealed class TocRuleTuningSession
{
    /// <summary>Cap on level-diff entries echoed back from a retoc.</summary>
    private const int DiffCap = 100;

    /// <summary>Cap on per-case rows echoed back from an evaluation.</summary>
    private const int CaseCap = 100;

    /// <summary>Cap on low-confidence entries echoed back by get_toc_quality.</summary>
    private const int LowConfidenceCap = 20;

    /// <summary>Cap on candidate rows echoed back by distill_rule_candidates.</summary>
    private const int CandidateCap = 20;

    /// <summary>
    /// Gold-set replay and tree re-derivation are server-side batch jobs; give them a
    /// generous but bounded deadline (PopoService keeps its client-wide timeout huge
    /// because of result downloads; here every call is a bounded computation instead).
    /// </summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(10);

    private readonly StructureDocument _document;
    private readonly string _baseUrl;
    private readonly string? _taskId;
    private readonly HttpClient _httpClient;

    public TocRuleTuningSession(StructureDocument document, string baseUrl, string? taskId)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _baseUrl = baseUrl.TrimEnd('/');
        _taskId = string.IsNullOrWhiteSpace(taskId) ? null : taskId;
        _httpClient = new HttpClient { Timeout = RequestTimeout };
    }

    /// <summary>Last override accepted by propose_rule_override; used as the default for
    /// evaluate_rules and retoc_document when the model passes no explicit override.</summary>
    public JsonObject? AcceptedOverride { get; private set; }

    // ------------------------------------------------------------------ tools

    public async Task<string> GetTocQuality()
    {
        var entries = _document.TocEntries;

        // Level jumps: a direct child level must be at most one deeper than its predecessor.
        var jumps = new JsonArray();
        for (var i = 1; i < entries.Count; i++)
        {
            if (entries[i].Level > entries[i - 1].Level + 1)
            {
                jumps.Add(new JsonObject
                {
                    ["index"] = i,
                    ["title"] = entries[i].Title,
                    ["from"] = entries[i - 1].Level,
                    ["to"] = entries[i].Level,
                });
            }
        }

        // Heuristic low-confidence flag: an entry with neither numbering evidence nor
        // table provenance has its level from title heuristics only.
        var lowConfidence = new JsonArray();
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (string.IsNullOrWhiteSpace(entry.Numbering) &&
                !string.Equals(entry.Source, "table", StringComparison.OrdinalIgnoreCase))
            {
                lowConfidence.Add(new JsonObject
                {
                    ["index"] = i,
                    ["title"] = entry.Title,
                    ["level"] = entry.Level,
                });
            }
        }

        var byLevel = new JsonObject();
        foreach (var group in entries.GroupBy(e => e.Level).OrderBy(g => g.Key))
        {
            byLevel[group.Key.ToString()] = group.Count();
        }

        var bySource = new JsonObject();
        foreach (var group in entries
                     .GroupBy(e => string.IsNullOrWhiteSpace(e.Source) ? "tree" : e.Source)
                     .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            bySource[group.Key] = group.Count();
        }

        var result = new JsonObject
        {
            ["docId"] = _document.DocId,
            ["taskId"] = _taskId,
            ["tocEntryCount"] = entries.Count,
            ["maxLevel"] = entries.Count == 0 ? 0 : entries.Max(e => e.Level),
            ["levelJumpCount"] = jumps.Count,
            ["levelJumps"] = jumps,
            ["lowConfidenceCount"] = lowConfidence.Count,
            ["lowConfidence"] = JsonArraySlice(lowConfidence, LowConfidenceCap),
            ["lowConfidenceTruncated"] = lowConfidence.Count > LowConfidenceCap,
            ["summary"] = new JsonObject { ["byLevel"] = byLevel, ["bySource"] = bySource },
        };

        if (_taskId is not null)
        {
            var (node, error) = await GetAsync($"/tasks/{Uri.EscapeDataString(_taskId)}/result").ConfigureAwait(false);
            result["storedResult"] = error ?? new JsonObject
            {
                ["status"] = node?["status"]?.DeepClone(),
                // The stored result carries neither tree/toc nor vlm_decisions (contract §2.4);
                // report their presence so the agent does not chase a decision log that is not there.
                ["hasTree"] = node?["result"] is JsonObject root && root.ContainsKey("tree"),
                ["hasToc"] = node?["result"] is JsonObject root2 && root2.ContainsKey("toc"),
                ["hasVlmDecisions"] = node?["result"] is JsonObject root3 && root3.ContainsKey("vlm_decisions"),
            };
        }

        return result.ToJsonString();
    }

    public string ProposeRuleOverride(string patches)
    {
        var (normalized, errors) = TocRuleOverrideValidator.Validate(patches);
        if (normalized is null)
        {
            var response = new JsonObject { ["accepted"] = false };
            var errorArray = new JsonArray();
            foreach (var error in errors)
            {
                errorArray.Add(error);
            }

            response["errors"] = errorArray;
            response["hint"] = "Fix every listed rule id and value, then propose again. Nothing was sent to the server.";
            return response.ToJsonString();
        }

        AcceptedOverride = normalized;
        return new JsonObject
        {
            ["accepted"] = true,
            ["normalized"] = normalized.DeepClone().AsObject(),
        }.ToJsonString();
    }

    public async Task<string> EvaluateRules(string? @override = null)
    {
        var (resolved, refusal) = ResolveOverride(@override);
        if (refusal is not null)
        {
            return refusal;
        }

        var body = new JsonObject { ["overrides"] = resolved ?? new JsonObject() };
        var (report, error) = await PostAsync("/toc/evaluate", body).ConfigureAwait(false);
        return error?.ToJsonString() ?? RenderEvaluateReport(report).ToJsonString();
    }

    public async Task<string> RetocDocument(string? @override = null)
    {
        if (_taskId is null)
        {
            return new JsonObject
            {
                ["error"] = $"No Popo task id in the run context (key '{TocRuleTuningTools.TaskIdItemKey}'). " +
                            "Cannot retoc without the completed task that produced this document.",
            }.ToJsonString();
        }

        var (resolved, refusal) = ResolveOverride(@override);
        if (refusal is not null)
        {
            return refusal;
        }

        var body = new JsonObject { ["overrides"] = resolved ?? new JsonObject() };
        var (result, error) = await PostAsync($"/tasks/{Uri.EscapeDataString(_taskId)}/retoc", body)
            .ConfigureAwait(false);
        if (error is not null)
        {
            return error.ToJsonString();
        }

        // The rewritten tree/toc is intentionally not echoed in full; the caller reads it
        // back through GET /tasks/{id}/result. Summarize what changed and by which pack.
        var summary = new JsonObject
        {
            ["task_id"] = result?["task_id"]?.DeepClone(),
            ["doc_id"] = result?["doc_id"]?.DeepClone(),
            ["rule_pack"] = result?["rule_pack"] is JsonObject pack
                ? new JsonObject
                {
                    ["schema_version"] = pack["schema_version"]?.DeepClone(),
                    ["fingerprint"] = pack["fingerprint"]?.DeepClone(),
                }
                : null,
            ["artifacts"] = result?["artifacts"]?.DeepClone(),
        };

        var diff = new JsonArray();
        var total = 0;
        if (result?["level_diff"] is JsonArray levelDiff)
        {
            total = levelDiff.Count;
            foreach (var entry in levelDiff.Take(DiffCap))
            {
                diff.Add(entry?.DeepClone());
            }
        }

        summary["level_diff"] = diff;
        summary["level_diff_total"] = total;
        return summary.ToJsonString();
    }

    /// <summary>
    /// Read-only corpus distillation (contract §2.7): aggregates the service's retoc
    /// override ledger into global rule-pack candidate proposals. Proposes only — the
    /// call never writes the pack, and promotion stays a human code review.
    /// </summary>
    public async Task<string> DistillRuleCandidates(int? top = null)
    {
        var body = new JsonObject();
        if (top is not null)
        {
            body["top"] = top.Value;
        }

        var (report, error) = await PostAsync("/toc/distill", body).ConfigureAwait(false);
        if (error is not null)
        {
            return error.ToJsonString();
        }

        return RenderDistillReport(report).ToJsonString();
    }

    // ------------------------------------------------------------- internals

    /// <summary>
    /// Resolve the override argument: explicit JSON is re-validated deterministically
    /// before anything is sent; nothing passed falls back to the last accepted proposal.
    /// </summary>
    private (JsonObject? Override, string? Refusal) ResolveOverride(string? overrideJson)
    {
        if (string.IsNullOrWhiteSpace(overrideJson))
        {
            return (AcceptedOverride, null);
        }

        var (normalized, errors) = TocRuleOverrideValidator.Validate(overrideJson);
        if (normalized is not null)
        {
            AcceptedOverride = normalized;
            return (normalized, null);
        }

        var refusalObject = new JsonObject { ["accepted"] = false, ["sent"] = false };
        var errorArray = new JsonArray();
        foreach (var error in errors)
        {
            errorArray.Add(error);
        }

        refusalObject["errors"] = errorArray;
        return (null, refusalObject.ToJsonString());
    }

    private static JsonObject RenderDistillReport(JsonNode? report)
    {
        var result = new JsonObject
        {
            ["ledger_size"] = report?["ledger_size"]?.DeepClone(),
            ["docs"] = report?["docs"]?.DeepClone(),
        };

        var candidates = new JsonArray();
        var total = 0;
        if (report?["candidates"] is JsonArray rawCandidates)
        {
            total = rawCandidates.Count;
            foreach (var item in rawCandidates.Take(CandidateCap))
            {
                candidates.Add(new JsonObject
                {
                    ["rule_id"] = item?["rule_id"]?.DeepClone(),
                    ["value"] = item?["value"]?.DeepClone(),
                    ["support"] = item?["support"]?.DeepClone(),
                    ["conflict_ratio"] = item?["conflict_ratio"]?.DeepClone(),
                    ["gold_evidence"] = item?["gold_evidence"]?.DeepClone(),
                    ["promotable"] = item?["promotable"]?.DeepClone(),
                    ["notes"] = item?["notes"]?.DeepClone() ?? new JsonArray(),
                    ["gate"] = item?["gate"] is JsonObject gate
                        ? new JsonObject
                        {
                            ["passed"] = gate["passed"]?.DeepClone(),
                            ["reasons"] = gate["reasons"]?.DeepClone() ?? new JsonArray(),
                            ["regressions"] = gate["regressions"]?.DeepClone() ?? new JsonArray(),
                            ["level_accuracy"] = gate["level_accuracy"]?.DeepClone(),
                        }
                        : null,
                });
            }
        }

        result["candidates"] = candidates;
        result["candidates_total"] = total;
        return result;
    }

    private static JsonObject RenderEvaluateReport(JsonNode? report)
    {
        var result = new JsonObject
        {
            ["passed"] = report?["gate"]?["passed"]?.DeepClone(),
            ["reasons"] = report?["gate"]?["reasons"] is JsonArray reasons
                ? reasons.DeepClone().AsArray()
                : new JsonArray(),
            ["thresholds"] = report?["thresholds"]?.DeepClone(),
        };

        if (report?["aggregate"] is JsonObject aggregate)
        {
            result["aggregate"] = new JsonObject
            {
                ["approved_cases"] = aggregate["approved_cases"]?.DeepClone(),
                ["level_accuracy"] = aggregate["level_accuracy"]?.DeepClone(),
                ["regressions"] = aggregate["regressions"]?.DeepClone() ?? new JsonArray(),
            };
        }

        var cases = new JsonArray();
        var casesTotal = 0;
        if (report?["cases"] is JsonArray rawCases)
        {
            casesTotal = rawCases.Count;
            foreach (var item in rawCases.Take(CaseCap))
            {
                cases.Add(new JsonObject
                {
                    ["case_id"] = item?["case_id"]?.DeepClone(),
                    ["status"] = item?["status"]?.DeepClone(),
                    ["regressed"] = item?["regressed"]?.DeepClone(),
                    ["level_accuracy"] = new JsonObject
                    {
                        ["baseline"] = item?["baseline"]?["level_accuracy"]?.DeepClone(),
                        ["candidate"] = item?["candidate"]?["level_accuracy"]?.DeepClone(),
                    },
                });
            }
        }

        result["cases"] = cases;
        result["casesTotal"] = casesTotal;

        if (report?["rule_pack"] is JsonObject pack)
        {
            result["rule_pack"] = new JsonObject
            {
                ["schema_version"] = pack["candidate"]?["schema_version"]?.DeepClone(),
                ["fingerprint"] = pack["candidate"]?["fingerprint"]?.DeepClone(),
                ["baseline_fingerprint"] = pack["baseline"]?["fingerprint"]?.DeepClone(),
            };
        }

        return result;
    }

    private async Task<(JsonNode? Node, JsonObject? Error)> PostAsync(string path, JsonNode body)
    {
        using var timeoutCts = new CancellationTokenSource(RequestTimeout);
        try
        {
            using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            using var response = await _httpClient
                .PostAsync($"{_baseUrl}{path}", content, timeoutCts.Token)
                .ConfigureAwait(false);
            return await ReadResponseAsync(response, path).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return (null, Error(path, ex.Message));
        }
    }

    private async Task<(JsonNode? Node, JsonObject? Error)> GetAsync(string path)
    {
        using var timeoutCts = new CancellationTokenSource(RequestTimeout);
        try
        {
            using var response = await _httpClient
                .GetAsync($"{_baseUrl}{path}", timeoutCts.Token)
                .ConfigureAwait(false);
            return await ReadResponseAsync(response, path).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return (null, Error(path, ex.Message));
        }
    }

    private static async Task<(JsonNode? Node, JsonObject? Error)> ReadResponseAsync(
        HttpResponseMessage response, string path)
    {
        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return (null, Error(path, $"HTTP {(int)response.StatusCode}: {ServerDetail(json)}"));
        }

        try
        {
            return (JsonNode.Parse(json), null);
        }
        catch (JsonException ex)
        {
            var preview = json.Length > 200 ? json[..200] + "..." : json;
            return (null, Error(path, $"invalid JSON response: {ex.Message}. Response: {preview}"));
        }
    }

    /// <summary>FastAPI-style error bodies carry the concrete reason in "detail"; otherwise
    /// fall back to a truncated raw body so the model can still see what the server said.</summary>
    private static string ServerDetail(string json)
    {
        try
        {
            var detail = JsonNode.Parse(json)?["detail"];
            switch (detail)
            {
                case null:
                    break;
                case JsonArray array:
                    var parts = new List<string>(array.Count);
                    foreach (var item in array)
                    {
                        parts.Add(item?.ToJsonString() ?? "null");
                    }

                    return string.Join("; ", parts);
                default:
                    return detail is JsonValue value && value.TryGetValue(out string? text)
                        ? text
                        : detail.ToJsonString();
            }
        }
        catch (JsonException)
        {
            // Not structured JSON — the raw body is the best available explanation.
        }

        return json.Length > 600 ? json[..600] + "..." : json;
    }

    private static JsonObject Error(string path, string detail) =>
        new() { ["error"] = $"{path}: {detail}" };

    private static JsonArray JsonArraySlice(JsonArray source, int limit)
    {
        var slice = new JsonArray();
        foreach (var item in source.Take(limit))
        {
            slice.Add(item?.DeepClone());
        }

        return slice;
    }
}

/// <summary>
/// Builds the popo.toc-tune tool surface for one run
/// (contract: narrative-operator-MinerU-Popo docs/toc-self-learning-loop.md §2.5):
/// get_toc_quality reads the run's StructureDocument (+ the stored task result, best effort),
/// propose_rule_override validates overrides against the whitelist deterministically, and
/// evaluate_rules / retoc_document call the Popo service's gate and write endpoints.
/// retoc_document is the only write and is approval-gated by the call site definition.
/// The produced session is parked in the context so the invoking UI can read it after the run.
///
/// Following the AgentToolContext convention (tools never touch DI or settings), the caller
/// fills context.Items: BaseUrlItemKey with the Popo service base URL (falls back to
/// <see cref="PopoService.DefaultBaseUrl"/>) and TaskIdItemKey with the id of the completed
/// task that produced the document (required only by retoc_document).
/// </summary>
internal static class TocRuleTuningTools
{
    /// <summary>Key of the <see cref="TocRuleTuningSession"/> in AgentToolContext.Items.</summary>
    public const string SessionItemKey = "popo.toc_tune.session";

    /// <summary>AgentToolContext.Items key: Popo service base URL (string). Optional;
    /// defaults to <see cref="PopoService.DefaultBaseUrl"/>.</summary>
    public const string BaseUrlItemKey = "popo.toc_tune.base_url";

    /// <summary>AgentToolContext.Items key: completed Popo task id for this document (string).
    /// Optional; without it retoc_document refuses and get_toc_quality stays local-only.</summary>
    public const string TaskIdItemKey = "popo.toc_tune.task_id";

    public static IReadOnlyList<AIFunction> Create(AgentToolContext context)
    {
        var document = context.Document
            ?? throw new InvalidOperationException(
                "The popo.toc-tune call site requires a StructureDocument in the run context.");

        var baseUrl = context.Items.TryGetValue(BaseUrlItemKey, out var configuredUrl) &&
                      configuredUrl is string { Length: > 0 } url
            ? url
            : PopoService.DefaultBaseUrl;
        var taskId = context.Items.TryGetValue(TaskIdItemKey, out var configuredTask) &&
                     configuredTask is string { Length: > 0 } id
            ? id
            : null;

        var session = new TocRuleTuningSession(document, baseUrl, taskId);
        context.Items[SessionItemKey] = session;

        return
        [
            AIFunctionFactory.Create(session.GetTocQuality,
                name: "get_toc_quality",
                description: "Quality summary of the current document's TOC: level jumps, low-confidence " +
                             "entries (heuristic: no numbering and no table provenance), per-level and " +
                             "per-source decision summary, plus the stored task result status when the task " +
                             "id is known. Read-only. Call first."),
            AIFunctionFactory.Create(session.ProposeRuleOverride,
                name: "propose_rule_override",
                description: "Validate a rule override offline: a JSON object mapping dotted rule ids to " +
                             "values, e.g. {\"alignment.max_toc_depth\": 4}. Only whitelisted rule ids are " +
                             "accepted and only with the right type/range; regexes must compile. Returns " +
                             "accepted true/false; on false, fix every listed error and propose again. " +
                             "Nothing is sent to the server by this tool."),
            AIFunctionFactory.Create(session.EvaluateRules,
                name: "evaluate_rules",
                description: "Gate an override: replays the gold set via /toc/evaluate and returns " +
                             "gate.passed, gate.reasons, thresholds, aggregate.regressions and per-case " +
                             "level_accuracy baseline->candidate, plus the candidate pack fingerprint. The " +
                             "override argument is a JSON object in propose_rule_override format; omit it to " +
                             "use the last accepted proposal. This gate must pass with zero regressions " +
                             "before retoc_document may be called with the same override."),
            AIFunctionFactory.Create(session.RetocDocument,
                name: "retoc_document",
                description: "Rewrite this document's stored task tree/toc by replaying the rule pack with " +
                             "the override via /tasks/{id}/retoc. The only WRITE in this loop and it needs " +
                             "explicit human approval. Call it only after evaluate_rules with the same " +
                             "override returned passed with zero regressions. Returns the level_diff, the " +
                             "pack fingerprint and the artifact flags; the full tree/toc stays in the task " +
                             "result."),
            AIFunctionFactory.Create(session.DistillRuleCandidates,
                name: "distill_rule_candidates",
                description: "Read-only corpus distillation: aggregates the service's retoc override ledger " +
                             "via /toc/distill into recurring cross-document rule changes as global-pack " +
                             "candidates with support, conflict ratio, gold evidence, and a gold-gate verdict " +
                             "for the top promotable ones (optional top argument, 0-5, caps the gate " +
                             "reruns). Proposes only: nothing this returns changes the global rule pack; " +
                             "promotion stays a human code review."),
        ];
    }
}
