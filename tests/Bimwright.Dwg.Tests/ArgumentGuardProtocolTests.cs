using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bimwright.Dwg.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests;

/// <summary>Actual gateway/stdio/SDK invocation with isolated discovery, logging off and a fake CAD endpoint.</summary>
public class ArgumentGuardProtocolTests
{
    private const string Marker = "argument-value-that-must-not-leak";

    [Theory]
    [InlineData("")]
    [InlineData(",\"arguments\":null")]
    [InlineData(",\"arguments\":{}")]
    [InlineData(",\"arguments\":{\"Handles\":\"argument-value-that-must-not-leak\"}")]
    [InlineData(",\"arguments\":{\"payload\":{\"handles\":\"argument-value-that-must-not-leak\"}}")]
    public async Task Missing_required_is_refused_before_backend_without_echoing_values(string arguments)
    {
        await using var h = await Gateway.StartAsync();
        var reply = await h.CallRawAsync("{\"name\":\"dwg_get_entity_properties\"" + arguments + "}");
        Assert.True(reply["result"].Value<bool>("isError"));
        var body = Body(reply);
        Assert.False(body.Value<bool>("ok"));
        Assert.StartsWith("INVALID_ARGUMENT:", body.Value<string>("error"));
        Assert.Null(body["_target"]);
        Assert.Contains("handles", body["invalid_arguments"]["missing"].Values<string>());
        Assert.DoesNotContain(Marker, reply.ToString());
        Assert.Empty(h.Requests);
        if (arguments.Contains("Handles"))
            Assert.Equal("handles", body["invalid_arguments"]["unknown"][0].Value<string>("did_you_mean"));
        if (arguments.Contains("payload"))
            Assert.Contains("top level", body.Value<string>("suggestion"));
    }

    [Fact]
    public async Task Extra_key_warns_without_changing_arguments_or_running_twice()
    {
        await using var h = await Gateway.StartAsync();
        var reply = await h.CallAsync("dwg_get_entity_properties", new JObject
        {
            ["handles"] = "[\"AB\"]", ["includeGeometry"] = true, ["include_geometry"] = Marker
        });
        var body = Body(reply);
        Assert.True(body.Value<bool>("ok"));
        Assert.Equal("includeGeometry", body["_ignored_arguments"]["unknown"][0].Value<string>("did_you_mean"));
        var dispatched = Assert.Single(h.Requests.Where(x => x.Value<string>("cmd") == "get_entity_properties"));
        Assert.Equal("AB", dispatched["params"]["handles"][0].Value<string>());
        Assert.True(dispatched["params"].Value<bool>("include_geometry"));
        Assert.DoesNotContain(Marker, dispatched.ToString());
        Assert.DoesNotContain(Marker, reply.ToString());
    }

    [Theory]
    [InlineData("{\"Code\":\"argument-value-that-must-not-leak\"}")]
    [InlineData("{\"payload\":{\"code\":\"argument-value-that-must-not-leak\"}}")]
    public async Task Rejected_code_keeps_key_only_diagnostics(string arguments)
    {
        await using var h = await Gateway.StartAsync();
        var reply = await h.CallRawAsync("{\"name\":\"dwg_send_code\",\"arguments\":" + arguments + "}");
        Assert.True(reply["result"].Value<bool>("isError"));
        Assert.StartsWith("INVALID_ARGUMENT:", Body(reply).Value<string>("error"));
        Assert.Null(Body(reply)["_target"]);
        Assert.Empty(h.Requests);
        Assert.DoesNotContain(Marker, reply.ToString());
    }

    [Fact]
    public async Task Optional_only_and_unknown_tools_keep_their_existing_behavior()
    {
        await using var h = await Gateway.StartAsync();
        var reply = await h.CallRawAsync("{\"name\":\"dwg_count_entities\"}");
        Assert.True(Body(reply).Value<bool>("ok"));
        Assert.Null(Body(reply)["_ignored_arguments"]);
        Assert.Single(h.Requests.Where(x => x.Value<string>("cmd") == "count_entities"));
        var unknown = await h.CallAsync("dwg_no_such_tool", new JObject());
        Assert.Equal(-32602, unknown["error"].Value<int>("code"));
    }

    [Fact]
    public async Task Valid_code_with_spoofed_rejection_metadata_warns_and_dispatches_once()
    {
        await using var h = await Gateway.StartAsync();
        Assert.True(Body(await h.CallAsync("dwg_switch_target", new JObject { ["target"] = "pid:" + Environment.ProcessId })).Value<bool>("ok"));
        var reply = await h.CallAsync("dwg_send_code", new JObject
        {
            ["code"] = "return \"" + Marker + "\";", ["rejected"] = true, ["reason"] = "missing_required"
        });
        Assert.True(Body(reply).Value<bool>("ok"));
        Assert.NotNull(Body(reply)["_ignored_arguments"]);
        Assert.Single(h.Requests.Where(x => x.Value<string>("cmd") == "send_code"));
        Assert.DoesNotContain(Marker, reply.ToString());
    }

    [Fact]
    public async Task Host_receipt_survives_actual_SDK_and_cannot_be_spoofed_by_an_argument()
    {
        await using var h = await Gateway.StartAsync();
        var reply = await h.CallAsync("dwg_get_drawing_info", new JObject { ["_target"] = new JObject { ["pid"] = -1 } });
        var body = Body(reply);
        Assert.True(body.Value<bool>("ok"));
        Assert.Equal(Environment.ProcessId, body["_target"].Value<int>("pid"));
        Assert.Equal("argument-guard-document", body["_target"].Value<string>("document_id"));
        var dispatched = Assert.Single(h.Requests.Where(request => request.Value<string>("cmd") == "get_drawing_info"));
        Assert.Null(dispatched["params"]?["_target"]);
    }

    private static JObject Body(JObject reply) => JObject.Parse(reply["result"]["content"].OfType<JObject>()
        .First(x => x.Value<string>("type") == "text").Value<string>("text"));

    private sealed class Gateway : IAsyncDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "dwg-argument-guard-" + Guid.NewGuid().ToString("N"));
        private Process process;
        private FakePluginServer host;
        private Task<string> stderr;
        private int id = 10;
        internal ConcurrentQueue<JObject> Requests { get; } = new();

        internal static async Task<Gateway> StartAsync()
        {
            var gateway = new Gateway();
            try { await gateway.Initialize(); return gateway; }
            catch { await gateway.DisposeAsync(); throw; }
        }

        private async Task Initialize()
        {
            using var owner = Process.GetCurrentProcess();
            var ownerId = owner.Id;
            var startTime = owner.StartTime.ToUniversalTime();
            host = new FakePluginServer(line =>
            {
                var request = JObject.Parse(line);
                Requests.Enqueue(request);
                var result = request.Value<string>("cmd") == "health" ? new JObject
                {
                    ["ok"] = true, ["target"] = new JObject
                    {
                        ["protocol"] = 1, ["pid"] = ownerId, ["process_start_utc"] = startTime,
                        ["document_id"] = "argument-guard-document"
                    }
                } : new JObject { ["ok"] = true };
                return new JObject { ["id"] = request["id"], ["ok"] = true, ["result"] = result,
                    ["_target"] = new JObject { ["protocol"] = 1, ["pid"] = ownerId, ["process_start_utc"] = startTime,
                        ["acad_year"] = 2024, ["target_id"] = "acad-2024-" + ownerId, ["document_id"] = "argument-guard-document" }
                }.ToString(Formatting.None);
            });
            var profile = Path.Combine(root, "Bimwright", "dwg-mcp");
            Directory.CreateDirectory(profile);
            File.WriteAllText(Path.Combine(profile, "acad-2024-" + owner.Id + ".json"), JsonConvert.SerializeObject(new DiscoveryInfo
            {
                Target = "2024", TargetId = "acad-2024-" + owner.Id, Pid = owner.Id, ProcessStartUtc = startTime,
                Port = host.Port, Token = "fixture"
            }));
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8
            };
            start.ArgumentList.Add(typeof(Program).Assembly.Location);
            foreach (var argument in new[] { "--config", Path.Combine(root, "absent.json"), "--toolsets", "all", "--target", "pid:" + owner.Id,
                                             "--disable-call-log" }) start.ArgumentList.Add(argument);
            foreach (var key in start.Environment.Keys.Where(x => x.StartsWith("BIMWRIGHT_DWG_", StringComparison.OrdinalIgnoreCase)).ToArray())
                start.Environment.Remove(key);
            start.Environment["LOCALAPPDATA"] = root;
            process = Process.Start(start);
            stderr = process.StandardError.ReadToEndAsync();
            await Request("initialize", new JObject
            {
                ["protocolVersion"] = "2025-06-18", ["capabilities"] = new JObject(),
                ["clientInfo"] = new JObject { ["name"] = "dwg-argument-tests", ["version"] = "1" }
            });
            await process.StandardInput.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
            await process.StandardInput.FlushAsync();
            var listed = await Request("tools/list", new JObject());
            var runtime = listed["result"]["tools"].OfType<JObject>()
                .Single(x => x.Value<string>("name") == "dwg_health")["_meta"]["bimwright/dwg-mcp/runtime"];
            Assert.Equal(process.Id, runtime.Value<int>("process_id"));
            Assert.Equal(process.StartTime.ToUniversalTime(), runtime.Value<DateTime>("process_started_utc"));
            Assert.Equal(typeof(Program).Assembly.ManifestModule.ModuleVersionId, Guid.Parse(runtime.Value<string>("module_version_id")));
            Assert.Equal(Program.ServerInformationalVersion, runtime.Value<string>("server_informational_version"));
        }

        internal Task<JObject> CallAsync(string tool, JObject arguments)
            => Request("tools/call", new JObject { ["name"] = tool, ["arguments"] = arguments });

        internal Task<JObject> CallRawAsync(string parameters)
            => Request("tools/call", JObject.Parse(parameters));

        private async Task<JObject> Request(string method, JObject parameters)
        {
            var requestId = id++;
            await process.StandardInput.WriteLineAsync(new JObject
            {
                ["jsonrpc"] = "2.0", ["id"] = requestId, ["method"] = method, ["params"] = parameters
            }.ToString(Formatting.None));
            await process.StandardInput.FlushAsync();
            while (true)
            {
                var line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15));
                Assert.NotNull(line);
                var reply = JObject.Parse(line);
                if (reply.Value<int?>("id") == requestId) return reply;
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (process != null)
                {
                    process.StandardInput.Close();
                    try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
                    catch (TimeoutException) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
                    if (stderr != null) await stderr;
                    process.Dispose();
                }
            }
            finally
            {
                host?.Dispose();
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
        }
    }
}
