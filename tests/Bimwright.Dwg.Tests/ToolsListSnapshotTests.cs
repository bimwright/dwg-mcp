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
            var names = typeof(QueryTools).Assembly.GetTypes()
                .Where(IsMcpToolType)
                .SelectMany(GetMcpToolNames)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(new[]
            {
                "dwg_accept_bake_suggestion",
                "dwg_apply_unicode_style",
                "dwg_batch_execute",
                "dwg_change_color",
                "dwg_change_layer",
                "dwg_collapse_and_rewrite",
                "dwg_copy_entities",
                "dwg_count_entities",
                "dwg_create_aligned_dimension",
                "dwg_create_arc",
                "dwg_create_bake_issue_draft",
                "dwg_create_circle",
                "dwg_create_diameter_dimension",
                "dwg_create_ellipse",
                "dwg_create_layer",
                "dwg_create_leader",
                "dwg_create_line",
                "dwg_create_linear_dimension",
                "dwg_create_mtext",
                "dwg_create_point",
                "dwg_create_polyline",
                "dwg_create_radial_dimension",
                "dwg_create_rectangle",
                "dwg_create_table",
                "dwg_create_text",
                "dwg_dismiss_bake_suggestion",
                "dwg_erase_entities",
                "dwg_explode_block",
                "dwg_get_block_attributes",
                "dwg_get_current_target",
                "dwg_get_drawing_info",
                "dwg_get_entity_properties",
                "dwg_get_selected_texts",
                "dwg_insert_block",
                "dwg_list_available_targets",
                "dwg_list_bake_suggestions",
                "dwg_list_baked_tools",
                "dwg_list_blocks",
                "dwg_list_layers",
                "dwg_move_entities",
                "dwg_offset_entities",
                "dwg_query_entities",
                "dwg_rotate_entities",
                "dwg_run_baked_tool",
                "dwg_scale_entities",
                "dwg_select_by_layer",
                "dwg_select_by_type",
                "dwg_send_code",
                "dwg_set_block_attributes",
                "dwg_switch_target",
                "dwg_translate_and_rewrite",
                "dwg_update_texts"
            }, names);
            Assert.All(names, name => Assert.StartsWith("dwg_", name, StringComparison.Ordinal));
        }

        [Fact]
        public void ChangeColorWrapperUsesSnakeCaseColorIndexParameter()
        {
            var method = typeof(ModifyTools).GetMethod(nameof(ModifyTools.ChangeColor));

            Assert.NotNull(method);
            Assert.Equal(new[] { "handles", "color_index" }, method.GetParameters().Select(p => p.Name).ToArray());
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

        private static bool IsMcpToolType(Type type)
            => type.GetCustomAttributes().Any(a => a.GetType().Name == "McpServerToolTypeAttribute");
    }
}
