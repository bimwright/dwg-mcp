using System.Threading.Tasks;
using Bimwright.Dwg.Server;
using Newtonsoft.Json;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class PluginClientTests
    {
        [Fact]
        public async Task Sends_request_and_receives_unicode_response()
        {
            using var fake = new FakePluginServer(line =>
            {
                var req = JsonConvert.DeserializeObject<McpRequest>(line);
                var resp = new McpResponse { Id = req.Id, Ok = true, Result = "建筑平面图" };
                return JsonConvert.SerializeObject(resp);
            });

            var client = new PluginClient(() => new DiscoveryInfo { Port = fake.Port, Token = "test" });
            var response = await client.SendAsync("ping", new { });

            Assert.True(response.Ok);
            Assert.Equal("建筑平面图", (string)response.Result);
        }

        [Fact]
        public async Task Preserves_explicit_request_id()
        {
            using var fake = new FakePluginServer(line =>
            {
                var req = JsonConvert.DeserializeObject<McpRequest>(line);
                Assert.Equal("req-123", req.Id);
                return JsonConvert.SerializeObject(new McpResponse { Id = req.Id, Ok = true, Result = "ok" });
            });

            var client = new PluginClient(() => new DiscoveryInfo { Port = fake.Port, Token = "test" });
            var response = await client.SendAsync("ping", new { }, "req-123");

            Assert.True(response.Ok);
            Assert.Equal("req-123", response.Id);
        }

        [Fact]
        public async Task Returns_error_response_when_plugin_unreachable()
        {
            var client = new PluginClient(() => new DiscoveryInfo { Port = 1, Token = "test" });
            var response = await client.SendAsync("ping", new { });

            Assert.False(response.Ok);
            Assert.Contains("plugin", response.Error, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
