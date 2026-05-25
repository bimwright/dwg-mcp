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
                typeof(CodeTools)
            }
            .SelectMany(GetMcpToolNames)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

            Assert.Equal(new[]
            {
                "dwg_apply_unicode_style",
                "dwg_collapse_and_rewrite",
                "dwg_get_current_target",
                "dwg_get_selected_texts",
                "dwg_list_available_targets",
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
