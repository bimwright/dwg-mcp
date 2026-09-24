using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Bimwright.Dwg.Server;
using Bimwright.Dwg.Server.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class ViewImageResponseTests
    {
        // A real 1x1 PNG: no host or image libraries needed.
        private static readonly byte[] Png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a6xkAAAAASUVORK5CYII=");

        private static JObject Response(string path) => new JObject
        {
            ["ok"] = true,
            ["result"] = new JObject
            {
                ["capture_id"] = "capture1", ["output_path"] = path,
                ["sha256"] = Convert.ToHexString(SHA256.HashData(Png)).ToLowerInvariant()
            }
        };

        [Fact]
        public void Capture_delivers_exact_hashed_bytes_as_MCP_image_and_preserves_JSON()
        {
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
            try
            {
                File.WriteAllBytes(path, Png);
                var result = ViewImageResponse.FromJson(Response(path).ToString());
                Assert.False(result.IsError);
                var image = Assert.Single(result.Content.OfType<ImageContentBlock>());
                Assert.Equal("image/png", image.MimeType);
                Assert.Equal(Png, image.DecodedData.ToArray());
                var text = JObject.Parse(Assert.Single(result.Content.OfType<TextContentBlock>()).Text);
                Assert.True((bool)text["ok"]);
                Assert.Equal(path, (string)text["result"]["output_path"]);
                Assert.True((bool)text["image_delivery"]["ok"]);
            }
            finally { File.Delete(path); }
        }

        [Theory]
        [InlineData("tamper")]
        [InlineData("missing")]
        [InlineData("oversize")]
        public void Delivery_failures_never_attach_wrong_bytes_and_keep_successful_capture_metadata(string mode)
        {
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
            try
            {
                if (mode == "tamper") File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
                if (mode == "oversize")
                {
                    using var stream = File.Create(path);
                    stream.SetLength(ViewImageResponse.MaxImageBytes + 1L);
                }
                var result = ViewImageResponse.FromJson(Response(path).ToString());
                Assert.True(result.IsError);
                Assert.Empty(result.Content.OfType<ImageContentBlock>());
                var text = JObject.Parse(Assert.Single(result.Content.OfType<TextContentBlock>()).Text);
                Assert.True((bool)text["ok"]);
                Assert.False((bool)text["image_delivery"]["ok"]);
                Assert.Equal("capture1", (string)text["result"]["capture_id"]);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Plugin_failure_passes_through_as_MCP_error_without_file_access()
        {
            var result = ViewImageResponse.FromJson("{'ok':false,'error':'stale capture'}");
            Assert.True(result.IsError);
            Assert.Single(result.Content);
            Assert.Contains("stale capture", ((TextContentBlock)result.Content[0]).Text);
        }

        [Fact]
        public void Actual_MCP_region_schema_requires_four_numbers_not_recursive_JToken_arrays()
        {
            var tool = McpServerTool.Create(typeof(ViewTools).GetMethod(nameof(ViewTools.InspectViewRegion)), (object)null);
            var schema = tool.ProtocolTool.InputSchema.GetProperty("properties").GetProperty("region");
            Assert.Equal("object", schema.GetProperty("type").GetString());
            var fields = schema.GetProperty("properties");
            foreach (string name in new[] { "left", "top", "right", "bottom" })
            {
                Assert.Equal("number", fields.GetProperty(name).GetProperty("type").GetString());
                Assert.Contains(schema.GetProperty("required").EnumerateArray(), value => value.GetString() == name);
            }
        }

        [Fact]
        public void Region_DTO_binding_rejects_missing_coordinate_instead_of_defaulting_to_zero()
        {
            Assert.Throws<System.Text.Json.JsonException>(() => System.Text.Json.JsonSerializer.Deserialize<ViewImageRegion>(
                "{\"left\":0.1,\"top\":0.2,\"right\":0.9}"));
            var region = System.Text.Json.JsonSerializer.Deserialize<ViewImageRegion>(
                "{\"left\":0.1,\"top\":0.2,\"right\":0.9,\"bottom\":0.8}");
            Assert.Equal(0.8, region.Bottom);
        }
    }
}
