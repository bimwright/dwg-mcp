using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server;

/// <summary>Bounded profile pages measured after MCP escaping, without altering group identifiers.</summary>
internal static class DrawingProfileResponse
{
    internal const int DefaultCap = 16 * 1024;
    internal const int MinimumCap = 4096;
    internal const int MaximumCap = 64 * 1024;
    private static readonly string[] Sections = { "types", "layers", "spaces" };

    internal static CallToolResult FromJson(string json, int maxOutputBytes)
    {
        var body = JObject.Parse(json);
        return Fit(Text(body, body.Value<bool?>("ok") != true), maxOutputBytes);
    }

    internal static int ReadRequestedCap(CallToolRequestParams request)
    {
        var arguments = request?.Arguments;
        if (arguments == null || !arguments.TryGetValue("max_output_bytes", out var value)) return DefaultCap;
        int cap;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out cap)
            || value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out cap))
            return cap >= MinimumCap && cap <= MaximumCap ? cap : DefaultCap;
        return DefaultCap;
    }

    internal static CallToolResult Fit(CallToolResult result, int cap)
    {
        if (cap < MinimumCap || cap > MaximumCap) throw new ArgumentOutOfRangeException(nameof(cap), "Profile output must be 4096..65536 bytes.");
        var text = result.Content?.OfType<TextContentBlock>().FirstOrDefault();
        JObject body;
        try { body = JObject.Parse(text?.Text ?? ""); }
        catch (JsonReaderException) { return BoundOther(result, cap); }
        if (body["result"] is not JObject profile || Count(profile["profile_version"]) != 1
            || profile["histograms"] is not JObject histograms
            || Sections.Any(section => histograms[section] is not JObject table || table["rows"] is not JArray))
            return BoundOther(result, cap);

        // Keep the caller's content/metadata untouched. Repeated final fitting uses
        // the original header offsets, never a previously emitted next_offset.
        var fitted = new CallToolResult
        {
            IsError = result.IsError,
            Content = result.Content.Select(block => ReferenceEquals(block, text)
                ? new TextContentBlock { Text = text.Text } : block).ToList(),
            StructuredContent = result.StructuredContent,
            Meta = result.Meta?.DeepClone().AsObject()
        };
        var fittedText = fitted.Content.OfType<TextContentBlock>().First();
        void Refresh()
        {
            SetOutput(profile, cap);
            fittedText.Text = body.ToString(Formatting.None);
        }
        Refresh();
        if (ResponseBudget.Measure(fitted) <= cap) return fitted;

        // Profile data takes precedence over optional decoration. Every omitted
        // decoration is recorded in the delivered text, not only MCP metadata.
        var output = (JObject)profile["output"];
        if (body["_ignored_arguments"] != null)
        {
            body["_ignored_arguments"] = new JObject { ["details_omitted"] = true };
            output["ignored_argument_details_omitted"] = true;
        }
        if (fitted.Meta != null) { fitted.Meta = null; output["metadata_omitted"] = true; }
        if (fitted.StructuredContent != null) { fitted.StructuredContent = null; output["structured_content_omitted"] = true; }
        if (fitted.Content.Count > 1)
        {
            output["other_content_blocks_omitted"] = fitted.Content.Count - 1;
            fitted.Content = new[] { fittedText };
        }
        Refresh();
        var bytes = ResponseBudget.Measure(fitted);
        var tables = Sections.Select(section => (JArray)histograms[section]["rows"]).ToArray();
        while (bytes > cap && tables.Any(rows => rows.Count > 0))
        {
            var excess = bytes - cap;
            var removed = 0;
            do
            {
                var tail = tables.Where(rows => rows.Count > 0)
                    .Select(rows => new { Rows = rows, Bytes = EscapedBytes(rows.Last) })
                    .OrderByDescending(item => item.Bytes).First();
                tail.Rows.RemoveAt(tail.Rows.Count - 1);
                removed += tail.Bytes;
            } while (removed < excess && tables.Any(rows => rows.Count > 0));
            Refresh();
            bytes = ResponseBudget.Measure(fitted);
        }
        return bytes <= cap ? fitted : CoreTooLarge(profile, cap);
    }

    private static void SetOutput(JObject profile, int cap)
    {
        var output = profile["output"] as JObject ?? new JObject();
        var omitted = new JObject();
        var prior = new JObject();
        var next = new JArray();
        var blocked = new JArray();
        var notRequested = new JArray();
        var requested = profile["request"]?.Value<string>("section") ?? "summary";
        var snapshot = profile.Value<string>("snapshot");
        var complete = true;
        foreach (var section in Sections)
        {
            var table = (JObject)profile["histograms"][section];
            var rows = (JArray)table["rows"];
            var offset = Count(table["offset"]);
            var total = Count(table["total_groups"]);
            var selected = requested == "summary" || requested == section;
            if (!selected) notRequested.Add(section);
            var before = selected && offset.HasValue && total.HasValue ? Math.Min(offset.Value, total.Value) : 0;
            var available = total.HasValue ? total.Value - before : (long?)null;
            var remaining = available.HasValue && offset.HasValue
                ? selected ? Math.Max(0, available.Value - rows.Count) : total.Value : (long?)null;
            table["returned"] = rows.Count;
            omitted[section] = remaining.HasValue ? new JValue(remaining.Value) : JValue.CreateNull();
            prior[section] = before;
            if (!remaining.HasValue || remaining.Value > 0 || before > 0) complete = false;
            if (remaining == 0) continue;
            if (!remaining.HasValue || !offset.HasValue || string.IsNullOrEmpty(snapshot))
            {
                blocked.Add(new JObject { ["section"] = section, ["reason"] = "continuation_context_unavailable" });
                continue;
            }
            if (selected && rows.Count == 0)
            {
                // Never advertise an unchanged offset as a successful next page.
                blocked.Add(new JObject
                {
                    ["section"] = section, ["offset"] = offset.Value,
                    ["reason"] = cap < MaximumCap ? "no_row_fits_effective_cap; increase max_output_bytes up to 65536 and the gateway --response-budget-bytes if it is lower"
                        : "no_row_can_be_delivered_at_max_output_bytes_65536"
                });
                continue;
            }
            next.Add(new JObject
            {
                ["tool"] = "dwg_get_drawing_profile",
                ["arguments"] = new JObject
                {
                    ["section"] = section, ["offset"] = selected ? offset.Value + rows.Count : 0,
                    ["expected_snapshot"] = snapshot, ["max_output_bytes"] = cap
                }
            });
        }
        output["budget_bytes"] = cap;
        output["complete"] = complete;
        output["omitted"] = omitted;
        output["prior_groups_not_repeated"] = prior;
        output["not_requested"] = notRequested;
        output["next_calls"] = next;
        output["blocked_sections"] = blocked;
        output["note"] = "Histograms are paged; omitted counts are groups remaining after this page (all groups for sections not requested). Prior offsets are not repeated. " +
            "Use next_calls to retrieve omitted groups under expected_snapshot; each call rescans and rejects a changed snapshot. " +
            (cap < MaximumCap ? "For a blocked empty page inspect blocked_sections, increase max_output_bytes up to 65536, and increase the gateway --response-budget-bytes if limiting. "
                : "A row blocked at 65536 cannot be delivered by this tool. ") +
            "No unchanged-offset continuation is issued for a blocked section; identifiers are not shortened. Coverage.complete describes the scan, not output completeness.";
        profile["output"] = output;
    }

    private static long? Count(JToken value)
        => value?.Type == JTokenType.Integer && long.TryParse(value.ToString(Formatting.None), NumberStyles.None, CultureInfo.InvariantCulture, out var count)
            && count >= 0 ? count : null;

    private static int EscapedBytes(JToken row)
        => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(row.ToString(Formatting.None), McpJsonUtilities.DefaultOptions).Length;

    private static CallToolResult BoundOther(CallToolResult result, int cap)
    {
        if (ResponseBudget.Measure(result) <= cap) return result;
        // Null toolName avoids re-entering the profile-specific final fitter.
        var bounded = ResponseBudget.Apply(result, new DwgMcpConfig
        { ResponseBudgetBytes = cap, ResponseWarnBytes = cap, ResponseStrongWarnBytes = cap }, true, null);
        var block = bounded.Content?.OfType<TextContentBlock>().FirstOrDefault();
        if (block == null) return bounded;
        var body = JObject.Parse(block.Text);
        body["output"] = new JObject
        {
            ["budget_bytes"] = cap, ["complete"] = false,
            ["note"] = "Profile content was unavailable or omitted. Inspect the retained error; narrow the request or increase max_output_bytes up to 65536 and the gateway --response-budget-bytes if limiting. No continuation was issued."
        };
        var candidate = Text(body, bounded.IsError);
        return ResponseBudget.Measure(candidate) <= cap ? candidate : bounded;
    }

    private static CallToolResult CoreTooLarge(JObject profile, int cap)
    {
        // Host source/scope are normally finite literals. A malformed oversized
        // core gets an explicit delivery error, never invented counts or IDs.
        var body = new JObject
        {
            ["ok"] = false, ["error"] = "PROFILE_OUTPUT_TOO_LARGE: profile core metadata cannot fit this output budget.",
            ["result"] = new JObject
            {
                ["profile_version"] = 1,
                ["source"] = new JObject { ["details_omitted"] = true, ["confidence"] = "unavailable_in_delivery" },
                ["scope"] = new JObject { ["details_omitted"] = true },
                ["coverage"] = new JObject { ["complete"] = profile["coverage"]?["complete"]?.Type == JTokenType.Boolean
                    ? profile["coverage"]["complete"].DeepClone() : JValue.CreateNull() },
                ["exact_total_count"] = JValue.CreateNull(), ["observed_entity_count"] = JValue.CreateNull(),
                ["output"] = new JObject
                {
                    ["budget_bytes"] = cap, ["complete"] = false, ["next_calls"] = new JArray(),
                    ["note"] = "Core/source/scope details and counts were omitted from delivery. Increase max_output_bytes up to 65536 and the gateway --response-budget-bytes if limiting; " +
                        "if this already is 65536, this profile cannot be delivered. No continuation or complete counts are claimed."
                }
            }
        };
        foreach (var key in new[] { "document_id", "snapshot" })
        {
            var value = profile[key]?.Type == JTokenType.String ? profile.Value<string>(key) : null;
            if (value != null && value.Length <= 128 && value.All(c => c is >= ' ' and <= '~')) body["result"][key] = value;
            else body["result"][key + "_omitted"] = true;
        }
        var result = Text(body, true);
        System.Diagnostics.Debug.Assert(ResponseBudget.Measure(result) <= cap);
        return result;
    }

    private static CallToolResult Text(JObject body, bool? failed)
        => new() { IsError = failed, Content = new[] { new TextContentBlock { Text = body.ToString(Formatting.None) } } };
}
