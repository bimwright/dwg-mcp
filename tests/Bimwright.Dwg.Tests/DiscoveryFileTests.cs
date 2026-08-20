using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Bimwright.Dwg.Server;
using Newtonsoft.Json;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class DiscoveryFileTests
    {
        [Fact]
        public void Resolve_PrefersHighestAvailableJsonTarget()
        {
            using var temp = new TempDiscoveryRoot();
            temp.WriteJson("2022", port: 2222, token: "old");
            temp.WriteJson("2027", port: 2727, token: "new");

            var info = AuthToken.Resolve(null, temp.Root);

            Assert.Equal("2027", info.Target);
            Assert.Equal(2727, info.Port);
            Assert.Equal("new", info.Token);
        }

        [Fact]
        public void Resolve_HonorsPinnedTarget()
        {
            using var temp = new TempDiscoveryRoot();
            temp.WriteJson("2024", port: 2424, token: "pin");
            temp.WriteJson("2026", port: 2626, token: "newer");

            var info = AuthToken.Resolve("2024", temp.Root);

            Assert.Equal("2024", info.Target);
            Assert.Equal(2424, info.Port);
            Assert.Equal("pin", info.Token);
        }

        [Theory]
        [InlineData("R24")]
        [InlineData("acad24")]
        [InlineData("2028")]
        public void Resolve_RejectsInvalidTarget(string target)
        {
            using var temp = new TempDiscoveryRoot();

            var ex = Assert.Throws<ArgumentException>(() => AuthToken.Resolve(target, temp.Root));

            Assert.Contains("4-digit AutoCAD year", ex.Message);
        }

        [Fact]
        public void ListAvailable_DeletesStalePidFiles()
        {
            using var temp = new TempDiscoveryRoot();
            var stalePath = temp.WriteJson("2026", port: 2626, token: "stale", pid: int.MaxValue);
            temp.WriteJson("2025", port: 2525, token: "live");

            var available = AuthToken.ListAvailable(temp.Root).ToArray();

            Assert.Single(available);
            Assert.Equal("2025", available[0].Target);
            Assert.False(File.Exists(stalePath));
        }

        [Fact]
        public void Resolve_IgnoresUnsupportedTransportAndFallsBack()
        {
            using var temp = new TempDiscoveryRoot();
            var badPath = temp.WriteJson("2027", port: 2727, token: "bad", transport: "udp");
            temp.WriteJson("2026", port: 2626, token: "good");

            var info = AuthToken.Resolve(null, temp.Root);

            Assert.Equal("2026", info.Target);
            Assert.Equal(2626, info.Port);
            Assert.False(File.Exists(badPath));
        }

        [Fact]
        public void Resolve_ParsesNamedPipeDiscovery()
        {
            using var temp = new TempDiscoveryRoot();
            temp.WriteJson("2025", transport: "pipe", pipeName: @"\\.\pipe\bimwright-dwg-2025", token: "pipe-token");

            var info = AuthToken.Resolve("2025", temp.Root);

            Assert.Equal("2025", info.Target);
            Assert.Equal("pipe", info.Transport);
            Assert.Equal("bimwright-dwg-2025", info.PipeName);
            Assert.Equal("pipe-token", info.Token);
        }

        [Fact]
        public void Resolve_ParsesPipeDiscoveryWithLiteralNullPort()
        {
            // Regression test: the real plugin's PipeTransportServer writes
            // "port": null for AutoCAD 2025-2027 (named pipe transport). Before
            // the fix, deserializing that into a non-nullable int threw inside
            // TryReadJson's try/catch, so the file was treated as invalid and
            // deleted on every single resolve attempt, permanently breaking pipe
            // discovery.
            using var temp = new TempDiscoveryRoot();
            var path = Path.Combine(temp.Root, "Dwg", "acad-2025.json");
            File.WriteAllText(path, @"{
  ""schema_version"": 2,
  ""acad_year"": 2025,
  ""transport"": ""pipe"",
  ""port"": null,
  ""pipe_name"": ""BimwrightDwg-2025-12345"",
  ""auth_token"": ""abc123"",
  ""pid"": " + Process.GetCurrentProcess().Id + @",
  ""process_name"": ""acad"",
  ""started_at_utc"": ""2026-01-01T00:00:00Z""
}");

            var info = AuthToken.Resolve("2025", temp.Root);

            Assert.Equal("2025", info.Target);
            Assert.Equal("pipe", info.Transport);
            Assert.Equal("BimwrightDwg-2025-12345", info.PipeName);
            Assert.Equal("abc123", info.Token);
            Assert.True(File.Exists(path), "the discovery file must survive a literal null port");
        }

        [Fact]
        public void Resolve_FallsBackToLegacyAcad24Discovery()
        {
            using var temp = new TempDiscoveryRoot();
            File.WriteAllText(
                Path.Combine(temp.Root, "portAcad24.txt"),
                "2424\nlegacy-token\n" + Process.GetCurrentProcess().Id + "\n");

            var info = AuthToken.Resolve("2024", temp.Root);

            Assert.Equal("2024", info.Target);
            Assert.Equal(2424, info.Port);
            Assert.Equal("legacy-token", info.Token);
        }

        private sealed class TempDiscoveryRoot : IDisposable
        {
            public string Root { get; } = Path.Combine(Path.GetTempPath(), "dwg-discovery-" + Guid.NewGuid().ToString("N"));

            public TempDiscoveryRoot()
            {
                Directory.CreateDirectory(Path.Combine(Root, "Dwg"));
            }

            public string WriteJson(
                string target,
                int port = 0,
                string token = "token",
                int? pid = null,
                string transport = "tcp",
                string pipeName = null)
            {
                var info = new DiscoveryInfo
                {
                    SchemaVersion = 2,
                    Target = target,
                    Version = target,
                    Transport = transport,
                    Host = "127.0.0.1",
                    Port = port == 0 ? (int?)null : port,
                    PipeName = pipeName,
                    Token = token,
                    Pid = pid ?? Process.GetCurrentProcess().Id,
                    ProcessName = "acad.exe",
                    StartedAtUtc = DateTime.UtcNow
                };
                var path = Path.Combine(Root, "Dwg", "acad-" + target + ".json");
                File.WriteAllText(path, JsonConvert.SerializeObject(info));
                return path;
            }

            public void Dispose()
            {
                try { Directory.Delete(Root, recursive: true); } catch { }
            }
        }
    }
}
