using System;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
            return Apply(result, config,
                (request.MatchedPrimitive as McpServerTool)?.ProtocolTool.Annotations?.ReadOnlyHint == true,
                request.Params?.Name);
        }));

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
        var budgetInfo = new JObject
        {
            ["status"] = "compacted", ["original_bytes"] = bytes, ["budget_bytes"] = limit,
            ["content_omitted"] = true, ["details_omitted"] = true,
            ["original_is_error"] = result.IsError,
            ["outcome"] = "Only retained tool-reported facts are known; omitted output does not establish success, rollback, or no effects.",
            ["guidance"] = readOnly
                ? "Narrow the selection or request fewer fields/smaller batches. No full-output file was written."
                : "Do not replay this operation to recover output. Inspect drawing/files and tool-reported outcome first; effects may already exist. No full-output file was written."
        };
        foreach (var textLimit in new[] { 256, 64, 0, -1 })
        {
            var receipt = Facts(data, textLimit);
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
        if (data?["result"] is JObject innerData) minimal["result"] = MinimalFacts(innerData);
        if (data?["image_delivery"] is JObject imageData) minimal["image_delivery"] = MinimalFacts(imageData);
        if (result.Content?.OfType<ImageContentBlock>().Any() == true)
            minimal["image_delivery"] = new JObject { ["ok"] = false, ["omitted"] = true };
        budgetInfo["minimal_receipt"] = true;
        minimal["_response_budget"] = budgetInfo;
        var fallback = new CallToolResult { IsError = result.IsError,
            Content = new[] { new TextContentBlock { Text = minimal.ToString(Formatting.None) } } };
        var finalBytes = Measure(fallback);
        System.Diagnostics.Debug.Assert(finalBytes <= limit);
        return fallback;
    }

    private static JObject MinimalFacts(JObject data)
    {
        var facts = new JObject();
        if (data == null) return facts;
        foreach (var key in new[] { "ok", "success", "blocked", "execution_authorized", "partial_failure",
            "transactional", "truncated", "mutation_applied" })
            if (data[key]?.Type is JTokenType.Boolean or JTokenType.Null) facts[key] = data[key].DeepClone();
        var outcome = data["outcome"]?.Type == JTokenType.String ? data.Value<string>("outcome") : null;
        if (new[] { "unknown", "partial", "success", "succeeded", "failed", "not_started", "completed", "cancelled", "running", "refused" }.Contains(outcome))
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
            "id", "cmd", "tool_name", "state", "handle", "new_handle", "erased", "name", "created", "value",
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
        return facts;
    }

    private static void ItemFacts(JArray items, JObject facts, int textLimit)
    {
        facts["items_reported"] = items.Count;
        facts["items_reported_ok"] = items.OfType<JObject>().Count(x => x["ok"]?.Type == JTokenType.Boolean && x.Value<bool>("ok"));
        facts["items_reported_failed"] = items.OfType<JObject>().Count(x => x["ok"]?.Type == JTokenType.Boolean && !x.Value<bool>("ok"));
        facts["items_without_status"] = items.Count - facts.Value<int>("items_reported_ok") - facts.Value<int>("items_reported_failed");
        facts["item_details_omitted"] = true;
        if (textLimit > 0)
            facts["failed_item_sample"] = new JArray(items.Select((item, index) => (item, index))
                .Where(x => x.item is JObject o && o["ok"]?.Type == JTokenType.Boolean && !o.Value<bool>("ok"))
                .Take(3).Select(x => { var sample = Facts((JObject)x.item, textLimit); sample["index"] = x.index; return sample; }));
    }
}
