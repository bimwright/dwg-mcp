using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Bimwright.Dwg.Server.Bake;
using Bimwright.Dwg.Server.Handlers;
using ModelContextProtocol.Server;
using Newtonsoft.Json;

namespace Bimwright.Dwg.Server.Tools
{
    [McpServerToolType]
    public class ToolBakerTools
    {
        // Test hook: keep database reads inside a fixture, away from the real profile.
        internal static BakePaths PathsOverride;

        [McpServerTool(Name = "dwg_list_baked_tools", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description(
            "List baked DWG tools accepted into the server registry." +
            " Reads the existing database without creating or migrating files; returns an empty list if no bake database exists.")]
        public static Task<string> ListBakedTools()
        {
            var paths = PathsOverride ?? new BakePaths();
            using var db = new BakeDb(paths);
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

        [McpServerTool(Name = "dwg_list_bake_suggestions", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description(
            "List adaptive ToolBaker suggestions from the server bake database." +
            " Reads the existing database without creating or migrating files; returns an empty list if no bake database exists.")]
        public static Task<string> ListBakeSuggestions()
        {
            var paths = PathsOverride ?? new BakePaths();
            using var db = new BakeDb(paths);
            return Task.FromResult(ListBakeSuggestionsHandler.Handle(db));
        }

        [McpServerTool(Name = "dwg_create_bake_issue_draft", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description(
            "Create a GitHub issue draft for a ToolBaker suggestion without submitting it." +
            " Returns title and body in the response only; does not create a file or contact GitHub, and never creates or migrates a database.")]
        public static Task<string> CreateBakeIssueDraft(
            [Description("Suggestion id from dwg_list_bake_suggestions.")] string id)
        {
            var paths = PathsOverride ?? new BakePaths();
            using var db = new BakeDb(paths);
            var suggestion = db.GetSuggestion(id);
            if (suggestion == null)
            {
                return Task.FromResult(JsonConvert.SerializeObject(new { ok = false, error_code = "not_found", message = "Bake suggestion was not found." }));
            }

            var title = "[ToolBaker] " + (suggestion.Title ?? suggestion.Id);
            var body = string.Join("\n", new[]
            {
                "## Summary",
                suggestion.Description ?? "Repeated DWG workflow detected.",
                "",
                "## Suggestion",
                "- id: `" + suggestion.Id + "`",
                "- source: `" + suggestion.Source + "`",
                "- score: `" + suggestion.Score + "`",
                "",
                "## Payload",
                "```json",
                suggestion.PayloadJson ?? "{}",
                "```"
            });

            return Task.FromResult(JsonConvert.SerializeObject(new
            {
                ok = true,
                issue = new { title, body }
            }));
        }
    }
}
