using System;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Bimwright.Dwg.Plugin;

namespace Bimwright.Dwg.Server;

/// <summary>Size of the final MCP result, including content escaping and metadata.</summary>
internal static class ResponseBudget
{
    internal static int Measure(CallToolResult result)
        => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(result, McpJsonUtilities.DefaultOptions).Length;

    // Register before the argument guard: SDK filters registered first are outermost.
    internal static IMcpServerBuilder Register(IMcpServerBuilder mcp, DwgMcpConfig config)
        => mcp.WithRequestFilters(filters => filters.AddCallToolFilter(next => async (request, ct) =>
        {
            var result = await next(request, ct); // Execute exactly once, including writes.
            var textRead = IsTextReadRequest(request.Params);
            if (textRead || IsEntityQueryExtendedRequest(request.Params))
            {
                var countOnly = request.Params.Name == "dwg_count_entities";
                var limit = Math.Min(countOnly ? EntityQueryResponse.DefaultCap : EntityQueryResponse.ReadRequestedCap(request.Params), config.ResponseBudgetBytesOrDefault);
                // Fit signed per-row checkpoints exactly once, after argument decoration.
                // Optional global warnings may add metadata, never trim the delivered page.
                var fitted = textRead ? TextReadResponse.Fit(result, limit) : EntityQueryResponse.Fit(result, limit, countOnly);
                var previousMeta = fitted.Meta?.DeepClone().AsObject();
                var decorated = Apply(fitted, config, true, request.Params.Name);
                if (Measure(decorated) > limit) decorated.Meta = previousMeta;
                return decorated;
            }
            var profile = request.Params?.Name == "dwg_get_drawing_profile";
            var profileLimit = profile ? Math.Min(DrawingProfileResponse.ReadRequestedCap(request.Params), config.ResponseBudgetBytesOrDefault) : 0;
            var profileSpace = profile ? DrawingProfileResponse.ReadRequestedSpace(request.Params) : "all";
            if (profile) result = DrawingProfileResponse.Fit(result, profileLimit, profileSpace);
            result = Apply(result, config,
                (request.MatchedPrimitive as McpServerTool)?.ProtocolTool.Annotations?.ReadOnlyHint == true,
                request.Params?.Name);
            // Argument warnings and global warning metadata must also fit the tool's cap.
            return profile ? DrawingProfileResponse.Fit(result, profileLimit, profileSpace) : result;
        }));

    internal static bool IsTextReadRequest(CallToolRequestParams request)
        => request?.Name == "dwg_get_selected_texts" && request.Arguments != null
            && new[] { "handles", "bbox", "bbox_mode", "space", "cursor", "max_output_bytes" }.Any(key =>
                request.Arguments.TryGetValue(key, out var value) && value.ValueKind != System.Text.Json.JsonValueKind.Null);

    internal static bool IsEntityQueryExtendedRequest(CallToolRequestParams request)
    {
        if (request?.Arguments == null || request.Name is not ("dwg_query_entities" or "dwg_count_entities")) return false;
        var keys = request.Name == "dwg_query_entities"
            ? new[] { "bbox", "bbox_mode", "space", "fields", "cursor", "max_output_bytes" }
            : new[] { "bbox", "bbox_mode", "space" };
        if (keys.Any(key => request.Arguments.TryGetValue(key, out var value)
            && value.ValueKind != System.Text.Json.JsonValueKind.Null)) return true;
        return request.Name == "dwg_query_entities" && request.Arguments.TryGetValue("paged", out var paged)
            && paged.ValueKind is not (System.Text.Json.JsonValueKind.False or System.Text.Json.JsonValueKind.Null);
    }

    internal static CallToolResult Apply(CallToolResult result, DwgMcpConfig config, bool readOnly, string toolName)
    {
        config.ValidateResponseBudget();
        var bytes = Measure(result);
        var limit = config.ResponseBudgetBytesOrDefault;
        if (bytes < config.ResponseWarnBytesOrDefault) return result;
        if (bytes <= limit)
        {
            var previousMeta = result.Meta;
            result.Meta = previousMeta?.DeepClone().AsObject() ?? new JsonObject();
            result.Meta["bimwright/dwg-mcp/response_budget"] = new JsonObject
            {
                ["level"] = bytes >= config.ResponseStrongWarnBytesOrDefault ? "strong_warning" : "warning",
                ["original_bytes"] = bytes, ["budget_bytes"] = limit
            };
            if (Measure(result) <= limit) return result;
            // A warning must not displace an otherwise valid response or image.
            result.Meta = previousMeta;
            return result;
        }

        // Output delivery is limited AFTER the tool returns. Neither annotation nor
        // outer ok proves native success, rollback, or absence of earlier effects.
        var data = Parse(result.Content?.OfType<TextContentBlock>().FirstOrDefault()?.Text);
        var artifact = OutputArtifactStore.Write(System.Text.Json.JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions), "mcp_tool_result");
        var budgetInfo = new JObject
        {
            ["status"] = "compacted", ["original_bytes"] = bytes, ["budget_bytes"] = limit,
            ["content_omitted"] = true, ["details_omitted"] = true,
            ["original_is_error"] = result.IsError,
            ["outcome"] = "Only retained tool-reported facts are known; omitted output does not establish success, rollback, or no effects.",
            ["guidance"] = readOnly
                ? "Read output_artifact if available; otherwise narrow future reads. Completeness applies to the MCP result received, not output already omitted upstream."
                : "Do not replay this operation to recover output. Read output_artifact if available and inspect drawing/files; effects may already exist. Completeness applies to this MCP result, not output already omitted upstream."
        };
        foreach (var textLimit in new[] { 256, 64, 0, -1 })
        {
            var receipt = Facts(data, textLimit);
            RetainTarget(data, receipt, Math.Max(0, textLimit));
            if (data == null && result.Content?.OfType<TextContentBlock>().FirstOrDefault() is TextContentBlock plain)
            {
                receipt["message"] = plain.Text.Substring(0, Math.Min(Math.Max(0, textLimit), plain.Text.Length));
                receipt["message_omitted"] = true;
            }
            if (data?["result"] is JObject inner)
            {
                receipt["result"] = Facts(inner, textLimit);
                if (toolName != "dwg_send_code" && inner["results"] is JArray items)
                    ItemFacts(items, (JObject)receipt["result"], textLimit);
            }
            else if (data?["result"] is JArray items) { var summary = new JObject(); ItemFacts(items, summary, textLimit); receipt["result"] = summary; }
            if (data?["image_delivery"] is JObject delivery)
                receipt["image_delivery"] = Facts(delivery, textLimit);
            if (result.Content?.OfType<ImageContentBlock>().Any() == true)
                receipt["image_delivery"] = new JObject { ["ok"] = false, ["omitted"] = true, ["reason"] = "final_response_budget" };
            receipt["_response_budget"] = budgetInfo.DeepClone();
            receipt["output_artifact"] = OutputReceiptFacts.Receipt(artifact, Math.Max(0, textLimit));
            var compact = new CallToolResult
            {
                IsError = result.IsError,
                Content = new[] { new TextContentBlock { Text = receipt.ToString(Formatting.None) } }
            };
            if (Measure(compact) <= limit) return compact;
        }
        // Arbitrary diagnostic/numeric values must never turn a completed call into
        // a generic SDK exception. This fixed vocabulary fits the 4 KiB minimum.
        var minimal = MinimalFacts(data);
        RetainTarget(data, minimal, 0);
        if (data?["result"] is JObject innerData) minimal["result"] = MinimalFacts(innerData);
        if (data?["image_delivery"] is JObject imageData) minimal["image_delivery"] = MinimalFacts(imageData);
        if (result.Content?.OfType<ImageContentBlock>().Any() == true)
            minimal["image_delivery"] = new JObject { ["ok"] = false, ["omitted"] = true };
        budgetInfo["minimal_receipt"] = true;
        minimal["_response_budget"] = budgetInfo;
        minimal["output_artifact"] = OutputReceiptFacts.Receipt(artifact, 0);
        var fallback = new CallToolResult { IsError = result.IsError,
            Content = new[] { new TextContentBlock { Text = minimal.ToString(Formatting.None) } } };
        var finalBytes = Measure(fallback);
        if (finalBytes > limit)
        {
            // A long/escaped local identity must not break the final byte cap.
            // Keep the real hash and full file; never return a shortened fake path.
            var pointer = (JObject)minimal["output_artifact"];
            pointer["path"] = null; pointer["path_omitted"] = true;
            pointer["reason"] = "artifact_path_exceeds_response_budget";
            fallback.Content = new[] { new TextContentBlock { Text = minimal.ToString(Formatting.None) } };
            finalBytes = Measure(fallback);
        }
        System.Diagnostics.Debug.Assert(finalBytes <= limit);
        return fallback;
    }

    // Retain only observed envelope metadata. Never use request/binding data or
    // a user's nested result._target, and never shorten an identity into another one.
    internal static void RetainTarget(JObject source, JObject destination, int titleLimit = 0)
    {
        if (source?["_target"] is not JObject target) return;
        var receipt = new JObject();
        foreach (var key in new[] { "protocol", "target_id", "pid", "process_start_utc", "acad_year", "document_id", "document_title" })
        {
            var value = target[key];
            if (value == null) continue;
            var cap = key == "document_title" ? titleLimit : 256;
            if (value is JValue && (value.Type is JTokenType.String or JTokenType.Integer or JTokenType.Date or JTokenType.Null)
                && (value.Type == JTokenType.Null || System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value.ToString(Formatting.None), McpJsonUtilities.DefaultOptions).Length <= cap))
                receipt[key] = value.DeepClone();
            else receipt[key + "_omitted"] = true;
        }
        destination["_target"] = receipt;
    }

    private static JObject MinimalFacts(JObject data)
    {
        var facts = new JObject();
        if (data == null) return facts;
        foreach (var key in new[] { "ok", "success", "blocked", "execution_authorized", "partial_failure",
            "transactional", "truncated", "mutation_applied", "output_verified" })
            if (data[key]?.Type is JTokenType.Boolean or JTokenType.Null) facts[key] = data[key].DeepClone();
        var outcome = data["outcome"]?.Type == JTokenType.String ? data.Value<string>("outcome") : null;
        if (new[] { "unknown", "partial", "success", "succeeded", "failed", "not_started", "completed", "completed_output_unverified", "cancelled", "running", "refused" }.Contains(outcome))
            facts["outcome"] = outcome;
        else if (data["outcome"] != null) facts["outcome_omitted"] = true;
        var code = data["error_code"] ?? (data["error"] as JObject)?["code"];
        if (code?.Type == JTokenType.String && code.Value<string>().Length <= 64
            && code.Value<string>().All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_'))
            facts["error_code"] = code.DeepClone();
        if (data["error"] != null) facts["error_details_omitted"] = true;
        if (data["effects"] != null) facts["effects_details_omitted"] = true;
        return facts;
    }

    private static JObject Parse(string text)
    {
        try { return JToken.Parse(text ?? "") as JObject; }
        catch (JsonReaderException) { return null; }
    }

    private static JObject Facts(JObject data, int textLimit)
    {
        var facts = new JObject();
        if (data == null) return facts;
        // Fixed response-envelope paths only. A user's send_code result.result DTO
        // may contain arbitrary ok/error fields; it is not an execution receipt.
        foreach (var key in new[] { "ok", "success", "blocked", "error_code", "error", "message",
            "execution_authorized", "partial_failure", "transactional", "rollback", "truncated",
            "original_length", "original_size_bytes", "outcome", "mutation_applied", "effects",
            "index", "command_ok", "output_verified", "id", "cmd", "tool_name", "state", "handle", "new_handle", "erased", "name", "created", "value",
            "layer", "output_path", "saved_path", "path", "title", "read_only", "opened", "was_already_open",
            "activated", "active_document", "style_created", "font_downloaded", "font_path",
            "reassigned_count", "dry_run", "purged_count", "count", "deleted_count", "created_count", "modified_count", "sha256" })
        {
            var value = data[key];
            if (value == null) continue;
            if (textLimit < 0 && key is not ("ok" or "success" or "blocked" or "error" or "error_code" or "execution_authorized"
                or "partial_failure" or "transactional" or "truncated" or "outcome" or "mutation_applied")) continue;
            if (value is JValue scalar && scalar.Type != JTokenType.String)
            {
                if (scalar.ToString(Formatting.None).Length <= 64) facts[key] = scalar.DeepClone();
                else facts[key + "_omitted"] = true;
            }
            else if (value.Type == JTokenType.String)
            {
                var text = value.Value<string>();
                // Keep short status codes even when shedding long diagnostics.
                var cap = key is "outcome" or "error_code" or "error" ? Math.Max(64, textLimit) : textLimit;
                if (text.Length <= cap) facts[key] = text;
                else if (cap > 0) { facts[key] = text.Substring(0, cap); facts[key + "_omitted"] = true; }
            }
            else if (key == "error" && value is JObject error)
            {
                // Health uses error:{code,message}, unlike typed tools' error string.
                var summary = new JObject();
                foreach (var name in new[] { "code", "message" })
                {
                    if (error[name]?.Type != JTokenType.String) continue;
                    var text = error.Value<string>(name);
                    var cap = name == "code" ? 64 : Math.Max(0, textLimit);
                    summary[name] = text.Substring(0, Math.Min(cap, text.Length));
                    if (text.Length > cap) summary[name + "_omitted"] = true;
                }
                facts[key] = summary;
            }
            else if (key == "effects" && value is JObject effects)
            {
                // Retain bounded scalar effect facts; geometry/lists are explicitly omitted.
                var summary = new JObject();
                foreach (var p in effects.Properties().Take(8))
                {
                    if (p.Name.Length > 64 || p.Value is not JValue) continue;
                    if (p.Value.Type != JTokenType.String || p.Value.Value<string>().Length <= Math.Min(64, textLimit))
                        summary[p.Name] = p.Value.DeepClone();
                }
                facts[key] = summary;
                facts[key + "_details_omitted"] = true;
            }
            else facts[key + "_omitted"] = true;
        }
        if (data["created_handles"] is JArray handles) facts["created_handles_reported_count"] = handles.Count;
        foreach (var key in new[] { "output_artifact", "file_receipt" })
            if (data[key] is JObject receipt) facts[key] = OutputReceiptFacts.Receipt(receipt, Math.Max(0, textLimit));
        return facts;
    }

    private static void ItemFacts(JArray items, JObject facts, int textLimit)
    {
        facts["items_reported"] = items.Count;
        facts["items_reported_ok"] = items.OfType<JObject>().Count(x => x["ok"]?.Type == JTokenType.Boolean && x.Value<bool>("ok"));
        facts["items_reported_unknown"] = items.OfType<JObject>().Count(x => x["ok"]?.Type == JTokenType.Boolean && !x.Value<bool>("ok") && x.Value<string>("outcome") == "unknown");
        facts["items_reported_failed"] = items.OfType<JObject>().Count(x => x["ok"]?.Type == JTokenType.Boolean && !x.Value<bool>("ok") && x.Value<string>("outcome") != "unknown");
        facts["items_output_unverified"] = items.OfType<JObject>().Count(x => x.Value<string>("outcome") == "completed_output_unverified");
        facts["items_without_status"] = items.Count - facts.Value<int>("items_reported_ok") - facts.Value<int>("items_reported_failed") - facts.Value<int>("items_reported_unknown");
        facts["item_details_omitted"] = true;
        if (textLimit > 0)
            facts["failed_item_sample"] = new JArray(items.Select((item, index) => (item, index))
                .Where(x => x.item is JObject o && o["ok"]?.Type == JTokenType.Boolean && !o.Value<bool>("ok") && o.Value<string>("outcome") != "unknown")
                .Take(3).Select(x => { var sample = Facts((JObject)x.item, textLimit); sample["index"] = x.index; return sample; }));
    }
}
