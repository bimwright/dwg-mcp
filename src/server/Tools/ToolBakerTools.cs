using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Bimwright.Dwg.Server.Bake;
using Bimwright.Dwg.Server.Handlers;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server.Tools
{
    [McpServerToolType]
    public class ToolBakerTools
    {
        [McpServerTool(Name = "dwg_list_baked_tools", ReadOnly = true, Idempotent = true), Description(
            "List baked DWG tools accepted into the server registry.")]
        public static Task<string> ListBakedTools()
        {
            var paths = new BakePaths();
            using var db = new BakeDb(paths);
            db.Migrate();
            var tools = db.ReadRegistryRecords()
                .Select(record => new
                {
                    name = record.Name,
                    description = record.Description,
                    source = record.Source,
                    handler_tool = record.HandlerTool,
                    usage_count = record.UsageCount,
                    created_at = record.CreatedAt
                })
                .ToArray();
            return Task.FromResult(JsonConvert.SerializeObject(new { tools }));
        }

        [McpServerTool(Name = "dwg_run_baked_tool"), Description(
            "Run a baked DWG tool by name. Params must be a JSON object for the baked tool.")]
        public static Task<string> RunBakedTool(
            [Description("Baked tool name.")] string name,
            [Description("Optional JSON object with runtime parameters.")] string @params = "{}")
        {
            JObject parsed;
            try
            {
                parsed = string.IsNullOrWhiteSpace(@params) ? new JObject() : JObject.Parse(@params);
            }
            catch (JsonException ex)
            {
                return Task.FromResult(JsonConvert.SerializeObject(new McpResponse { Ok = false, Error = "params must be a JSON object: " + ex.Message }));
            }

            var paths = new BakePaths();
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

        [McpServerTool(Name = "dwg_list_bake_suggestions", ReadOnly = true, Idempotent = true), Description(
            "List adaptive ToolBaker suggestions from the server bake database.")]
        public static Task<string> ListBakeSuggestions()
        {
            var paths = new BakePaths();
            using var db = new BakeDb(paths);
            db.Migrate();
            return Task.FromResult(ListBakeSuggestionsHandler.Handle(db));
        }

        [McpServerTool(Name = "dwg_accept_bake_suggestion"), Description(
            "Accept a ToolBaker suggestion, apply it in the AutoCAD plugin, and record it in the server registry.")]
        public static async Task<string> AcceptBakeSuggestion(
            [Description("Suggestion id from dwg_list_bake_suggestions.")] string id,
            [Description("Snake_case baked tool name.")] string name,
            [Description("Output choice. v1 supports mcp_only.")] string output_choice = "mcp_only",
            [Description("Optional JSON object schema override.")] string params_schema = null)
        {
            var paths = new BakePaths();
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

        [McpServerTool(Name = "dwg_dismiss_bake_suggestion"), Description(
            "Dismiss a ToolBaker suggestion. action must be snooze_30d, never, or never_with_gap_signal.")]
        public static Task<string> DismissBakeSuggestion(string id, string action)
        {
            var paths = new BakePaths();
            using var db = new BakeDb(paths);
            db.Migrate();
            return Task.FromResult(DismissBakeSuggestionHandler.Handle(db, id, action));
        }
    }
}
