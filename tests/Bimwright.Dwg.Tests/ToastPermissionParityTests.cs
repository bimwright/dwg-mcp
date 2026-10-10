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
        public void Toast_and_History_read_write_kind_matches_each_tools_permission()
        {
            var tools = typeof(Program).Assembly.GetTypes()
                .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() != null)
                .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                .Select(method => method.GetCustomAttribute<McpServerToolAttribute>())
                .Where(attribute => attribute != null).ToArray();
            Assert.NotEmpty(tools);
            foreach (var tool in tools)
                Assert.True(tool.ReadOnly == (ToolActivityClassifier.Classify(tool.Name) == ToolActivityKind.Read),
                    tool.Name + " has inconsistent Read/Write permission and toast/History classification.");
        }
    }
}
