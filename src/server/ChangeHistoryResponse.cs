using System;
using System.Linq;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server;

internal static class ChangeHistoryResponse
{
    private const int DefaultCap = 128 * 1024;

    internal static bool Applies(string tool) => tool is "dwg_get_change_records" or "dwg_survey_change_impact";

    internal static void ValidateCap(int? cap)
    {
        if (cap < DwgMcpConfig.MinResponseBudgetBytes) throw new ArgumentException("max_response_bytes must be at least 4096.");
    }

    internal static int Cap(int? requested, DwgMcpConfig config)
        => Math.Min(requested ?? DefaultCap, config?.ResponseBudgetBytesOrDefault ?? DwgMcpConfig.DefaultResponseBudgetBytes);

    internal static int RequestCap(CallToolRequestParams request, DwgMcpConfig config)
        => Cap(request.Arguments != null && request.Arguments.TryGetValue("max_response_bytes", out var arg)
            && arg.ValueKind == JsonValueKind.Number && arg.TryGetInt32(out var cap) && cap >= DwgMcpConfig.MinResponseBudgetBytes ? cap : null, config);

    internal static string Encode(McpResponse response, int? requested)
    {
        var envelope = JObject.FromObject(response);
        var body = envelope["result"] as JObject;
        var checkpoints = body?["_checkpoints"] as JArray;
        body?.Remove("_checkpoints");
        var cap = Cap(requested, ServerState.Config);
        var original = envelope.ToString(Formatting.None);
        if (Size(original) <= cap) return original;

        if (body?["calls"] is JArray calls && checkpoints?.Count == calls.Count)
        {
            while (calls.Count > 1)
            {
                calls.RemoveAt(calls.Count - 1);
                body["next_cursor"] = checkpoints[calls.Count - 1].DeepClone();
                var page = envelope.ToString(Formatting.None);
                if (Size(page) <= cap) return page;
            }
        }
        return TooLarge(envelope);
    }

    internal static CallToolResult FitFinal(CallToolResult result, int cap)
    {
        if (ResponseBudget.Measure(result) <= cap) return result;
        var text = result.Content?.OfType<TextContentBlock>().FirstOrDefault()?.Text;
        JObject body = null;
        if (text != null) { try { body = JObject.Parse(text); } catch (Newtonsoft.Json.JsonException) { } }
        return new CallToolResult { IsError = true, Content = new[] { new TextContentBlock { Text = TooLarge(body) } } };
    }

    private static int Size(string text) => ResponseBudget.Measure(new CallToolResult
    {
        Content = new[] { new TextContentBlock { Text = text } }
    });

    private static string TooLarge(JObject original)
    {
        var error = new JObject { ["ok"] = false, ["error"] = "HISTORY_OUTPUT_TOO_LARGE: increase max_response_bytes or narrow the request." };
        ResponseBudget.RetainTarget(original, error);
        return error.ToString(Formatting.None);
    }
}
