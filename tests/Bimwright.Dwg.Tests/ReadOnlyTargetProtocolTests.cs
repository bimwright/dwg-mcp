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

/// <summary>Real gateway/stdio with two live process identities and fake CAD endpoints; no Autodesk host.</summary>
public class ReadOnlyTargetProtocolTests
{
    [Theory]
    [InlineData("pid")]
    [InlineData("id")]
    public async Task Read_only_startup_pins_the_explicit_instance_and_hidden_writes_cannot_change_it(string selectorKind)
    {
        await using var h = await Gateway.StartAsync(selectorKind);
        var targets = Body(await h.CallAsync("dwg_list_available_targets"))["result"]["targets"];
        Assert.Equal(2, targets.Count());
        Assert.All(targets, target => Assert.Equal("2024", target.Value<string>("target")));
        var names = h.Catalog["tools"].Select(tool => tool.Value<string>("name")).ToArray();
        Assert.Equal(23, names.Length);
        Assert.DoesNotContain("dwg_switch_target", names);
        Assert.All(h.Catalog["tools"], tool => Assert.True(tool["annotations"].Value<bool>("readOnlyHint")));
        Assert.Equal("none", Body(await h.CallAsync("dwg_get_current_target"))["result"].Value<string>("state"));
        Assert.Empty(h.Selected.Requests);
        Assert.Empty(h.Other.Requests);

        var health = Body(await h.CallAsync("dwg_health"));
        Assert.True(health.Value<bool>("ok"));
        Assert.True(health.Value<bool>("host_connected"));
        Assert.Equal(h.Selected.Info.Pid, health["target"].Value<int>("pid"));
        Assert.Equal(h.Selected.Info.ProcessStartUtc, health["target"].Value<DateTime>("process_start_utc"));
        Assert.Equal(h.Selected.DocumentId, health["target"].Value<string>("document_id"));
        Assert.False(health["binding"].Value<bool>("selection_confirmed"));
        var drawing = Body(await h.CallAsync("dwg_get_drawing_info"));
        Assert.True(drawing.Value<bool>("ok"));
        Assert.Equal(h.Selected.Info.Pid, drawing["result"].Value<int>("pid"));
        Assert.Single(h.Selected.Requests.Where(request => request.Value<string>("cmd") == "get_drawing_info"));

        var before = Body(await h.CallAsync("dwg_get_current_target"))["result"];
        Assert.Equal(h.Selected.Info.Pid, before.Value<int>("pid"));
        var count = h.Selected.Requests.Count;
        foreach (var blocked in new[] { "dwg_switch_target", "dwg_create_line", "dwg_save_drawing", "dwg_send_code" })
        {
            var reply = await h.CallAsync(blocked, new JObject
            {
                ["target"] = "pid:" + h.Other.Info.Pid, ["verify"] = true, ["rebind"] = true
            });
            Assert.True(reply["error"] != null || reply["result"]?.Value<bool?>("isError") == true);
        }
        Assert.True(JToken.DeepEquals(before, Body(await h.CallAsync("dwg_get_current_target"))["result"]));
        Assert.Equal(count, h.Selected.Requests.Count);
        Assert.Empty(h.Other.Requests);
    }

    [Fact]
    public async Task Read_only_target_loss_keeps_the_pin_and_reports_restart_recovery_without_fallback()
    {
        await using var h = await Gateway.StartAsync("pid");
        Assert.True(Body(await h.CallAsync("dwg_get_drawing_info")).Value<bool>("ok"));
        var before = Body(await h.CallAsync("dwg_get_current_target"))["result"];
        var count = h.Selected.Requests.Count;
        await h.StopSelectedIdentityAsync();

        var available = Body(await h.CallAsync("dwg_list_available_targets"))["result"]["targets"];
        Assert.Equal(h.Other.Info.Pid, Assert.Single(available).Value<int>("pid"));
        var refused = await h.CallAsync("dwg_get_drawing_info");
        Assert.True(refused["result"].Value<bool>("isError"));
        AssertReadOnlyRecovery(refused["result"]["content"][0].Value<string>("text"));
        var health = Body(await h.CallAsync("dwg_health"));
        Assert.False(health.Value<bool>("ok"));
        Assert.False(health.Value<bool>("host_connected"));
        Assert.Equal("TARGET_LOST", health["error"].Value<string>("code"));
        AssertReadOnlyRecovery(health["error"].Value<string>("message"));
        var after = Body(await h.CallAsync("dwg_get_current_target"))["result"];
        Assert.Equal("lost", after.Value<string>("state"));
        Assert.Equal(before["pid"], after["pid"]);
        Assert.Equal(before["generation"], after["generation"]);
        Assert.Equal(count, h.Selected.Requests.Count);
        Assert.Empty(h.Other.Requests);
    }

    [Theory]
    [InlineData("TARGET_LOST")]
    [InlineData("TARGET_CHANGED")]
    public async Task Read_only_host_identity_refusal_keeps_its_code_and_adds_restart_recovery(string code)
    {
        await using var h = await Gateway.StartAsync("pid");
        Assert.True(Body(await h.CallAsync("dwg_health")).Value<bool>("ok"));
        var before = Body(await h.CallAsync("dwg_get_current_target"))["result"];
        h.Selected.Refusal = code + ": host identity refusal; command was not started. Select intended PID again.";

        var drawing = Body(await h.CallAsync("dwg_get_drawing_info"));
        Assert.False(drawing.Value<bool>("ok"));
        Assert.StartsWith(code + ":", drawing.Value<string>("error"));
        AssertReadOnlyRecovery(drawing.Value<string>("error"), code);
        var response = await h.CallAsync("dwg_health");
        Assert.True(response["result"].Value<bool>("isError"));
        var health = Body(response);
        Assert.False(health.Value<bool>("ok"));
        Assert.False(health.Value<bool>("host_connected"));
        Assert.Equal(code, health["error"].Value<string>("code"));
        AssertReadOnlyRecovery(health["error"].Value<string>("message"), code);
        var after = Body(await h.CallAsync("dwg_get_current_target"))["result"];
        Assert.Equal(before["pid"], after["pid"]);
        Assert.Equal(before["generation"], after["generation"]);
        Assert.Single(h.Selected.Requests.Where(request => request.Value<string>("cmd") == "get_drawing_info"));
        Assert.Equal(2, h.Selected.Requests.Count(request => request.Value<string>("cmd") == "health"));
        Assert.Empty(h.Other.Requests);
    }

    private static void AssertReadOnlyRecovery(string message, string code = "TARGET_LOST")
    {
        Assert.Contains(code, message);
        Assert.Contains("dwg_list_available_targets", message);
        Assert.Contains("restart", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--target", message);
        Assert.Contains("pid:", message);
        Assert.DoesNotContain("dwg_switch_target", message);
    }

    private static JObject Body(JObject reply)
        => JObject.Parse(reply["result"]["content"].First(block => block.Value<string>("type") == "text").Value<string>("text"));

    private sealed class Endpoint : IDisposable
    {
        private readonly FakePluginServer server;
        internal ConcurrentQueue<JObject> Requests { get; } = new();
        internal DiscoveryInfo Info { get; }
        internal string DocumentId { get; }
        internal volatile string Refusal;

        internal Endpoint(int pid, DateTime processStart)
        {
            DocumentId = "readonly-document-" + pid;
            server = new FakePluginServer(line =>
            {
                var request = JObject.Parse(line);
                Requests.Enqueue(request);
                var refusal = Refusal;
                if (refusal != null && request.Value<string>("cmd") is "get_drawing_info" or "health")
                    return new JObject { ["id"] = request["id"], ["ok"] = false, ["error"] = refusal }.ToString(Formatting.None);
                var result = request.Value<string>("cmd") == "health" ? new JObject
                {
                    ["ok"] = true, ["target"] = new JObject
                    {
                        ["protocol"] = 1, ["pid"] = pid, ["process_start_utc"] = processStart,
                        ["document_id"] = DocumentId
                    }
                } : new JObject { ["ok"] = true, ["pid"] = pid, ["document_id"] = DocumentId };
                return new JObject { ["id"] = request["id"], ["ok"] = true, ["result"] = result }.ToString(Formatting.None);
            });
            Info = new DiscoveryInfo
            {
                SchemaVersion = 3, AcadYear = 2024, Target = "2024", Pid = pid,
                TargetId = "acad-2024-" + pid, ProcessStartUtc = processStart,
                Port = server.Port, Token = "readonly-fixture-" + pid
            };
        }

        public void Dispose() => server.Dispose();
    }

    private sealed class Gateway : IAsyncDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "dwg-readonly-target-" + Guid.NewGuid().ToString("N"));
        private Process process, selectedIdentity;
        private Task<string> stderr, identityOutput, identityError;
        private int id = 10;
        internal Endpoint Selected { get; private set; }
        internal Endpoint Other { get; private set; }
        internal JObject Catalog { get; private set; }

        internal static async Task<Gateway> StartAsync(string selectorKind)
        {
            var gateway = new Gateway();
            try { await gateway.Initialize(selectorKind); return gateway; }
            catch { await gateway.DisposeAsync(); throw; }
        }

        private async Task Initialize(string selectorKind)
        {
            // An idle gateway is an owned live identity, never an AutoCAD process.
            selectedIdentity = Process.Start(StartInfo(Path.Combine(root, "identity")));
            identityOutput = selectedIdentity.StandardOutput.ReadToEndAsync();
            identityError = selectedIdentity.StandardError.ReadToEndAsync();
            Selected = new Endpoint(selectedIdentity.Id, selectedIdentity.StartTime.ToUniversalTime());
            using var owner = Process.GetCurrentProcess();
            Other = new Endpoint(owner.Id, owner.StartTime.ToUniversalTime());
            var profile = Path.Combine(root, "Bimwright", "dwg-mcp");
            Directory.CreateDirectory(profile);
            foreach (var endpoint in new[] { Selected, Other })
                File.WriteAllText(Path.Combine(profile, endpoint.Info.TargetId + ".json"), JsonConvert.SerializeObject(endpoint.Info));

            var start = StartInfo(root);
            foreach (var argument in new[] { "--read-only", "--toolsets", "all", "--target",
                         selectorKind == "id" ? "id:" + Selected.Info.TargetId : "pid:" + Selected.Info.Pid })
                start.ArgumentList.Add(argument);
            process = Process.Start(start);
            stderr = process.StandardError.ReadToEndAsync();
            var initialized = await Request("initialize", new JObject
            {
                ["protocolVersion"] = "2025-06-18", ["capabilities"] = new JObject(),
                ["clientInfo"] = new JObject { ["name"] = "dwg-readonly-target-tests", ["version"] = "1" }
            });
            Assert.Equal(Program.ServerInformationalVersion, initialized["result"]["serverInfo"].Value<string>("version"));
            await process.StandardInput.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
            await process.StandardInput.FlushAsync();
            Catalog = (JObject)(await Request("tools/list", new JObject()))["result"];
            var runtime = Catalog["tools"].Single(tool => tool.Value<string>("name") == "dwg_health")["_meta"]["bimwright/dwg-mcp/runtime"];
            Assert.Equal(process.Id, runtime.Value<int>("process_id"));
            Assert.Equal(process.StartTime.ToUniversalTime(), runtime.Value<DateTime>("process_started_utc"));
            Assert.Equal(typeof(Program).Assembly.ManifestModule.ModuleVersionId, Guid.Parse(runtime.Value<string>("module_version_id")));
            Assert.Equal(Program.ServerInformationalVersion, runtime.Value<string>("server_informational_version"));
        }

        private static ProcessStartInfo StartInfo(string profile)
        {
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8
            };
            start.ArgumentList.Add(typeof(Program).Assembly.Location);
            foreach (var argument in new[] { "--config", Path.Combine(profile, "absent.json"), "--disable-call-log" })
                start.ArgumentList.Add(argument);
            foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("BIMWRIGHT_DWG_", StringComparison.OrdinalIgnoreCase)).ToArray())
                start.Environment.Remove(key);
            // This redirects discovery only; logging is explicitly disabled above.
            start.Environment["LOCALAPPDATA"] = profile;
            return start;
        }

        internal Task<JObject> CallAsync(string tool, JObject arguments = null)
            => Request("tools/call", new JObject { ["name"] = tool, ["arguments"] = arguments ?? new JObject() });

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

        internal async Task StopSelectedIdentityAsync()
        {
            await StopProcess(selectedIdentity);
            await Task.WhenAll(identityOutput, identityError);
        }

        private static async Task StopProcess(Process owned)
        {
            if (owned == null || owned.HasExited) return;
            owned.StandardInput.Close();
            try { await owned.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (TimeoutException) { owned.Kill(entireProcessTree: true); await owned.WaitForExitAsync(); }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await StopProcess(process);
                await StopProcess(selectedIdentity);
                if (stderr != null) await stderr;
                if (identityOutput != null) await identityOutput;
                if (identityError != null) await identityError;
            }
            finally
            {
                process?.Dispose(); selectedIdentity?.Dispose(); Selected?.Dispose(); Other?.Dispose();
                var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!Path.GetFullPath(root).StartsWith(temp, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Unsafe test cleanup path.");
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
        }
    }
}
