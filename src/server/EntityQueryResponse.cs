using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server;

/// <summary>One final query-page fit; only the host's checkpoint for the last delivered row may escape.</summary>
internal static class EntityQueryResponse
{
    internal const int DefaultCap = 16 * 1024;
    internal const int MinimumCap = 4096;
    internal const int MaximumCap = 64 * 1024;
    private static readonly string[] InternalKeys = { "_checkpoints", "_continuation_allowed", "_host_more", "_blocked" };

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

    internal static CallToolResult Fit(CallToolResult result, int cap, bool countOnly = false,
        string toolName = "dwg_query_entities", string collection = "entities", string versionKey = "query_page_version", string identityField = "handle")
    {
        if (cap < MinimumCap || cap > MaximumCap) throw new ArgumentOutOfRangeException(nameof(cap), "Query output must be 4096..65536 bytes.");
        var textRead = collection == "clusters";
        var bandRead = collection == "bands";
        var text = result.Content?.OfType<TextContentBlock>().FirstOrDefault();
        JObject body;
        try { body = JObject.Parse(text?.Text ?? ""); }
        catch (JsonReaderException) { return textRead && result.IsError != true ? Unsupported(cap, true) : BoundOther(result, cap, textRead); }
        if (textRead && body["ok"]?.Type != JTokenType.Boolean)
            return result.IsError == true ? BoundOther(result, cap, true) : Unsupported(cap, true, body);
        if (textRead && body.Value<bool?>("ok") == false) return BoundOther(result, cap, true);
        // Older plugins accept unknown query parameters and may silently ignore
        // bbox/space/paging. Never present that legacy result as a filtered read.
        if (body.Value<bool?>("ok") == true)
        {
            var data = body["result"] as JObject;
            if (countOnly)
                return Count(data?["query_count_version"]) == 1 ? BoundOther(result, cap) : Unsupported(cap, original: body);
            if (Count(data?[versionKey]) != 1 || data?[collection] is not JArray)
                return Unsupported(cap, textRead, body, bandRead);
        }
        if (body["result"] is not JObject page || Count(page[versionKey]) != 1 || page[collection] is not JArray rows)
            return BoundOther(result, cap, textRead);

        var checkpoints = page["_checkpoints"] as JArray;
        var candidateCount = rows.Count;
        var hostMore = page.Value<bool?>("_host_more") == true;
        var scanComplete = page["coverage"]?.Value<bool?>("scan_complete") == true;
        var hostAllowsContinuation = page.Value<bool?>("_continuation_allowed") == true;
        var matchedAfter = Count(page["matches_after_cursor"]);
        var arguments = page["query_arguments"] is JObject supplied ? (JObject)supplied.DeepClone() : null;
        arguments?.Remove("cursor");
        (page["query_arguments"] as JObject)?.Remove("cursor");
        var hostBlocked = page["_blocked"] is JObject blocked ? SafeBlocked(blocked, identityField) : null;
        foreach (var key in InternalKeys) page.Remove(key);
        if (textRead)
        {
            foreach (var key in InternalKeys) body.Remove(key);
            // Enhanced text reads never expose legacy writable cluster IDs.
            foreach (var row in rows.OfType<JObject>()) { row.Remove("id"); row.Remove("rewrite_mode"); }
            if (!scanComplete)
            {
                // Grouping a partial population changes medians and adjacency;
                // no subset can stand in for complete-scope clusters.
                rows.Clear(); candidateCount = 0; hostMore = false;
                checkpoints = null; matchedAfter = null; hostAllowsContinuation = false;
                page["matched_count"] = null; page["exact_match_count"] = null; page["matches_after_cursor"] = null;
            }
        }
        var fitted = new CallToolResult
        {
            IsError = result.IsError,
            Content = result.Content.Select(block => ReferenceEquals(block, text) ? new TextContentBlock { Text = text.Text } : block).ToList(),
            StructuredContent = result.StructuredContent,
            Meta = result.Meta?.DeepClone().AsObject()
        };
        var fittedText = fitted.Content.OfType<TextContentBlock>().First();
        var output = new JObject();
        var firstIdentity = (rows.First as JObject)?[identityField]?.DeepClone();
        // An old structured/secondary copy could contain undelivered rows or
        // private checkpoints. The single rebuilt text page is authoritative.
        if (fitted.StructuredContent != null) { fitted.StructuredContent = null; output["structured_content_omitted"] = true; }
        if (fitted.Content.Count > 1) { output["other_content_blocks_omitted"] = fitted.Content.Count - 1; fitted.Content = new[] { fittedText }; }
        void Refresh()
        {
            var delivered = rows.Count;
            var remaining = matchedAfter.HasValue ? Math.Max(0, matchedAfter.Value - delivered) : (long?)null;
            var more = hostMore || delivered < candidateCount || remaining.HasValue && remaining.Value > 0;
            var allowed = hostAllowsContinuation && scanComplete;
            string nextCursor = null;
            JObject deliveryBlocked = hostBlocked == null ? null : (JObject)hostBlocked.DeepClone();
            if (more && allowed && delivered > 0)
            {
                if (arguments != null && checkpoints?.Count == candidateCount && Token(checkpoints[delivered - 1]))
                    nextCursor = checkpoints[delivered - 1].Value<string>();
                else { allowed = false; deliveryBlocked = new JObject { ["reason"] = "host_checkpoint_or_query_arguments_unavailable" }; }
            }
            if (more && delivered == 0 && candidateCount > 0)
            {
                allowed = false;
                deliveryBlocked = SafeBlocked(new JObject
                {
                    [identityField] = firstIdentity,
                    ["reason"] = "row_does_not_fit_final_MCP_output_budget"
                }, identityField);
            }
            if (more && delivered == 0) allowed = false;
            page["count"] = delivered;
            output["budget_bytes"] = cap;
            output["complete"] = scanComplete && !more && deliveryBlocked == null;
            output["has_more"] = more;
            output["omitted_matching"] = remaining.HasValue ? new JValue(remaining.Value) : JValue.CreateNull();
            if (textRead) output["omitted_clusters"] = output["omitted_matching"].DeepClone();
            output["continuation_allowed"] = allowed && delivered > 0;
            output["next_cursor"] = nextCursor;
            var calls = new JArray();
            if (nextCursor != null)
            {
                var nextArguments = (JObject)arguments.DeepClone();
                if (!textRead && !bandRead) nextArguments["paged"] = true;
                nextArguments["cursor"] = nextCursor;
                nextArguments["max_output_bytes"] = cap;
                calls.Add(new JObject { ["tool"] = toolName, ["arguments"] = nextArguments });
            }
            output["next_calls"] = calls;
            if (deliveryBlocked != null) output["blocked"] = deliveryBlocked;
            else output.Remove("blocked");
            var hostPayloadBlocked = candidateCount == 0 && hostBlocked?.Value<string>("reason")?.StartsWith("row_exceeds_host", StringComparison.Ordinal) == true;
            output["note"] = bandRead
                ? scanComplete
                    ? "omitted_matching counts whole bands after the delivered page. Follow next_calls with the same options. For a blocked band, try a larger output budget or disable samples and restart."
                    : "The source scan was incomplete. No complete-scope bands or continuation are available; see coverage."
                : textRead ? TextGuidance(cap, hostPayloadBlocked, scanComplete) : "Only retained entities were delivered. omitted_matching counts observed matches after the input cursor; unavailable extents/read failures remain separate in coverage. " +
                "Follow next_calls with the same filters and fields; an incomplete scan disables continuation. A blocked row is never skipped. " +
                (hostPayloadBlocked ? "The blocked row exceeds the host payload budget; narrow fields and restart without a cursor. Raising the MCP cap does not bypass that host limit. "
                    : cap < MaximumCap ? "Retry the same input cursor with a larger max_output_bytes (up to 65536); a smaller --response-budget-bytes must also be raised. "
                    : "This page already uses the 65536-byte tool maximum; a still-blocked row requires narrower fields and a new query. ") +
                "Changing fields or filters requires restarting without a cursor.";
            page["output"] = output;
            fittedText.Text = body.ToString(Formatting.None);
        }

        Refresh();
        if (ResponseBudget.Measure(fitted) <= cap) return fitted;
        if (body["_ignored_arguments"] != null)
        {
            body["_ignored_arguments"] = new JObject { ["details_omitted"] = true };
            output["ignored_argument_details_omitted"] = true;
        }
        if (fitted.Meta != null) { fitted.Meta = null; output["metadata_omitted"] = true; }
        // The full query is still present in next_calls; avoid duplicating a long
        // layer/layout filter in a small page's public header.
        if (page["query_arguments"] != null) { page.Remove("query_arguments"); output["query_arguments_header_omitted"] = true; }
        Refresh();
        var bytes = ResponseBudget.Measure(fitted);
        while (bytes > cap && rows.Count > 0)
        {
            var excess = bytes - cap;
            var removed = 0;
            do
            {
                removed += EscapedBytes(rows.Last);
                rows.RemoveAt(rows.Count - 1);
            } while (removed < excess && rows.Count > 0);
            Refresh();
            bytes = ResponseBudget.Measure(fitted);
        }
        return bytes <= cap ? fitted : CoreTooLarge(page, cap, collection, versionKey, identityField, textRead, body);
    }

    private static long? Count(JToken value)
        => value?.Type == JTokenType.Integer && long.TryParse(value.ToString(Formatting.None), NumberStyles.None, CultureInfo.InvariantCulture, out var count)
            && count >= 0 ? count : null;

    private static CallToolResult Unsupported(int cap, bool textRead = false, JObject original = null, bool bandRead = false)
    {
        var body = new JObject
        {
            ["ok"] = false,
            ["error"] = bandRead ? "BAND_FEATURE_UNSUPPORTED: the plugin did not confirm the band paging contract. Update gateway and plugin together."
                : textRead
                ? "TEXT_FEATURE_UNSUPPORTED: the plugin did not confirm the requested bounded text-read contract. Update gateway and plugin together."
                : "QUERY_FEATURE_UNSUPPORTED: the plugin did not confirm the requested spatial/paged contract. Update gateway and plugin together.",
            ["result"] = new JObject { ["output"] = new JObject
            {
                ["budget_bytes"] = cap, ["complete"] = false, ["next_calls"] = new JArray(),
                ["note"] = bandRead ? "No bands, counts or continuation were delivered."
                    : textRead
                    ? "Unverified legacy selection data and writable cluster IDs were discarded: handles, bbox, space and cursor semantics were not confirmed. No cluster count, clusters or continuation are claimed."
                    : "Unverified legacy data was discarded: bbox, space, fields and cursor semantics were not confirmed. No count, entity rows or continuation are claimed."
            }}
        };
        ResponseBudget.RetainTarget(original, body);
        return BoundOther(Text(body, true), cap, textRead);
    }

    private static string TextGuidance(int cap, bool hostPayloadBlocked, bool scanComplete)
        => (scanComplete ? "Only retained whole clusters were delivered. omitted_clusters counts complete-scope clusters after the input cursor, not text entities. "
                : "The source scan was incomplete; no complete-scope clusters, cluster counts or continuation are claimed. ") +
            "Read failures and unavailable extents remain separate in coverage. Follow next_calls with the same source, groupingStrength and includeEntities. A blocked cluster is never split or skipped. " +
            (hostPayloadBlocked
                ? "The blocked cluster exceeds the host payload budget; set includeEntities=false, use weaker groupingStrength or narrow the source, and restart without a cursor. Raising the MCP cap does not bypass that host limit. "
                : cap < MaximumCap
                    ? "Retry the same input cursor with a larger max_output_bytes (up to 65536); a smaller --response-budget-bytes must also be raised. "
                    : "This page already uses the 65536-byte tool maximum; a still-blocked cluster requires includeEntities=false, weaker groupingStrength or a narrower source. ") +
            "Changing includeEntities, groupingStrength or source requires restarting without a cursor. These read-only cluster keys are not writable translation IDs.";

    private static bool Token(JToken value)
    {
        if (value?.Type != JTokenType.String) return false;
        var token = value.Value<string>();
        return token.Length > 0 && token.Length <= 1024 && token.All(c => c >= ' ' && c <= '~');
    }

    private static int EscapedBytes(JToken value)
        => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value.ToString(Formatting.None), McpJsonUtilities.DefaultOptions).Length;

    private static JObject SafeBlocked(JObject data, string identityField = "handle")
    {
        var result = new JObject();
        var identity = data[identityField]?.Type == JTokenType.String ? data.Value<string>(identityField) : null;
        var validIdentity = identityField == "handle"
            ? identity != null && identity.Length is > 0 and <= 16 && identity.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F' or >= 'a' and <= 'f')
            : identity != null && identity.Length is > 0 and <= 128 && identity.All(c => c >= ' ' && c <= '~');
        if (validIdentity) result[identityField] = identity;
        else result[identityField + "_unavailable"] = true;
        var reason = data["reason"]?.Type == JTokenType.String ? data.Value<string>("reason") : null;
        if (reason != null && reason.Length <= 256 && EscapedBytes(new JValue(reason)) <= 256) result["reason"] = reason;
        else { result["reason"] = "blocked_row_details_unavailable"; result["details_omitted"] = true; }
        return result;
    }

    private static CallToolResult BoundOther(CallToolResult result, int cap, bool textRead = false)
    {
        // Even a malformed page must not expose host-only checkpoint fields.
        var originalText = result.Content?.OfType<TextContentBlock>().FirstOrDefault();
        if (originalText != null)
        {
            try
            {
                var original = JObject.Parse(originalText.Text);
                var payload = original["result"] as JObject;
                if (textRead || payload != null && InternalKeys.Any(key => payload.Property(key) != null))
                {
                    foreach (var key in InternalKeys) { payload?.Remove(key); if (textRead) original.Remove(key); }
                    if (textRead && payload?["clusters"] is JArray clusters)
                        foreach (var row in clusters.OfType<JObject>()) { row.Remove("id"); row.Remove("rewrite_mode"); }
                    original["output"] = new JObject { ["budget_bytes"] = cap, ["complete"] = false, ["next_calls"] = new JArray(),
                        ["note"] = textRead
                            ? "Host cursor internals, writable IDs and secondary content were omitted. Inspect the retained error; no continuation was issued. If a source limit was reached, narrow handles or bbox and restart without a cursor; raising the output cap does not raise source limits."
                            : "Host cursor internals and secondary content were omitted from a malformed page. Inspect the retained error; no continuation was issued." };
                    result = new CallToolResult { IsError = result.IsError,
                        Content = new[] { new TextContentBlock { Text = original.ToString(Formatting.None) } } };
                }
            }
            catch (JsonReaderException) { }
        }
        if (ResponseBudget.Measure(result) <= cap) return result;
        var bounded = ResponseBudget.Apply(result, new DwgMcpConfig
        { ResponseBudgetBytes = cap, ResponseWarnBytes = cap, ResponseStrongWarnBytes = cap }, true, null);
        var block = bounded.Content?.OfType<TextContentBlock>().FirstOrDefault();
        if (block == null) return bounded;
        var body = JObject.Parse(block.Text);
        body["output"] = new JObject { ["budget_bytes"] = cap, ["complete"] = false, ["next_calls"] = new JArray(),
            ["note"] = textRead
                ? "Text-read details were omitted; inspect the retained error. No cursor was advanced. Increase max_output_bytes (up to 65536) and --response-budget-bytes if needed. Source limits require narrower handles or bbox and a restart without a cursor."
                : "Query details were omitted; inspect the retained error. No cursor was advanced. Increase max_output_bytes (up to 65536) and --response-budget-bytes if needed." };
        var candidate = Text(body, bounded.IsError);
        return ResponseBudget.Measure(candidate) <= cap ? candidate : bounded;
    }

    private static CallToolResult CoreTooLarge(JObject page, int cap, string collection, string versionKey, string identityField, bool textRead, JObject original)
    {
        var body = new JObject
        {
            ["ok"] = false, ["error"] = collection == "bands" ? "BAND_PAGE_TOO_LARGE: band metadata cannot fit this output budget."
                : textRead
                ? "TEXT_PAGE_TOO_LARGE: text-read metadata or one whole cluster cannot fit this output budget."
                : "QUERY_PAGE_TOO_LARGE: query metadata or one entity cannot fit this output budget.",
            ["result"] = new JObject
            {
                [versionKey] = 1, ["count"] = 0, [collection] = new JArray(),
                ["source"] = new JObject { ["details_omitted"] = true }, ["scope"] = new JObject { ["details_omitted"] = true },
                ["coverage"] = new JObject { ["details_omitted"] = true },
                ["output"] = new JObject
                {
                    ["budget_bytes"] = cap, ["complete"] = false, ["has_more"] = null, ["omitted_matching"] = null,
                    ["continuation_allowed"] = false, ["next_cursor"] = null, ["next_calls"] = new JArray(),
                    ["note"] = collection == "bands" ? "No band or continuation was delivered. Increase the output budget or narrow the filters."
                        : textRead
                        ? "No cluster or continuation was delivered. Increase max_output_bytes up to 65536 and --response-budget-bytes if smaller. If the maximum still cannot fit this page, set includeEntities=false, use weaker groupingStrength or narrow the source, and restart without a cursor; no cluster was split or skipped."
                        : "No entity or continuation was delivered. Increase max_output_bytes up to 65536 and --response-budget-bytes if smaller. " +
                            "If the maximum still cannot fit this page, narrow fields and restart without a cursor; no row was skipped."
                }
            }
        };
        var output = (JObject)body["result"]["output"];
        if (textRead)
        {
            output["omitted_clusters"] = null;
            foreach (var key in new[] { "read_only", "writeback_ready", "legacy_cluster_state_preserved" })
                if (page[key]?.Type == JTokenType.Boolean) body["result"][key] = page[key].DeepClone();
        }
        if (page["output"]?["blocked"] is JObject blocked) output["blocked"] = SafeBlocked(blocked, identityField);
        foreach (var key in new[] { "snapshot", "document_id" })
        {
            var value = page[key]?.Type == JTokenType.String ? page.Value<string>(key) : null;
            if (value != null && value.Length <= 128 && value.All(c => c >= ' ' && c <= '~')) body["result"][key] = value;
            else body["result"][key + "_omitted"] = true;
        }
        ResponseBudget.RetainTarget(original, body);
        var result = Text(body, true);
        if (textRead && ResponseBudget.Measure(result) > cap)
        {
            if (output["blocked"] != null) output["blocked"] = new JObject { ["reason"] = "blocked_cluster_details_omitted", ["details_omitted"] = true };
            foreach (var key in new[] { "snapshot", "document_id" }) { ((JObject)body["result"]).Remove(key); body["result"][key + "_omitted"] = true; }
            result = Text(body, true);
        }
        return BoundOther(result, cap, textRead);
    }

    private static CallToolResult Text(JObject body, bool? failed)
        => new() { IsError = failed, Content = new[] { new TextContentBlock { Text = body.ToString(Formatting.None) } } };
}
