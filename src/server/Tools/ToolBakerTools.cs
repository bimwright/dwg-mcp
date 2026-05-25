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

        [McpServerTool(Name = "dwg_list_bake_suggestions", ReadOnly = true, Idempotent = true), Description(
            "List adaptive ToolBaker suggestions from the server bake database.")]
        public static Task<string> ListBakeSuggestions()
        {
            var paths = new BakePaths();
            using var db = new BakeDb(paths);
            db.Migrate();
            return Task.FromResult(ListBakeSuggestionsHandler.Handle(db));
        }

        [McpServerTool(Name = "dwg_create_bake_issue_draft", ReadOnly = true, Idempotent = true), Description(
            "Create a GitHub issue draft for a ToolBaker suggestion without submitting it.")]
        public static Task<string> CreateBakeIssueDraft(
            [Description("Suggestion id from dwg_list_bake_suggestions.")] string id)
        {
            var paths = new BakePaths();
            using var db = new BakeDb(paths);
            db.Migrate();
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
