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
                EnableToolbaker = true,
                AllowLanBind = false,
                LogLevel = "info"
            }));

            try
            {
                var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [DwgMcpConfig.EnvTarget] = "2025",
                    [DwgMcpConfig.EnvToolsets] = "query,modify",
                    [DwgMcpConfig.EnvReadOnly] = "true",
                    [DwgMcpConfig.EnvEnableToolbaker] = "false",
                    [DwgMcpConfig.EnvAllowLanBind] = "false",
                    [DwgMcpConfig.EnvLogLevel] = "debug"
                };

                var config = DwgMcpConfig.Load(
                    new[] { "--config", path, "--target", "2026", "--toolsets", "query", "--allow-lan-bind", "--log-level", "warn" },
                    envLookup: name => env.TryGetValue(name, out var value) ? value : null);

                Assert.Equal("2026", config.Target);
                Assert.Equal(new[] { "query" }, config.Toolsets);
                Assert.True(config.ReadOnly);
                Assert.False(config.EnableToolbaker);
                Assert.True(config.AllowLanBind);
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
            Assert.Null(config.EnableToolbaker);
            Assert.Null(config.AllowLanBind);
            Assert.Null(config.ResponseBudgetBytes);
            Assert.Null(config.ResponseWarnBytes);
            Assert.Null(config.ResponseStrongWarnBytes);
            Assert.False(config.ReadOnlyOrDefault);
            Assert.True(config.EnableToolbakerOrDefault);
            Assert.False(config.AllowLanBindOrDefault);
            Assert.Equal(80 * 1024 * 1024, config.ResponseBudgetBytesOrDefault);
            Assert.Equal(1024 * 1024, config.ResponseWarnBytesOrDefault);
            Assert.Equal(8 * 1024 * 1024, config.ResponseStrongWarnBytesOrDefault);
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

        [Fact]
        public void Response_bytes_apply_json_then_env_then_cli()
        {
            const string json = "{\"responseBudgetBytes\":10000,\"responseWarnBytes\":500,\"responseStrongWarnBytes\":5000}";
            var env = new Dictionary<string, string>
            {
                [DwgMcpConfig.EnvResponseBudgetBytes] = "20000",
                [DwgMcpConfig.EnvResponseWarnBytes] = "1000",
                [DwgMcpConfig.EnvResponseStrongWarnBytes] = "10000"
            };
            var fromJson = ResponseConfig(json);
            Assert.Equal(10000, fromJson.ResponseBudgetBytes);
            Assert.Equal(500, fromJson.ResponseWarnBytes);
            Assert.Equal(5000, fromJson.ResponseStrongWarnBytes);
            var fromEnv = ResponseConfig(json, env);
            Assert.Equal(20000, fromEnv.ResponseBudgetBytes);
            Assert.Equal(1000, fromEnv.ResponseWarnBytes);
            Assert.Equal(10000, fromEnv.ResponseStrongWarnBytes);
            var fromCli = ResponseConfig(json, env, new[]
            {
                "--response-budget-bytes", "30000", "--response-warn-bytes", "1500", "--response-strong-warn-bytes", "15000"
            });
            Assert.Equal(30000, fromCli.ResponseBudgetBytesOrDefault);
            Assert.Equal(1500, fromCli.ResponseWarnBytesOrDefault);
            Assert.Equal(15000, fromCli.ResponseStrongWarnBytesOrDefault);
        }

        [Theory]
        [InlineData(4096, 4096, 4096)]
        [InlineData(524288, 524288, 524288)]
        [InlineData(2097152, 1048576, 2097152)]
        [InlineData(16777216, 1048576, 8388608)]
        public void Unset_warning_defaults_clamp_to_the_effective_budget(int budget, int warn, int strong)
        {
            var config = ResponseConfig(args: new[] { "--response-budget-bytes", budget.ToString(System.Globalization.CultureInfo.InvariantCulture) });
            Assert.Equal(budget, config.ResponseBudgetBytesOrDefault);
            Assert.Equal(warn, config.ResponseWarnBytesOrDefault);
            Assert.Equal(strong, config.ResponseStrongWarnBytesOrDefault);
            Assert.Null(config.ResponseWarnBytes);
            Assert.Null(config.ResponseStrongWarnBytes);
        }

        [Fact]
        public void Explicit_json_null_response_options_stay_unset()
        {
            var config = ResponseConfig("{\"responseBudgetBytes\":null,\"responseWarnBytes\":null,\"responseStrongWarnBytes\":null}");
            Assert.Null(config.ResponseBudgetBytes);
            Assert.Null(config.ResponseWarnBytes);
            Assert.Null(config.ResponseStrongWarnBytes);
            Assert.Equal(80 * 1024 * 1024, config.ResponseBudgetBytesOrDefault);
            Assert.Equal(1024 * 1024, config.ResponseWarnBytesOrDefault);
            Assert.Equal(8 * 1024 * 1024, config.ResponseStrongWarnBytesOrDefault);
        }

        [Theory]
        [InlineData(4096, 1, 4096)]
        [InlineData(4096, 4096, 4096)]
        [InlineData(int.MaxValue, int.MaxValue, int.MaxValue)]
        public void Response_budget_boundaries_accept_positive_ordered_values(int budget, int warn, int strong)
        {
            var config = ResponseConfig(JsonConvert.SerializeObject(new { responseBudgetBytes = budget, responseWarnBytes = warn, responseStrongWarnBytes = strong }));
            Assert.Equal(budget, config.ResponseBudgetBytesOrDefault);
            Assert.Equal(warn, config.ResponseWarnBytesOrDefault);
            Assert.Equal(strong, config.ResponseStrongWarnBytesOrDefault);
        }

        [Theory]
        [InlineData("{\"responseBudgetBytes\":4095}", "ResponseBudgetBytes")]
        [InlineData("{\"responseBudgetBytes\":-1}", "ResponseBudgetBytes")]
        [InlineData("{\"responseWarnBytes\":0}", "ResponseWarnBytes")]
        [InlineData("{\"responseStrongWarnBytes\":0}", "ResponseStrongWarnBytes")]
        [InlineData("{\"responseWarnBytes\":9000000}", "ResponseWarnBytes")]
        [InlineData("{\"responseStrongWarnBytes\":83886081}", "ResponseStrongWarnBytes")]
        public void Response_budget_rejects_invalid_effective_bounds(string json, string parameter)
            => Assert.Equal(parameter, Assert.Throws<ArgumentException>(() => ResponseConfig(json)).ParamName);

        [Fact]
        public void Effective_validation_allows_numeric_lower_priority_values_to_be_overridden()
        {
            const string json = "{\"responseBudgetBytes\":-1,\"responseWarnBytes\":0,\"responseStrongWarnBytes\":0}";
            var env = new Dictionary<string, string>
            {
                [DwgMcpConfig.EnvResponseBudgetBytes] = "4096",
                [DwgMcpConfig.EnvResponseWarnBytes] = "10",
                [DwgMcpConfig.EnvResponseStrongWarnBytes] = "20"
            };
            Assert.Equal(4096, ResponseConfig(json, env).ResponseBudgetBytesOrDefault);
            env[DwgMcpConfig.EnvResponseBudgetBytes] = "0";
            env[DwgMcpConfig.EnvResponseWarnBytes] = "0";
            env[DwgMcpConfig.EnvResponseStrongWarnBytes] = "0";
            var config = ResponseConfig(json, env, new[]
            {
                "--response-budget-bytes", "8192", "--response-warn-bytes", "1024", "--response-strong-warn-bytes", "4096"
            });
            Assert.Equal(8192, config.ResponseBudgetBytesOrDefault);
            Assert.Equal(1024, config.ResponseWarnBytesOrDefault);
            Assert.Equal(4096, config.ResponseStrongWarnBytesOrDefault);
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData(" 4096")]
        [InlineData("+4096")]
        [InlineData("-1")]
        [InlineData("1.5")]
        [InlineData("1e6")]
        [InlineData("8MiB")]
        [InlineData("4_096")]
        [InlineData("2147483648")]
        [InlineData("٤٠٩٦")]
        public void Provided_env_and_cli_byte_counts_require_ascii_decimal_even_when_overridden(string raw)
        {
            foreach (var option in ResponseOptions)
            {
                var env = new Dictionary<string, string> { [option.Env] = raw };
                var fromEnv = Assert.Throws<ArgumentException>(() => ResponseConfig(env: env, args: new[] { option.Cli, "4096" }));
                Assert.Equal(option.Env, fromEnv.ParamName);
                var fromCli = Assert.Throws<ArgumentException>(() => ResponseConfig(args: new[] { option.Cli, raw, option.Cli, "4096" }));
                Assert.Equal(option.Cli, fromCli.ParamName);
            }
        }

        [Theory]
        [InlineData("--response-budget-bytes")]
        [InlineData("--response-warn-bytes")]
        [InlineData("--response-strong-warn-bytes")]
        public void Byte_count_cli_options_require_a_value(string option)
        {
            Assert.Equal(option, Assert.Throws<ArgumentException>(() => ResponseConfig(args: new[] { option })).ParamName);
            Assert.Equal(option, Assert.Throws<ArgumentException>(() => ResponseConfig(args: new[] { option, "--read-only" })).ParamName);
        }

        [Fact]
        public void Last_valid_byte_count_cli_occurrence_wins()
        {
            var config = ResponseConfig(args: new[]
            {
                "--response-budget-bytes", "4096", "--RESPONSE-BUDGET-BYTES", "8192",
                "--response-warn-bytes", "0", "--response-warn-bytes", "1024",
                "--response-strong-warn-bytes", "0", "--response-strong-warn-bytes", "4096"
            });
            Assert.Equal(8192, config.ResponseBudgetBytesOrDefault);
            Assert.Equal(1024, config.ResponseWarnBytesOrDefault);
            Assert.Equal(4096, config.ResponseStrongWarnBytesOrDefault);
        }

        [Fact]
        public void Direct_configuration_can_validate_without_materializing_nullable_defaults()
        {
            var config = new DwgMcpConfig();
            config.ValidateResponseBudget();
            Assert.Null(config.ResponseBudgetBytes);
            Assert.Null(config.ResponseWarnBytes);
            Assert.Null(config.ResponseStrongWarnBytes);
            config.ResponseBudgetBytes = 4095;
            Assert.Equal("ResponseBudgetBytes", Assert.Throws<ArgumentException>(() => config.ValidateResponseBudget()).ParamName);
        }

        private static readonly (string Cli, string Env)[] ResponseOptions =
        {
            ("--response-budget-bytes", DwgMcpConfig.EnvResponseBudgetBytes),
            ("--response-warn-bytes", DwgMcpConfig.EnvResponseWarnBytes),
            ("--response-strong-warn-bytes", DwgMcpConfig.EnvResponseStrongWarnBytes)
        };

        private static DwgMcpConfig ResponseConfig(string json = null, Dictionary<string, string> env = null, string[] args = null)
        {
            var path = Path.Combine(Path.GetTempPath(), "dwg-response-config-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                if (json != null) File.WriteAllText(path, json);
                return DwgMcpConfig.Load(args, path, name => env != null && env.TryGetValue(name, out var value) ? value : null);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
    }
}
