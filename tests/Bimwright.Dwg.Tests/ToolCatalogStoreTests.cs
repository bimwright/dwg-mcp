using Bimwright.Dwg.Plugin.ToolCatalog;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class ToolCatalogStoreTests
    {
        public ToolCatalogStoreTests()
        {
            ToolCatalogStore.Clear();
        }

        [Fact]
        public void Accepts_a_tool_array_and_sorts_by_name()
        {
            var error = ToolCatalogStore.AcceptJson(JObject.Parse(@"{
                ""tools"": [
                    { ""name"": ""dwg_zoom_extents"", ""description"": ""Zoom"", ""source"": ""built-in"", ""timeout_seconds"": 30 },
                    { ""name"": ""dwg_capture_view_image"", ""description"": ""Capture"", ""source"": ""built-in"", ""timeout_seconds"": 30 }
                ]
            }"));

            Assert.Null(error);
            Assert.Equal(ToolCatalogStatus.Current, ToolCatalogStore.Status);
            var tools = ToolCatalogStore.Snapshot();
            Assert.Equal("dwg_capture_view_image", tools[0].Name);
            Assert.Equal("dwg_zoom_extents", tools[1].Name);
            Assert.Equal(30, tools[0].TimeoutSeconds);
        }

        [Fact]
        public void Missing_tools_array_is_invalid_and_clears_the_previous_list()
        {
            ToolCatalogStore.AcceptJson(JObject.Parse(@"{ ""tools"": [ { ""name"": ""dwg_a"", ""source"": ""built-in"" } ] }"));
            var error = ToolCatalogStore.AcceptJson(JObject.Parse("{}"));

            Assert.False(string.IsNullOrEmpty(error));
            Assert.Equal(ToolCatalogStatus.Invalid, ToolCatalogStore.Status);
            Assert.Empty(ToolCatalogStore.Snapshot());
        }

        [Fact]
        public void Non_object_items_are_skipped_not_thrown()
        {
            var error = ToolCatalogStore.AcceptJson(JObject.Parse(@"{
                ""tools"": [ 5, ""x"", { ""name"": ""dwg_a"", ""source"": ""built-in"" } ]
            }"));

            Assert.Null(error);
            Assert.Equal(ToolCatalogStatus.Current, ToolCatalogStore.Status);
            Assert.Single(ToolCatalogStore.Snapshot());
        }

        [Fact]
        public void Clear_returns_to_empty()
        {
            ToolCatalogStore.AcceptJson(JObject.Parse(@"{ ""tools"": [ { ""name"": ""dwg_a"" } ] }"));
            ToolCatalogStore.Clear();
            Assert.Equal(ToolCatalogStatus.Empty, ToolCatalogStore.Status);
            Assert.Empty(ToolCatalogStore.Snapshot());
        }
    }
}
