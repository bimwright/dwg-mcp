using System;
using System.ComponentModel;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server.Tools
{
    [McpServerToolType]
    public class BatchTools
    {
        [McpServerTool(Name = "dwg_batch_execute", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false), Description(
            "Run multiple internal DWG commands sequentially as a logical batch. commands must be a JSON array of {cmd, params}. " +
            "run_lisp is blocked in this build, including batches; use dwg_inspect_lisp for analysis only." +
            " May change drawing data or files; disabled in read-only mode even when a particular batch only contains reads. Commands are sequential, not one atomic transaction: earlier effects can remain after a later failure. Use AutoCAD UNDO for drawing edits and backups for file changes.")]
        public static Task<string> BatchExecute(
            [Description("JSON array: [{\"cmd\":\"get_selected_texts\",\"params\":{}}]. Wire command names are unprefixed.")] string commands)
        {
            var parsed = string.IsNullOrWhiteSpace(commands) ? new JArray() : JArray.Parse(commands);
            var request = new JObject { ["commands"] = parsed };
            return ToolGateway.LoggedCall("batch_execute", request, request);
        }
    }
}
