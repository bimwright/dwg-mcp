using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Bimwright.Dwg.Server;
using Newtonsoft.Json;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class InstanceTargetingTests
    {
        [Fact]
        public async Task Discovery_replacement_cannot_redirect_an_existing_client()
        {
            using var first = new FakePluginServer(line => Reply(line, "first"));
            using var second = new FakePluginServer(line => Reply(line, "second"));
            var selected = new DiscoveryInfo { Port = first.Port, Token = "first-token" };
            var client = new PluginClient(() => selected);
            Assert.Equal("first", (await client.SendAsync("get_drawing_info", new { })).Result.ToString());
            selected = new DiscoveryInfo { Port = second.Port, Token = "second-token" };
            Assert.Equal("first", (await client.SendAsync("get_drawing_info", new { })).Result.ToString());
        }

        [Fact]
        public void Per_instance_discovery_is_read_and_deduplicates_the_year_alias()
        {
            var root = Path.Combine(Path.GetTempPath(), "dwg-instance-" + Guid.NewGuid().ToString("N"));
            var directory = Path.Combine(root, "dwg-mcp");
            Directory.CreateDirectory(directory);
            try
            {
                var info = new DiscoveryInfo { Target = "2024", Pid = Process.GetCurrentProcess().Id, Port = 1234, Token = "test" };
                var path = Path.Combine(directory, "acad-2024-" + info.Pid + ".json");
                File.WriteAllText(path, JsonConvert.SerializeObject(info));
                Assert.Single(AuthToken.ListAvailable(root));
                File.Copy(path, Path.Combine(directory, "acad-2024.json"));
                Assert.Single(AuthToken.ListAvailable(root));
            }
            finally { Directory.Delete(root, true); }
        }

        private static string Reply(string line, string value)
        {
            var request = JsonConvert.DeserializeObject<McpRequest>(line);
            return JsonConvert.SerializeObject(new McpResponse { Id = request.Id, Ok = true, Result = value });
        }

        [Fact]
        public async Task Same_year_instances_require_explicit_PID_and_reject_reused_process_identity()
        {
            var root = Path.Combine(Path.GetTempPath(), "dwg-two-instances-" + Guid.NewGuid().ToString("N"));
            var directory = Path.Combine(root, "dwg-mcp");
            Directory.CreateDirectory(directory);
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            start.ArgumentList.Add(typeof(Program).Assembly.Location);
            start.ArgumentList.Add("--config");
            start.ArgumentList.Add(Path.Combine(root, "absent-config.json"));
            start.Environment["LOCALAPPDATA"] = Path.Combine(root, "child-profile");
            using var child = Process.Start(start);
            var output = child.StandardOutput.ReadToEndAsync();
            var errors = child.StandardError.ReadToEndAsync();
            try
            {
                using var current = Process.GetCurrentProcess();
                foreach (var process in new[] { current, child })
                {
                    var info = new DiscoveryInfo { SchemaVersion = 3, AcadYear = 2024, Target = "2024", Pid = process.Id,
                        TargetId = "acad-2024-" + process.Id, ProcessStartUtc = process.StartTime.ToUniversalTime(), Port = 1234, Token = "test" };
                    File.WriteAllText(Path.Combine(directory, info.TargetId + ".json"), JsonConvert.SerializeObject(info));
                }
                Assert.Equal(2, AuthToken.ListAvailable(root).Count);
                Assert.Contains("AMBIGUOUS_TARGET", Assert.Throws<InvalidOperationException>(() => AuthToken.Resolve("2024", root)).Message);
                Assert.Equal(child.Id, AuthToken.Resolve("pid:" + child.Id, root).Pid);
                Assert.Equal(current.Id, AuthToken.Resolve("id:acad-2024-" + current.Id, root).Pid);
                var path = Path.Combine(directory, "acad-2024-" + child.Id + ".json");
                var stale = JsonConvert.DeserializeObject<DiscoveryInfo>(File.ReadAllText(path));
                stale.ProcessStartUtc = stale.ProcessStartUtc.Value.AddMinutes(-1);
                File.WriteAllText(path, JsonConvert.SerializeObject(stale));
                Assert.Equal(current.Id, Assert.Single(AuthToken.ListAvailable(root)).Pid);
                Assert.False(File.Exists(path));
            }
            finally
            {
                child.StandardInput.Close();
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
                await Task.WhenAll(output, errors);
                Directory.Delete(root, true);
            }
        }
    }
}
