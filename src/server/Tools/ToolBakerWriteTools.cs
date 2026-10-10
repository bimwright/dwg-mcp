using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Bimwright.Dwg.Server.Bake;
using Bimwright.Dwg.Server.Handlers;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server.Tools
{
    [McpServerToolType]
    public class ToolBakerWriteTools
    {
        [McpServerTool(Name = "dwg_run_baked_tool", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false), Description(
            "Run a baked DWG tool by name. Params must be a JSON object for the baked tool." +
            " May change drawing data and files according to the accepted tool. Review the tool before running; use AutoCAD UNDO for drawing edits, but file changes require a backup or manual cleanup. No universal rollback is provided; disabled in read-only mode.")]
        public static Task<string> RunBakedTool(
            [Description("Baked tool name.")] string name,
            [Description("Optional JSON object with runtime parameters.")] string @params = "{}")
        {
            if (ServerState.IsReadOnly) return Task.FromResult(ServerState.ReadOnlyError("dwg_run_baked_tool"));

            JObject parsed;
            try
            {
                parsed = string.IsNullOrWhiteSpace(@params) ? new JObject() : JObject.Parse(@params);
            }
            catch (JsonException ex)
            {
                return Task.FromResult(JsonConvert.SerializeObject(new McpResponse { Ok = false, Error = "params must be a JSON object: " + ex.Message }));
            }

            var paths = ToolBakerTools.PathsOverride ?? new BakePaths();
            using var db = new BakeDb(paths);
            db.Migrate();
            var record = db.GetRegistryRecord(name);
            if (record == null)
            {
                return Task.FromResult(JsonConvert.SerializeObject(new McpResponse { Ok = false, Error = "baked tool not found: " + name }));
            }

            return ToolGateway.LoggedCall(
                "run_baked_tool",
                new { name, @params = parsed },
                new { name, @params = parsed, tool_record = JObject.FromObject(record) });
        }

        [McpServerTool(Name = "dwg_accept_bake_suggestion", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false), Description(
            "Accept a ToolBaker suggestion, apply it in the AutoCAD plugin, and record it in the server registry." +
            " Creates persistent plugin/server registry data and changes the suggestion state. These changes are outside AutoCAD UNDO; back up ToolBaker data first. There is no automatic undo; disabled in read-only mode.")]
        public static async Task<string> AcceptBakeSuggestion(
            [Description("Suggestion id from dwg_list_bake_suggestions.")] string id,
            [Description("Snake_case baked tool name.")] string name,
            [Description("Output choice. v1 supports mcp_only.")] string output_choice = "mcp_only",
            [Description("Optional JSON object schema override.")] string params_schema = null)
        {
            if (ServerState.IsReadOnly) return ServerState.ReadOnlyError("dwg_accept_bake_suggestion");

            var paths = ToolBakerTools.PathsOverride ?? new BakePaths();
            using var db = new BakeDb(paths);
            db.Migrate();
            return await AcceptBakeSuggestionHandler.HandleAsync(
                db,
                id,
                name,
                output_choice,
                params_schema,
                pluginApply: async request =>
                {
                    var response = await ToolGateway.SendRaw("apply_bake", request);
                    if (!response.Ok)
                    {
                        return new JObject
                        {
                            ["success"] = false,
                            ["error_code"] = "plugin_apply_failed",
                            ["message"] = response.Error
                        };
                    }
                    return JObject.FromObject(response.Result ?? new { });
                });
        }

        [McpServerTool(Name = "dwg_dismiss_bake_suggestion", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false), Description(
            "Dismiss a ToolBaker suggestion. action must be snooze_30d, never, or never_with_gap_signal." +
            " Persists the dismissed state in the local bake database, removing the prior active suggestion state. This is outside AutoCAD UNDO and has no undo tool; restore backed-up ToolBaker data if needed. Disabled in read-only mode.")]
        public static Task<string> DismissBakeSuggestion(string id, string action)
        {
            if (ServerState.IsReadOnly) return Task.FromResult(ServerState.ReadOnlyError("dwg_dismiss_bake_suggestion"));

            var paths = ToolBakerTools.PathsOverride ?? new BakePaths();
            using var db = new BakeDb(paths);
            db.Migrate();
            return Task.FromResult(DismissBakeSuggestionHandler.Handle(db, id, action));
        }
    }
}
