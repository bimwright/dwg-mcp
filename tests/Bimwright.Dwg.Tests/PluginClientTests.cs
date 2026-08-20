using System.Threading.Tasks;
using Bimwright.Dwg.Server;
using Newtonsoft.Json;
using Xunit;
using System;
using System.Diagnostics;
using System.IO;

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
        public async Task Sends_request_using_json_discovery_auth_token()
        {
            using var temp = new TempDiscoveryRoot();
            using var fake = new FakePluginServer(line =>
            {
                var req = JsonConvert.DeserializeObject<McpRequest>(line);
                Assert.Equal("json-token", req.Auth);
                return JsonConvert.SerializeObject(new McpResponse { Id = req.Id, Ok = true, Result = "ok" });
            });
            temp.WriteJson("2024", fake.Port, "json-token");

            var client = new PluginClient(() => AuthToken.Resolve("2024", temp.Root));
            var response = await client.SendAsync("ping", new { });

            Assert.True(response.Ok);
            Assert.Equal("ok", (string)response.Result);
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
        public async Task Sends_request_over_named_pipe_transport()
        {
            // Regression test: SendStreamAsync used to wrap the transport
            // stream in a StreamWriter and a StreamReader without leaveOpen,
            // so both disposed the same NamedPipeClientStream. The second
            // Dispose threw ObjectDisposedException ("Cannot access a closed
            // pipe"), which SendAsync's catch turned into a false failure even
            // though the plugin had already answered successfully.
            using var fake = new FakeNamedPipeServer(line =>
            {
                var req = JsonConvert.DeserializeObject<McpRequest>(line);
                return JsonConvert.SerializeObject(new McpResponse { Id = req.Id, Ok = true, Result = "pong" });
            });

            var client = new PluginClient(() => new DiscoveryInfo
            {
                Transport = "pipe",
                PipeName = fake.PipeName,
                Token = "test"
            });

            var response = await client.SendAsync("ping", new { });

            Assert.True(response.Ok);
            Assert.Equal("pong", (string)response.Result);
        }

        [Fact]
        public async Task Sends_multiple_sequential_requests_over_named_pipe_transport()
        {
            using var fake = new FakeNamedPipeServer(line =>
            {
                var req = JsonConvert.DeserializeObject<McpRequest>(line);
                return JsonConvert.SerializeObject(new McpResponse { Id = req.Id, Ok = true, Result = "pong" });
            });

            var client = new PluginClient(() => new DiscoveryInfo
            {
                Transport = "pipe",
                PipeName = fake.PipeName,
                Token = "test"
            });

            for (var i = 0; i < 3; i++)
            {
                var response = await client.SendAsync("ping", new { }, "req-" + i);
                Assert.True(response.Ok, response.Error);
                Assert.Equal("pong", (string)response.Result);
            }
        }

        [Fact]
        public async Task Returns_error_response_when_plugin_unreachable()
        {
            var client = new PluginClient(() => new DiscoveryInfo { Port = 1, Token = "test" });
            var response = await client.SendAsync("ping", new { });

            Assert.False(response.Ok);
            Assert.Contains("plugin", response.Error, System.StringComparison.OrdinalIgnoreCase);
        }

        private sealed class TempDiscoveryRoot : IDisposable
        {
            public string Root { get; } = Path.Combine(Path.GetTempPath(), "dwg-plugin-client-" + Guid.NewGuid().ToString("N"));

            public TempDiscoveryRoot()
            {
                Directory.CreateDirectory(Path.Combine(Root, "Dwg"));
            }

            public void WriteJson(string target, int port, string token)
            {
                var info = new DiscoveryInfo
                {
                    SchemaVersion = 2,
                    Target = target,
                    Version = target,
                    Transport = "tcp",
                    Host = "127.0.0.1",
                    Port = port,
                    Token = token,
                    Pid = Process.GetCurrentProcess().Id
                };
                File.WriteAllText(
                    Path.Combine(Root, "Dwg", "acad-" + target + ".json"),
                    JsonConvert.SerializeObject(info));
            }

            public void Dispose()
            {
                try { Directory.Delete(Root, recursive: true); } catch { }
            }
        }
    }
}
