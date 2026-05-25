using System;
using System.Linq;
using System.Reflection;
using Bimwright.Dwg.Server.Tools;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class ToolsListSnapshotTests
    {
        [Fact]
        public void CurrentBackedMcpToolsUseDwgPrefix()
        {
            var names = new[]
            {
                typeof(QueryTools),
                typeof(ModifyTools),
                typeof(MetaTools),
                typeof(BatchTools),
                typeof(ToolBakerTools),
                typeof(ToolBakerWriteTools),
                typeof(CodeTools)
            }
            .SelectMany(GetMcpToolNames)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

            Assert.Equal(new[]
            {
                "dwg_accept_bake_suggestion",
                "dwg_apply_unicode_style",
                "dwg_batch_execute",
                "dwg_change_layer",
                "dwg_collapse_and_rewrite",
                "dwg_count_entities",
                "dwg_create_bake_issue_draft",
                "dwg_create_circle",
                "dwg_create_layer",
                "dwg_create_line",
                "dwg_dismiss_bake_suggestion",
                "dwg_get_current_target",
                "dwg_get_drawing_info",
                "dwg_get_entity_properties",
                "dwg_get_selected_texts",
                "dwg_list_available_targets",
                "dwg_list_bake_suggestions",
                "dwg_list_baked_tools",
                "dwg_list_layers",
                "dwg_query_entities",
                "dwg_run_baked_tool",
                "dwg_select_by_layer",
                "dwg_select_by_type",
                "dwg_send_code",
                "dwg_switch_target",
                "dwg_translate_and_rewrite",
                "dwg_update_texts"
            }, names);
            Assert.All(names, name => Assert.StartsWith("dwg_", name, StringComparison.Ordinal));
        }

        private static string[] GetMcpToolNames(Type type)
        {
            return type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Select(method => method.GetCustomAttributes().FirstOrDefault(a => a.GetType().Name == "McpServerToolAttribute"))
                .Where(attr => attr != null)
                .Select(attr => (string)attr.GetType().GetProperty("Name")?.GetValue(attr))
                .Select(name => string.IsNullOrWhiteSpace(name) ? throw new InvalidOperationException("MCP tool must set an explicit Name.") : name)
                .ToArray();
        }
    }
}
