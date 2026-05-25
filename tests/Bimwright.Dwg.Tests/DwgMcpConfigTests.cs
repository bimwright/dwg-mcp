using System;
using System.Collections.Generic;
using System.IO;
using Bimwright.Dwg.Server;
using Newtonsoft.Json;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class DwgMcpConfigTests
    {
        [Fact]
        public void Load_AppliesJsonEnvCliPrecedence()
        {
            var path = Path.Combine(Path.GetTempPath(), "dwg-config-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, JsonConvert.SerializeObject(new DwgMcpConfig
            {
                Target = "2024",
                Toolsets = new List<string> { "query" },
                ReadOnly = false,
                EnableSendCode = false,
                EnableToolbaker = true,
                LogLevel = "info"
            }));

            try
            {
                var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [DwgMcpConfig.EnvTarget] = "2025",
                    [DwgMcpConfig.EnvToolsets] = "query,modify",
                    [DwgMcpConfig.EnvReadOnly] = "true",
                    [DwgMcpConfig.EnvEnableSendCode] = "yes",
                    [DwgMcpConfig.EnvEnableToolbaker] = "false",
                    [DwgMcpConfig.EnvLogLevel] = "debug"
                };

                var config = DwgMcpConfig.Load(
                    new[] { "--config", path, "--target", "2026", "--toolsets", "query", "--log-level", "warn" },
                    envLookup: name => env.TryGetValue(name, out var value) ? value : null);

                Assert.Equal("2026", config.Target);
                Assert.Equal(new[] { "query" }, config.Toolsets);
                Assert.True(config.ReadOnly);
                Assert.True(config.EnableSendCode);
                Assert.False(config.EnableToolbaker);
                Assert.Equal("warn", config.LogLevel);
            }
            finally
            {
                try { File.Delete(path); } catch { }
            }
        }

        [Fact]
        public void DefaultsKeepNullableValuesUnset()
        {
            var config = DwgMcpConfig.Load(Array.Empty<string>(), configFilePath: Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json"), envLookup: _ => null);

            Assert.Null(config.Target);
            Assert.Null(config.Toolsets);
            Assert.Null(config.ReadOnly);
            Assert.Null(config.EnableSendCode);
            Assert.Null(config.EnableToolbaker);
            Assert.False(config.ReadOnlyOrDefault);
            Assert.False(config.EnableSendCodeOrDefault);
            Assert.True(config.EnableToolbakerOrDefault);
        }

        [Theory]
        [InlineData("1", true)]
        [InlineData("true", true)]
        [InlineData("yes", true)]
        [InlineData("0", false)]
        [InlineData("false", false)]
        [InlineData("no", false)]
        public void Load_ParsesBooleanEnvironmentValues(string raw, bool expected)
        {
            var config = DwgMcpConfig.Load(
                Array.Empty<string>(),
                configFilePath: Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json"),
                envLookup: name => name == DwgMcpConfig.EnvReadOnly ? raw : null);

            Assert.Equal(expected, config.ReadOnly);
        }
    }
}
