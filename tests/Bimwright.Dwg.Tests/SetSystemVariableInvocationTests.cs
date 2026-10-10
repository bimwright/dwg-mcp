using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Bimwright.Dwg.Server.Tools;
using Bimwright.Dwg.Tests.Helpers;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    /// <summary>dwg_set_system_variable invoked through the MCP SDK, with the wire send captured.</summary>
    public class SetSystemVariableInvocationTests
    {
        [Theory]
        [InlineData("ORTHOMODE", "1", JTokenType.Integer)]
        [InlineData("CLAYER", "\"Standard\"", JTokenType.String)]
        [InlineData("LTSCALE", "0.5", JTokenType.Float)]
        public async Task Scalar_value_binds_and_reaches_the_plugin_unchanged(string name, string valueJson, JTokenType expected)
        {
            var tool = McpServerTool.Create(typeof(DrawingWriteTools).GetMethod(nameof(DrawingWriteTools.SetSystemVariable)));
            await using var server = McpServer.Create(new StreamServerTransport(new MemoryStream(), new MemoryStream()), new McpServerOptions());
            var context = new RequestContext<CallToolRequestParams>(server, new JsonRpcRequest { Method = "tools/call", Id = new RequestId(1) })
            {
                Params = new CallToolRequestParams
                {
                    Name = "dwg_set_system_variable",
                    Arguments = new Dictionary<string, JsonElement>
                    {
                        ["name"] = JsonDocument.Parse(JsonSerializer.Serialize(name)).RootElement.Clone(),
                        ["value"] = JsonDocument.Parse(valueJson).RootElement.Clone()
                    }
                }
            };

            var capture = await Capture.Send(async () =>
            {
                var result = await tool.InvokeAsync(context);
                return Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
            });

            Assert.Equal("set_system_variable", capture.Command);
            var sent = capture.Json();
            Assert.Equal(name, sent.Value<string>("name"));
            Assert.Equal(expected, sent["value"].Type);
            Assert.True(JToken.DeepEquals(JToken.Parse(valueJson), sent["value"]), sent.ToString());
        }
    }
}
