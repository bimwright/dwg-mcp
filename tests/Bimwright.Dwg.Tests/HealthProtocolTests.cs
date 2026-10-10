using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Bimwright.Dwg.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class HealthProtocolTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Health_is_available_offline_without_AutoCAD_and_exposes_same_process_runtime_metadata(bool readOnly)
        {
            var root = Path.Combine(Path.GetTempPath(), "dwg-health-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8
            };
            start.ArgumentList.Add(typeof(Program).Assembly.Location);
            start.ArgumentList.Add("--config");
            start.ArgumentList.Add(Path.Combine(root, "absent.json"));
            start.ArgumentList.Add("--toolsets");
            start.ArgumentList.Add("query");
            if (readOnly) start.ArgumentList.Add("--read-only");
            foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("BIMWRIGHT_DWG_", StringComparison.OrdinalIgnoreCase)).ToArray())
                start.Environment.Remove(key);
            start.Environment["LOCALAPPDATA"] = root;
            using var process = Process.Start(start);
            var stderr = process.StandardError.ReadToEndAsync();
            try
            {
                var initialize = await Request(process, 1, "initialize", new
                {
                    protocolVersion = "2025-11-25", capabilities = new { },
                    clientInfo = new { name = "dwg-health-contract-test", version = "1" }
                });
                await process.StandardInput.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
                await process.StandardInput.FlushAsync();
                var catalog = await Request(process, 2, "tools/list", new { });
                var tool = Assert.Single((JArray)catalog["result"]["tools"], t => t.Value<string>("name") == "dwg_health");
                Assert.Equal("boolean", tool["inputSchema"]["properties"]["check_updates"].Value<string>("type"));
                Assert.DoesNotContain("check_updates", tool["inputSchema"]["required"]?.Values<string>() ?? Array.Empty<string>());
                Assert.True(tool["annotations"].Value<bool>("readOnlyHint"));
                Assert.False(tool["annotations"].Value<bool>("destructiveHint"));
                Assert.True(tool["annotations"].Value<bool>("idempotentHint"));
                Assert.True(tool["annotations"].Value<bool>("openWorldHint"));
                Assert.Contains("dwg_health(check_updates=true)", initialize["result"].Value<string>("instructions"));
                var called = await Request(process, 3, "tools/call", new { name = "dwg_health", arguments = new { } });
                Assert.True(called["result"].Value<bool>("isError"));
                var health = JObject.Parse(called["result"]["content"][0].Value<string>("text"));
                Assert.False(health.Value<bool>("ok"));
                Assert.False(health.Value<bool>("host_connected"));
                Assert.Equal("NO_TARGET", health["error"].Value<string>("code"));
                Assert.Equal("not_checked", health["update"].Value<string>("status"));
                Assert.Equal(JTokenType.Null, health["update"]["notification"].Type);
                Assert.Equal("check_only", health["update_guidance"].Value<string>("mode"));
                Assert.DoesNotContain("install.ps1", health["update_guidance"].ToString());
                Assert.Equal("dwg-mcp", health["setup_gateway"].Value<string>("product_id"));
                Assert.Equal(process.Id, health["setup_gateway"].Value<int>("process_id"));
                Assert.Equal("loaded_assembly", health["setup_gateway"].Value<string>("identity_basis"));
                Assert.Equal(initialize["result"]["serverInfo"].Value<string>("version"), health["mcp"].Value<string>("server_informational_version"));
                Assert.Equal(typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion,
                    initialize["result"]["serverInfo"].Value<string>("version"));
                var key = health["client_verification"].Value<string>("tools_list_metadata_key");
                Assert.True(JToken.DeepEquals(tool["_meta"][key], health["client_verification"]["expected_runtime"]));
                Assert.Equal("not_observable_by_server", health["client_verification"].Value<string>("status"));
                Assert.Empty(Directory.GetFiles(root, "*.jsonl", SearchOption.AllDirectories));
            }
            finally
            {
                process.StandardInput.Close();
                try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); }
                catch (TimeoutException) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
                await stderr;
                Directory.Delete(root, recursive: true);
            }
        }

        private static async Task<JObject> Request(Process process, int id, string method, object parameters)
        {
            await process.StandardInput.WriteLineAsync(JsonConvert.SerializeObject(new { jsonrpc = "2.0", id, method, @params = parameters }));
            await process.StandardInput.FlushAsync();
            while (true)
            {
                var line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20));
                Assert.NotNull(line);
                var reply = JObject.Parse(line);
                if (reply.Value<int?>("id") == id)
                {
                    Assert.Null(reply["error"]);
                    return reply;
                }
            }
        }
    }
}
