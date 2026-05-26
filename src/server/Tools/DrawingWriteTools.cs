using System.ComponentModel;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server.Tools
{
    [McpServerToolType]
    public class DrawingWriteTools
    {
        [McpServerTool(Name = "dwg_set_system_variable", ReadOnly = false), Description(
            "Set the value of a drawing system variable. Rejects variable names not in the write allowlist.")]
        public static Task<string> SetSystemVariable(
            [Description("Name of the system variable to set.")] string name,
            [Description("New value. Types are coerced automatically depending on the variable.")] JToken value)
        {
            var request = new JObject
            {
                ["name"] = name,
                ["value"] = value
            };
            return ToolGateway.LoggedCall("set_system_variable", request, request);
        }

        [McpServerTool(Name = "dwg_save_drawing", ReadOnly = false), Description(
            "Save the current drawing. If output_path is omitted, saves the current drawing file (requires confirm=true).")]
        public static Task<string> SaveDrawing(
            [Description("Optional absolute path to save the drawing to. If specified, behaves like SaveAs.")] string output_path = null,
            [Description("Must be set to true when saving to the active drawing file without a path.")] bool? confirm = null)
        {
            var request = new JObject();
            if (output_path != null) request["output_path"] = output_path;
            if (confirm.HasValue) request["confirm"] = confirm.Value;

            return ToolGateway.LoggedCall("save_drawing", request, request);
        }

        [McpServerTool(Name = "dwg_purge_drawing", ReadOnly = false), Description(
            "Purge unused named objects (layers, blocks, styles) from the current drawing.")]
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
