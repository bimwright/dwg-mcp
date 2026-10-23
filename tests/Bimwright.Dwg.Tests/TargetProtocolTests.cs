using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bimwright.Dwg.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class TargetProtocolTests
    {
        [Fact]
        public async Task SDK_exposes_target_refusal_without_sending_the_mutation()
        {
            var root = Path.Combine(Path.GetTempPath(), "dwg-target-protocol-" + Guid.NewGuid().ToString("N"));
            var dir = Path.Combine(root, "Bimwright", "dwg-mcp");
            Directory.CreateDirectory(dir);
            using var current = Process.GetCurrentProcess();
            var calls = 0;
            using var fake = new FakePluginServer(_ => { System.Threading.Interlocked.Increment(ref calls); return "{}"; });
            File.WriteAllText(Path.Combine(dir, "acad-2024.json"), JsonConvert.SerializeObject(new DiscoveryInfo
            {
                Target = "2024", Pid = current.Id, Port = fake.Port, Token = "test"
            }));
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8 };
            start.ArgumentList.Add(typeof(Program).Assembly.Location);
            start.ArgumentList.Add("--config"); start.ArgumentList.Add(Path.Combine(root, "absent.json"));
            start.ArgumentList.Add("--target"); start.ArgumentList.Add("2024");
            foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("BIMWRIGHT_DWG_", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
            start.Environment["LOCALAPPDATA"] = root;
            using var process = Process.Start(start);
            var stderr = process.StandardError.ReadToEndAsync();
            try
            {
                await Request(process, 1, "initialize", new { protocolVersion = "2025-11-25", capabilities = new { }, clientInfo = new { name = "target-test", version = "1" } });
                await process.StandardInput.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
                var result = await Request(process, 2, "tools/call", new { name = "dwg_create_line", arguments = new { start = "{\"x\":0,\"y\":0,\"z\":0}", end = "{\"x\":1,\"y\":1,\"z\":0}" } });
                Assert.True(result["result"].Value<bool>("isError"));
                Assert.Contains("TARGET_UNCONFIRMED", result["result"]["content"][0].Value<string>("text"));
                Assert.Equal(0, calls);
            }
            finally
            {
                process.StandardInput.Close();
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
                await stderr;
                Directory.Delete(root, true);
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
                if (reply.Value<int?>("id") == id) return reply;
            }
        }
    }
}
