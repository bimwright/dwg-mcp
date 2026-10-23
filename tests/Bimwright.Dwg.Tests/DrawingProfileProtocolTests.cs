using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bimwright.Dwg.Server;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests;

/// <summary>Real gateway and SDK wire; the isolated TCP endpoint is a simulated CAD host.</summary>
public class DrawingProfileProtocolTests
{
    [Theory]
    [InlineData(false, false, 16384)]
    [InlineData(true, false, 16384)]
    [InlineData(true, true, 4096)]
    public async Task Own_byte_cap_and_content_continuation_survive_real_SDK_and_argument_warnings(bool readOnly, bool warn, int cap)
    {
        await using var h = await Gateway.StartAsync(readOnly, warn);
        var args = new JObject { ["max_output_bytes"] = cap };
        for (var i = 0; i < 32; i++) args["unknown_" + i + new string('x', 55)] = "private-value";
        var reply = await h.CallAsync("dwg_get_drawing_profile", args);
        Assert.False(reply["result"].Value<bool>("isError"));
        Assert.True(Encoding.UTF8.GetByteCount(h.LastRawResult) <= cap);
        var typed = System.Text.Json.JsonSerializer.Deserialize<CallToolResult>(h.LastRawResult, McpJsonUtilities.DefaultOptions);
        Assert.Equal(Encoding.UTF8.GetByteCount(h.LastRawResult), ResponseBudget.Measure(typed));
        var body = Body(reply)["result"];
        Assert.Equal(50000, body.Value<long>("exact_total_count"));
        Assert.False(body["scope"].Value<bool>("block_expansion"));
        Assert.True(body["coverage"].Value<bool>("complete"));
        Assert.NotEmpty(body["output"]["next_calls"]);
        Assert.NotEmpty(body["output"].Value<string>("note"));
        Assert.Contains("omitted", body["output"].ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-value", reply.ToString());
        Assert.Single(h.Requests.Where(x => x.Value<string>("cmd") == "get_drawing_profile"));
    }

    [Fact]
    public async Task Invalid_profile_budget_is_refused_before_host_dispatch()
    {
        await using var h = await Gateway.StartAsync();
        var reply = await h.CallAsync("dwg_get_drawing_profile", new JObject { ["max_output_bytes"] = 1024 });
        Assert.True(reply["result"].Value<bool>("isError"));
        Assert.False(Body(reply).Value<bool>("ok"));
        Assert.Empty(h.Requests);
    }

    [Fact]
    public async Task Group_continuation_is_forwarded_once_with_exact_snapshot()
    {
        await using var h = await Gateway.StartAsync(readOnly: true);
        var reply = await h.CallAsync("dwg_get_drawing_profile", new JObject { ["section"] = "layers", ["offset"] = 5, ["expected_snapshot"] = "fixture:12" });
        Assert.False(reply["result"].Value<bool>("isError"));
        var call = Assert.Single(h.Requests.Where(x => x.Value<string>("cmd") == "get_drawing_profile"));
        Assert.Equal(5, call["params"].Value<int>("offset"));
        Assert.Equal("fixture:12", call["params"].Value<string>("expected_snapshot"));
    }

    private static JObject Body(JObject reply) => JObject.Parse(reply["result"]["content"].OfType<JObject>()
        .First(x => x.Value<string>("type") == "text").Value<string>("text"));

    internal static JObject FixtureProfile()
    {
        var tables = new JObject();
        foreach (var section in new[] { "types", "layers", "spaces" })
        {
            var rows = new JArray();
            for (var i = 0; i < 128; i++) rows.Add(new JObject { ["name"] = section + i.ToString("D4") + new string('界', 60) + "\"+<>&", ["count"] = 10 });
            tables[section] = new JObject { ["offset"] = 0, ["total_groups"] = 5000, ["counts_complete"] = true, ["rows"] = rows };
        }
        return new JObject
        {
            ["profile_version"] = 1, ["document_id"] = "fixture-document", ["snapshot"] = "fixture:12",
            ["source"] = new JObject { ["api"] = "AutoCAD .NET", ["counting"] = "full_enumeration", ["sampling"] = false, ["estimated"] = false, ["type_basis"] = "managed_runtime_type_full_name" },
            ["scope"] = new JObject { ["spaces"] = "model_and_layout_top_level", ["block_expansion"] = false, ["xref_traversal"] = false, ["erased"] = "excluded" },
            ["coverage"] = new JObject { ["complete"] = true, ["entities_read"] = 50000 },
            ["exact_total_count"] = 50000, ["observed_entity_count"] = 50000,
            ["histograms"] = tables, ["request"] = new JObject { ["section"] = "summary", ["offset"] = 0, ["max_output_bytes"] = 16384 }
        };
    }

    private sealed class Gateway : IAsyncDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "dwg-profile-gateway-" + Guid.NewGuid().ToString("N"));
        private Process process;
        private FakePluginServer host;
        private Task<string> stderr;
        private int id = 10;
        internal string LastRawResult;
        internal ConcurrentQueue<JObject> Requests { get; } = new();

        internal static async Task<Gateway> StartAsync(bool readOnly = false, bool warn = false)
        {
            var gateway = new Gateway();
            try { await gateway.Initialize(readOnly, warn); return gateway; }
            catch { await gateway.DisposeAsync(); throw; }
        }

        private async Task Initialize(bool readOnly, bool warn)
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
                } : FixtureProfile();
                if (request.Value<string>("cmd") == "get_drawing_profile")
                {
                    var args = request["params"] as JObject ?? new JObject();
                    var section = args.Value<string>("section") ?? "summary";
                    result["request"] = new JObject { ["section"] = section, ["offset"] = args.Value<int?>("offset") ?? 0, ["max_output_bytes"] = args.Value<int?>("max_output_bytes") ?? 16384 };
                    foreach (var key in new[] { "types", "layers", "spaces" })
                    {
                        result["histograms"][key]["offset"] = key == section ? args.Value<int?>("offset") ?? 0 : 0;
                        if (section != "summary" && section != key) result["histograms"][key]["rows"] = new JArray();
                    }
                }
                return new JObject { ["id"] = request["id"], ["ok"] = true, ["result"] = result }.ToString(Formatting.None);
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
            if (readOnly) start.ArgumentList.Add("--read-only");
            if (warn) foreach (var value in new[] { "--response-warn-bytes", "1", "--response-strong-warn-bytes", "1" }) start.ArgumentList.Add(value);
            start.Environment["LOCALAPPDATA"] = root;
            process = Process.Start(start);
            stderr = process.StandardError.ReadToEndAsync();
            await Request("initialize", new JObject
            {
                ["protocolVersion"] = "2025-06-18", ["capabilities"] = new JObject(),
                ["clientInfo"] = new JObject { ["name"] = "dwg-profile-tests", ["version"] = "1" }
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
                if (reply.Value<int?>("id") == requestId)
                {
                    using var document = System.Text.Json.JsonDocument.Parse(line);
                    LastRawResult = document.RootElement.TryGetProperty("result", out var result) ? result.GetRawText() : null;
                    return reply;
                }
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
