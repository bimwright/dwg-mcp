using System.Linq;
using Bimwright.Dwg.Server;
using Newtonsoft.Json.Linq;
using Bimwright.Dwg.Server.Tools;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class ToolCatalogBuilderTests
    {
        [Fact]
        public void Build_reads_mcp_tool_names_and_the_shared_timeout()
        {
            var catalog = ToolCatalogBuilder.Build(new[] { typeof(ViewTools) });
            var names = catalog["tools"].Select(tool => tool.Value<string>("name")).ToArray();

            Assert.Contains("dwg_capture_view_image", names);
            Assert.Contains("dwg_restore_view", names);
            Assert.All(catalog["tools"], tool =>
            {
                Assert.Equal("built-in", tool.Value<string>("source"));
                Assert.Equal(ToolCatalogBuilder.TimeoutSeconds, tool.Value<int>("timeout_seconds"));
                Assert.False(string.IsNullOrWhiteSpace(tool.Value<string>("description")));
            });
        }
    }
}
