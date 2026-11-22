using System.Threading.Tasks;
using Bimwright.Dwg.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;

namespace Bimwright.Dwg.Tests
{
    [CollectionDefinition("Gateway client name", DisableParallelization = true)]
    public sealed class GatewayClientNameCollection { }

    [Collection("Gateway client name")]
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
                McpResponse response = null;
                for (var attempt = 0; attempt < 20; attempt++)
                {
                    response = await client.SendAsync("ping", new { }, "req-" + i);
                    if (response.Ok)
                        break;
                    await Task.Delay(30);
                }

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

        [Fact]
        // FC-TST-03
        public async Task Sends_client_name_on_wire_when_present_and_omits_when_blank()
        {
            var previous = PluginClient.ClientName;
            try
            {
                JObject captured = null;
                using var fake = new FakePluginServer(line =>
                {
                    captured = JObject.Parse(line);
                    return JsonConvert.SerializeObject(new McpResponse { Id = captured.Value<string>("id"), Ok = true, Result = "ok" });
                });

                var client = new PluginClient(() => new DiscoveryInfo { Port = fake.Port, Token = "test" });

                PluginClient.ClientName = "claude-code";
                Assert.True((await client.SendAsync("ping", new { })).Ok);
                Assert.Equal("claude-code", captured.Value<string>("client_name"));

                PluginClient.ClientName = null;
                Assert.True((await client.SendAsync("ping", new { })).Ok);
                Assert.Null(captured["client_name"]);

                PluginClient.ClientName = "   ";
                Assert.True((await client.SendAsync("ping", new { })).Ok);
                Assert.Null(captured["client_name"]);
            }
            finally
            {
                PluginClient.ClientName = previous;
            }
        }

        [Fact]
        // FC-TST-03
        public async Task MCP_filter_records_the_client_name_sent_at_initialize_and_forwards_it()
        {
            var previous = PluginClient.ClientName;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var pipe = "dwg-client-name-" + Guid.NewGuid().ToString("N");
            using var input = new NamedPipeServerStream(pipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            using var output = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
            await Task.WhenAll(input.WaitForConnectionAsync(deadline.Token), output.ConnectAsync(deadline.Token));

            JObject wireRequest = null;
            using var fake = new FakePluginServer(line =>
            {
                wireRequest = JObject.Parse(line);
                return JsonConvert.SerializeObject(new McpResponse { Id = wireRequest.Value<string>("id"), Ok = true, Result = "ok" });
            });

            try
            {
                PluginClient.ClientName = null;
                var fixture = new ClientNameProbeTool(new PluginClient(() => new DiscoveryInfo { Port = fake.Port, Token = "test" }));
                var tool = McpServerTool.Create(typeof(ClientNameProbeTool).GetMethod(nameof(ClientNameProbeTool.Probe)), _ => fixture);
                var builder = Host.CreateApplicationBuilder();
                builder.Logging.ClearProviders();
                var mcp = builder.Services.AddMcpServer().WithStreamServerTransport(input, input).WithTools(new[] { tool });
                mcp = ResponseBudget.Register(mcp, new DwgMcpConfig());
                mcp = ToolArgumentGuard.Register(mcp);
                using var host = builder.Build();
                await host.StartAsync(deadline.Token);

                await using var client = await McpClient.CreateAsync(
                    new StreamClientTransport(output, output),
                    new McpClientOptions { ClientInfo = new Implementation { Name = "claude-code", Version = "2.1.330" } },
                    cancellationToken: deadline.Token);

                Assert.Null(PluginClient.ClientName);
                var response = await client.CallToolAsync("dwg_probe_client_name", cancellationToken: deadline.Token);
                Assert.False(response.IsError == true);
                Assert.Equal("claude-code", PluginClient.ClientName);
                Assert.Equal("claude-code", wireRequest?.Value<string>("client_name"));

                await client.DisposeAsync();
                await host.StopAsync(deadline.Token);
            }
            finally
            {
                PluginClient.ClientName = previous;
            }
        }

        private sealed class ClientNameProbeTool
        {
            private readonly PluginClient _plugin;
            public ClientNameProbeTool(PluginClient plugin) => _plugin = plugin;

            [McpServerTool(Name = "dwg_probe_client_name", ReadOnly = true)]
            public async Task<string> Probe()
            {
                var response = await _plugin.SendAsync("ping", new { });
                return JsonConvert.SerializeObject(response);
            }
        }

        private sealed class TempDiscoveryRoot : IDisposable
        {
            public string Root { get; } = Path.Combine(Path.GetTempPath(), "dwg-plugin-client-" + Guid.NewGuid().ToString("N"));

            public TempDiscoveryRoot()
            {
                Directory.CreateDirectory(Path.Combine(Root, "dwg-mcp"));
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
                    Path.Combine(Root, "dwg-mcp", "acad-" + target + ".json"),
                    JsonConvert.SerializeObject(info));
            }

            public void Dispose()
            {
                try { Directory.Delete(Root, recursive: true); } catch { }
            }
        }
    }
}
