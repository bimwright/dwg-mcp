using System;
using System.ComponentModel;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Bimwright.Dwg.Plugin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server.Tools
{
    [McpServerToolType]
    public class BatchTools
    {
        [McpServerTool(Name = "dwg_batch_execute", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false), Description(
            "Run multiple internal DWG commands sequentially as a logical batch. commands must be a JSON array of {cmd, params}. " +
            "run_lisp is blocked in this build, including batches; use dwg_inspect_lisp for analysis only." +
            " Each step reports index, command_ok, effective ok, outcome and nullable mutation_applied; file-output steps also report output_verified. Read nested results/file_receipt and output_artifact for omitted details. " +
            "May change drawing data or files; disabled in read-only mode even when a particular batch only contains reads. Commands are sequential, not one atomic transaction: earlier effects can remain after a later failure. Use AutoCAD UNDO for drawing edits and backups for file changes.")]
        public static Task<string> BatchExecute(
            [Description("JSON array: [{\"cmd\":\"get_selected_texts\",\"params\":{}}]. Wire command names are unprefixed.")] string commands)
        {
            var parsed = string.IsNullOrWhiteSpace(commands) ? new JArray() : JArray.Parse(commands);
            foreach (var item in parsed)
            {
                if (ScriptInputContract.RequiresDirectCall(item?["cmd"]?.Value<string>(), item?["params"]))
                    return Task.FromResult(JsonConvert.SerializeObject(new { ok = false,
                        error = "SCRIPT_INPUTS_DIRECT_TOOL_ONLY: call enhanced send_code directly. The batch was not sent or started." }));
                if (EntityQueryOptions.RequiresDirectCall(item?["cmd"]?.Value<string>(), item?["params"]))
                    return Task.FromResult(JsonConvert.SerializeObject(new
                    {
                        ok = false, error = "PAGED_QUERY_DIRECT_TOOL_ONLY: call spatial/scoped/paged read tools directly. The batch was not sent or started."
                    }));
            }
            var request = new JObject { ["commands"] = parsed };
            return ToolGateway.LoggedCall("batch_execute", request, request);
        }
    }
}
