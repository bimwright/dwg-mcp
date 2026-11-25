using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Dwg.Server;
using Bimwright.Dwg.Server.Tools;
using Bimwright.Dwg.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    // FC-TGT-07: pure classifier for a client-launched Windows AppContainer.
    public class ClientSandboxClassifierTests
    {
        private const string Normal = @"C:\Users\x\AppData\Local";
        private const string Container = @"C:\Users\x\AppData\Local\Packages\OpenAI.Codex_2p2nqsd0c76g0\AC";

        [Fact]
        public void FC_TGT_07_appcontainer_token_alone_is_detected()
        {
            var result = ClientSandbox.Classify(true, Normal);
            Assert.True(result.Detected);
            Assert.Equal("appcontainer", result.Kind);
            Assert.Equal(new[] { "token_is_appcontainer" }, result.Evidence);
        }

        [Fact]
        public void FC_TGT_07_packages_ac_path_alone_is_detected()
        {
            var result = ClientSandbox.Classify(false, Container);
            Assert.True(result.Detected);
            Assert.Equal("appcontainer", result.Kind);
            Assert.Equal(new[] { "local_app_data_under_packages_ac" }, result.Evidence);
        }

        [Fact]
        public void FC_TGT_07_token_and_path_report_both_evidence_items()
        {
            var result = ClientSandbox.Classify(true, Container + @"\#!sandbox.{0F1E2D3C-0000-0000-0000-000000000000}");
            Assert.True(result.Detected);
            Assert.Equal(new[] { "token_is_appcontainer", "local_app_data_under_packages_ac" }, result.Evidence);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(null)]
        public void FC_TGT_07_neither_signal_or_unknown_token_is_not_detected(bool? token)
        {
            var result = ClientSandbox.Classify(token, Normal);
            Assert.False(result.Detected);
            Assert.Null(result.Kind);
            Assert.Empty(result.Evidence);
        }

        [Fact]
        public void FC_TGT_07_null_token_with_packages_ac_path_is_detected_by_path()
            => Assert.True(ClientSandbox.Classify(null, Container).Detected);

        [Fact]
        public void FC_TGT_07_discovery_root_under_packages_ac_is_detected_when_os_path_is_normal()
        {
            var result = ClientSandbox.Classify(false, Normal, Container + @"\Bimwright");
            Assert.True(result.Detected);
            Assert.Equal(new[] { "local_app_data_under_packages_ac" }, result.Evidence);
        }

        [Theory]
        [InlineData(@"C:\Users\x\AppData\Local\Packages\OpenAI.Codex_2p2nqsd0c76g0\AC\")]
        [InlineData(@"C:\Users\x\AppData\Local\Packages\OpenAI.Codex_2p2nqsd0c76g0\AC\\")]
        [InlineData("C:/Users/x/AppData/Local/Packages/OpenAI.Codex_2p2nqsd0c76g0/AC")]
        [InlineData("C:/Users/x/AppData/Local/Packages/OpenAI.Codex_2p2nqsd0c76g0/AC/")]
        [InlineData(@"c:\users\x\appdata\local\packages\openai.codex_2p2nqsd0c76g0\ac")]
        public void FC_TGT_07_trailing_separators_forward_slashes_and_case_are_tolerated(string path)
            => Assert.True(ClientSandbox.Classify(false, path).Detected);

        [Theory]
        [InlineData(@"C:\Users\x\AppData\Local")]
        [InlineData(@"C:\Users\x\AppData\Local\")]
        [InlineData(@"C:\Users\x\AppData\Local\Packages\Foo\ACX")]
        [InlineData(@"C:\Users\x\AppData\Local\PackagesX\Foo\AC")]
        [InlineData(@"C:\Users\x\AppData\Local\Packages\AC")]
        [InlineData(@"C:\Users\x\AppData\Local\MyPackages\Foo\AC")]
        [InlineData("")]
        [InlineData(null)]
        public void FC_TGT_07_lookalike_or_normal_paths_are_not_detected(string path)
            => Assert.False(ClientSandbox.Classify(false, path).Detected);

        [Fact]
        public void FC_TGT_07_token_probe_reads_this_unsandboxed_test_process_as_not_appcontainer()
        {
            // The test runner is not launched in an AppContainer; null would mean the probe failed.
            if (OperatingSystem.IsWindows()) Assert.False(ClientSandbox.ProbeTokenIsAppContainer());
            else Assert.Null(ClientSandbox.ProbeTokenIsAppContainer());
        }
    }

    // FC-TGT-07: shell cases through the real server methods with the sandbox seam.
    [Collection("Script handlers")]
    public class ClientSandboxServerTests
    {
        private static readonly ClientSandboxResult Sandboxed = ClientSandbox.Classify(true, null);

        private static JObject Gateway() => new JObject
        {
            ["assembly_informational_version"] = "2.1.0+development",
            ["module_version_id"] = "3d72c7b7-7366-4990-a5f6-9a7a11a844df"
        };

        private static JObject Data(CallToolResult reply)
            => JObject.Parse(Assert.Single(reply.Content.OfType<TextContentBlock>()).Text);

        private static async Task InScope(bool? sandboxed, Func<string, Task> body, int? livePort = null)
        {
            var root = Path.Combine(Path.GetTempPath(), "dwg-sandbox-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            if (livePort != null)
            {
                Directory.CreateDirectory(Path.Combine(root, "dwg-mcp"));
                using var current = Process.GetCurrentProcess();
                File.WriteAllText(Path.Combine(root, "dwg-mcp", "acad-2024.json"), JsonConvert.SerializeObject(new DiscoveryInfo
                {
                    Target = "2024", Pid = current.Id, Port = livePort, Token = "test"
                }));
            }
            var previousConfig = ServerState.Config;
            AuthToken.RootOverride.Value = root;
            ClientSandbox.Override.Value = sandboxed == null ? null : sandboxed.Value ? Sandboxed : ClientSandboxResult.None;
            try
            {
                ServerState.Config = new DwgMcpConfig();
                await body(root);
            }
            finally
            {
                ServerState.Config = previousConfig;
                AuthToken.RootOverride.Value = null;
                ClientSandbox.Override.Value = null;
                try { Directory.Delete(root, true); } catch { }
            }
        }

        private static void AssertSandboxBlock(JToken sandbox, string root)
        {
            Assert.NotNull(sandbox);
            Assert.True(sandbox.Value<bool>("detected"));
            Assert.Equal("appcontainer", sandbox.Value<string>("kind"));
            Assert.Equal(new[] { "token_is_appcontainer" }, sandbox["evidence"].Values<string>());
            Assert.Equal(Path.Combine(root, "dwg-mcp"), sandbox.Value<string>("data_dir"));
        }

        [Fact]
        public Task FC_TGT_07_list_targets_when_sandboxed_with_no_targets_keeps_success_shape_and_adds_guidance()
            => InScope(true, async root =>
            {
                var reply = JObject.Parse(await MetaTools.ListAvailableTargets());
                Assert.True(reply.Value<bool>("ok"));
                Assert.Empty((JArray)reply["result"]["targets"]);
                AssertSandboxBlock(reply["result"]["sandbox"], root);
                Assert.Equal(ClientSandbox.Message, reply["result"].Value<string>("note"));
                Assert.StartsWith("CLIENT_SANDBOXED: dwg-mcp is running inside your MCP client's sandbox", ClientSandbox.Message);
            });

        [Fact]
        public Task FC_TGT_07_target_tools_when_sandboxed_with_no_targets_refuse_with_client_sandboxed_and_send_nothing()
            => InScope(true, async _ =>
            {
                // McpException is the envelope the SDK returns as isError=true (as for TARGET_* refusals).
                // Discovery found nothing, so no transport exists; the refusal is raised before any send.
                var read = await Capture.Unstubbed(() => Assert.ThrowsAsync<McpException>(() => MetaTools.ListOpenDocuments()));
                Assert.Equal(ClientSandbox.Message, read.Message);
                var write = await Capture.Unstubbed(() => Assert.ThrowsAsync<McpException>(() =>
                    ModifyTools.CreateLine("{\"x\":0,\"y\":0,\"z\":0}", "{\"x\":1,\"y\":1,\"z\":0}")));
                Assert.Equal(ClientSandbox.Message, write.Message);
            });

        [Fact]
        public Task FC_TGT_07_switch_target_when_sandboxed_with_no_targets_refuses_with_client_sandboxed()
            => InScope(true, async _ =>
            {
                var reply = JObject.Parse(await Capture.Unstubbed(() => MetaTools.SwitchTarget("pid:12345")));
                Assert.False(reply.Value<bool>("ok"));
                Assert.Equal(ClientSandbox.Message, reply.Value<string>("error"));
            });

        [Fact]
        public Task FC_TGT_07_health_when_sandboxed_with_no_targets_reports_client_sandboxed_and_sandbox_block()
            => InScope(true, async root =>
            {
                using var http = new HttpClient(new ReleaseUpdateCheckTests.Handler());
                var reply = await new HealthTools(new PluginClient(() => AuthToken.Resolve()), new ReleaseUpdateCheck(http), Gateway()).Health();
                Assert.True(reply.IsError);
                var health = Data(reply);
                Assert.False(health.Value<bool>("host_connected"));
                Assert.Equal("CLIENT_SANDBOXED", health["error"].Value<string>("code"));
                Assert.Equal(ClientSandbox.Message, health["error"].Value<string>("message"));
                AssertSandboxBlock(health["sandbox"], root);
            });

        [Fact]
        public async Task FC_TGT_07_sandboxed_with_a_live_target_keeps_normal_behaviour_except_health_sandbox_block()
        {
            var commands = new System.Collections.Concurrent.ConcurrentQueue<string>();
            using var fake = new FakePluginServer(line =>
            {
                var request = JObject.Parse(line);
                commands.Enqueue(request.Value<string>("cmd"));
                return new JObject
                {
                    ["id"] = request["id"], ["ok"] = true,
                    ["result"] = new JObject { ["ok"] = true, ["documents"] = new JArray() }
                }.ToString(Formatting.None);
            });
            await InScope(true, async root =>
            {
                var list = JObject.Parse(await MetaTools.ListAvailableTargets());
                Assert.True(list.Value<bool>("ok"));
                Assert.Single((JArray)list["result"]["targets"]);
                Assert.Null(list["result"]["sandbox"]);
                Assert.Null(list["result"]["note"]);

                var client = new PluginClient(() => AuthToken.Resolve());
                var response = await client.SendAsync("list_open_documents", new JObject());
                Assert.True(response.Ok, response.Error);
                Assert.Contains("list_open_documents", commands);

                using var http = new HttpClient(new ReleaseUpdateCheckTests.Handler());
                var health = Data(await new HealthTools(new PluginClient(() => AuthToken.Resolve()), new ReleaseUpdateCheck(http), Gateway()).Health());
                Assert.True(health.Value<bool>("host_connected"));
                Assert.Null(health["error"]);
                AssertSandboxBlock(health["sandbox"], root);
            }, fake.Port);
        }

        [Fact]
        public Task FC_TGT_07_not_sandboxed_keeps_no_target_behaviour_unchanged()
            => InScope(false, async _ =>
            {
                var list = JObject.Parse(await MetaTools.ListAvailableTargets());
                Assert.True(list.Value<bool>("ok"));
                Assert.Empty((JArray)list["result"]["targets"]);
                Assert.Null(list["result"]["sandbox"]);
                Assert.Null(list["result"]["note"]);

                using var http = new HttpClient(new ReleaseUpdateCheckTests.Handler());
                var health = Data(await new HealthTools(new PluginClient(() => AuthToken.Resolve()), new ReleaseUpdateCheck(http), Gateway()).Health());
                Assert.Equal("NO_TARGET", health["error"].Value<string>("code"));
                Assert.Null(health["sandbox"]);

                var tool = await Capture.Unstubbed(() => Assert.ThrowsAsync<InvalidOperationException>(() => MetaTools.ListOpenDocuments()));
                Assert.DoesNotContain("CLIENT_SANDBOXED", tool.Message);
            });

        [Fact]
        public Task FC_TGT_07_protocol_sets_isError_true_for_switch_target_and_change_history_in_sandbox()
            => InScope(true, async root =>
            {
                await Capture.Unstubbed(async () =>
                {
                    var pipe = "dwg-client-sandbox-" + Guid.NewGuid().ToString("N");
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    using var input = new NamedPipeServerStream(pipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    using var output = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
                    await Task.WhenAll(input.WaitForConnectionAsync(timeout.Token), output.ConnectAsync(timeout.Token));

                    var health = new HealthTools(new PluginClient(() => AuthToken.Resolve()),
                        new ReleaseUpdateCheck(new HttpClient(new ReleaseUpdateCheckTests.Handler())), Gateway());
                    var tools = typeof(MetaTools).GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .Concat(typeof(ChangeHistoryTools).GetMethods(BindingFlags.Public | BindingFlags.Static))
                        .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() != null)
                        .Select(m => McpServerTool.Create(m))
                        .Concat(typeof(HealthTools).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() != null)
                            .Select(m => McpServerTool.Create(m, _ => health)))
                        .ToArray();

                    var builder = Host.CreateApplicationBuilder();
                    builder.Logging.ClearProviders();
                    var mcp = builder.Services.AddMcpServer().WithStreamServerTransport(input, input).WithTools(tools);
                    mcp = ResponseBudget.Register(mcp, ServerState.Config);
                    ToolArgumentGuard.Register(mcp, new ServerLogger(Path.Combine(root, "calls.jsonl"), () => false));
                    using var host = builder.Build();
                    await host.StartAsync(timeout.Token);

                    using var reader = new StreamReader(output, new UTF8Encoding(false), false, 1024, leaveOpen: true);
                    using var writer = new StreamWriter(output, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
                    var nextId = 1;
                    async Task<JObject> Call(string method, object parameters)
                    {
                        var id = nextId++;
                        await writer.WriteLineAsync(JsonConvert.SerializeObject(new { jsonrpc = "2.0", id, method, @params = parameters }));
                        while (true)
                        {
                            var reply = JObject.Parse(await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)));
                            if (reply.Value<int?>("id") != id) continue;
                            Assert.Null(reply["error"]);
                            return (JObject)reply["result"];
                        }
                    }

                    try
                    {
                        await Call("initialize", new
                        {
                            protocolVersion = "2025-06-18",
                            capabilities = new { },
                            clientInfo = new { name = "client-sandbox-test", version = "1" }
                        });
                        await writer.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");

                        var listResult = await Call("tools/call", new { name = "dwg_list_available_targets", arguments = new { } });
                        Assert.NotEqual(true, listResult.Value<bool?>("isError"));
                        var listBody = JObject.Parse(listResult["content"][0].Value<string>("text"));
                        Assert.True(listBody.Value<bool>("ok"));
                        AssertSandboxBlock(listBody["result"]["sandbox"], root);

                        var switchResult = await Call("tools/call", new { name = "dwg_switch_target", arguments = new { target = "pid:12345" } });
                        Assert.True(switchResult.Value<bool?>("isError"));
                        var switchBody = JObject.Parse(switchResult["content"][0].Value<string>("text"));
                        Assert.False(switchBody.Value<bool>("ok"));
                        Assert.Equal(ClientSandbox.Message, switchBody.Value<string>("error"));

                        var recordsResult = await Call("tools/call", new { name = "dwg_get_change_records", arguments = new { } });
                        Assert.True(recordsResult.Value<bool?>("isError"));
                        var recordsBody = JObject.Parse(recordsResult["content"][0].Value<string>("text"));
                        Assert.False(recordsBody.Value<bool>("ok"));
                        Assert.Equal(ClientSandbox.Message, recordsBody.Value<string>("error"));

                        var impactResult = await Call("tools/call", new { name = "dwg_survey_change_impact", arguments = new { handles = new[] { "AB" } } });
                        Assert.True(impactResult.Value<bool?>("isError"));
                        var impactBody = JObject.Parse(impactResult["content"][0].Value<string>("text"));
                        Assert.False(impactBody.Value<bool>("ok"));
                        Assert.Equal(ClientSandbox.Message, impactBody.Value<string>("error"));

                        var docsResult = await Call("tools/call", new { name = "dwg_list_open_documents", arguments = new { } });
                        Assert.True(docsResult.Value<bool?>("isError"));
                        Assert.Contains(ClientSandbox.Message, docsResult["content"][0].Value<string>("text"));

                        var healthResult = await Call("tools/call", new { name = "dwg_health", arguments = new { } });
                        Assert.True(healthResult.Value<bool?>("isError"));
                        var healthBody = JObject.Parse(healthResult["content"][0].Value<string>("text"));
                        Assert.Equal("CLIENT_SANDBOXED", healthBody["error"].Value<string>("code"));
                        AssertSandboxBlock(healthBody["sandbox"], root);
                    }
                    finally { await host.StopAsync(timeout.Token); }
                    return true;
                });
            });
    }
}
