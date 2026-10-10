using System.ComponentModel;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server.Tools
{
    [McpServerToolType]
    public class DrawingWriteTools
    {
        [McpServerTool(Name = "dwg_set_system_variable", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false), Description(
            "Set the value of a drawing system variable. Rejects variable names not in the write allowlist." +
            " Changes the named variable in the drawing or AutoCAD session. Record its prior value with dwg_get_variables and set it back to undo; some session settings are not covered by AutoCAD UNDO.")]
        public static Task<string> SetSystemVariable(
            [Description("Name of the system variable to set.")] string name,
            [Description("New value as a JSON number or string, for example 1, 0.5 or \"Standard\". It is coerced to the variable's type.")] System.Text.Json.JsonElement value)
        {
            // The MCP SDK binds with System.Text.Json and cannot create a Newtonsoft JToken.
            var request = new JObject
            {
                ["name"] = name,
                ["value"] = JToken.Parse(value.GetRawText())
            };
            return ToolGateway.LoggedCall("set_system_variable", request, request);
        }

        [McpServerTool(Name = "dwg_save_drawing", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false), Description(
            "Save the current drawing. If output_path is omitted, saves the current drawing file (requires confirm=true)." +
            " Writes DWG bytes and may overwrite the active file or an explicitly allowed destination. AutoCAD UNDO does not restore overwritten file bytes; keep and restore a backup. Delete a newly created copy if it is no longer needed.")]
        public static Task<string> SaveDrawing(
            [Description("Optional absolute path to save the drawing to. If specified, behaves like SaveAs.")] string output_path = null,
            [Description("Must be set to true when saving to the active drawing file without a path.")] bool? confirm = null,
            [Description("If true, allow overwriting an existing file when output_path is specified.")] bool? overwrite_existing = null,
            [Description("If true, allow saving the drawing inside the repository directory.")] bool? allow_repo_output = null)
        {
            var request = new JObject();
            if (output_path != null) request["output_path"] = output_path;
            if (confirm.HasValue) request["confirm"] = confirm.Value;
            if (overwrite_existing.HasValue) request["overwrite_existing"] = overwrite_existing.Value;
            if (allow_repo_output.HasValue) request["allow_repo_output"] = allow_repo_output.Value;

            return ToolGateway.LoggedCall("save_drawing", request, request);
        }

        [McpServerTool(Name = "dwg_purge_drawing", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false), Description(
            "Purge unused named objects (layers, blocks, styles) from the current drawing." +
            " dry_run=true only previews candidates; actual purge requires confirm=true. Use AutoCAD UNDO immediately to restore purged drawing objects before saving.")]
        public static Task<string> PurgeDrawing(
            [Description("If true, list items that would be purged without actually purging them.")] bool? dry_run = null,
            [Description("Must be set to true when dry_run is false to execute the purge.")] bool? confirm = null)
        {
            var request = new JObject();
            if (dry_run.HasValue) request["dry_run"] = dry_run.Value;
            if (confirm.HasValue) request["confirm"] = confirm.Value;

            return ToolGateway.LoggedCall("purge_drawing", request, request);
        }
    }
}
