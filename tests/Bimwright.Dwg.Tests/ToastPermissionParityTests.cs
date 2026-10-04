using System.Linq;
using System.Reflection;
using Bimwright.Dwg.Plugin.Views.Toast;
using Bimwright.Dwg.Server;
using ModelContextProtocol.Server;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public sealed class ToastPermissionParityTests
    {
        [Fact]
        public void Capture_file_tools_have_matching_toast_and_History_write_permissions()
        {
            var tools = typeof(Program).Assembly.GetTypes()
                .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() != null)
                .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
                .Select(method => method.GetCustomAttribute<McpServerToolAttribute>())
                .Where(attribute => attribute != null &&
                    (attribute.Name == "dwg_capture_view_image" || attribute.Name == "dwg_inspect_view_region"
                        || attribute.Name == "dwg_restore_view")).ToArray();
            Assert.Equal(3, tools.Length);
            foreach (var tool in tools)
                Assert.True(tool.ReadOnly == (ToolActivityClassifier.Classify(tool.Name) == ToolActivityKind.Read),
                    tool.Name + " has inconsistent Read/Write permission and toast/History classification.");
        }
    }
}
