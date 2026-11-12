using System.Linq;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Server.Tools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server;

internal static class AgentReportReminder
{
    internal const string Text = "Before your final answer for this job, call dwg_report_task_result once. "
        + "Include [label](dwg:<handle>) links to relevant entities when available.";

    internal static bool Enabled => ServerState.EnabledToolTypes.Contains(typeof(ReportTools));

    internal static string Serialize(string command, McpResponse response)
    {
        if (response?.Ok != true || !Enabled || !IsWrite(command))
            return JsonConvert.SerializeObject(response);

        var data = JObject.FromObject(response);
        if (!Succeeded(command, data["result"])) return JsonConvert.SerializeObject(response);
        data["_report"] = Text;
        return data.ToString(Formatting.None);
    }

    private static bool IsWrite(string command)
        => !string.IsNullOrEmpty(command) && TargetContract.IsWrite(command)
            && command is not ("open_drawing" or "capture_view_image" or "inspect_view_region" or "restore_view" or "apply_bake");

    private static bool Succeeded(string command, JToken result)
    {
        // These handlers can return a successful envelope without a successful write.
        var body = result as JObject;
        return command switch
        {
            "send_code" => IsBoolean(body?["ok"], true) && IsBoolean(body?["executed"], true),
            "purge_drawing" => IsBoolean(body?["dry_run"], false),
            "save_drawing" or "export_dxf" => IsBoolean((body?["file_receipt"] as JObject)?["verified"], true),
            "batch_execute" => !IsBoolean(body?["rolled_back"], true) && body?["results"] is JArray steps
                && steps.OfType<JObject>().Any(SuccessfulWriteStep),
            "update_texts" or "collapse_and_rewrite" or "translate_and_rewrite"
                => (result as JArray ?? body?["results"] as JArray)?.OfType<JObject>().Any(item => IsBoolean(item["ok"], true)) == true,
            _ => true
        };
    }

    private static bool SuccessfulWriteStep(JObject step)
    {
        var command = step["cmd"]?.Type == JTokenType.String ? step.Value<string>("cmd") : null;
        return IsBoolean(step["ok"], true) && !IsBoolean(step["mutation_applied"], false)
            && IsWrite(command) && Succeeded(command, step["result"]);
    }

    private static bool IsBoolean(JToken token, bool value)
        => token?.Type == JTokenType.Boolean && token.Value<bool>() == value;
}
