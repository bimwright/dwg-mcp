using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Bimwright.Dwg.Server;
using Bimwright.Dwg.Server.Tools;
using Bimwright.Dwg.Tests.Helpers;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    [Collection("Script handlers")]
    public class RuntimeSwitchTests
    {
        [Fact]
        public void Defaults_keep_send_code_on_and_call_log_off()
        {
            var config = Load();
            Assert.Null(config.EnableSendCode);
            Assert.Null(config.EnableCallLog);
            Assert.True(config.EnableSendCodeOrDefault);
            Assert.False(config.EnableCallLogOrDefault);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Json_then_env_then_explicit_cli(bool value)
        {
            var path = Path.Combine(Path.GetTempPath(), "dwg-switch-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, new JObject { ["enableSendCode"] = value, ["enableCallLog"] = value }.ToString());
            try
            {
                var json = DwgMcpConfig.Load(configFilePath: path, envLookup: _ => null);
                Assert.Equal(value, json.EnableSendCode);
                Assert.Equal(value, json.EnableCallLog);
                Func<string, string> env = name => name == DwgMcpConfig.EnvEnableSendCode || name == DwgMcpConfig.EnvEnableCallLog
                    ? (!value).ToString() : null;
                var fromEnv = DwgMcpConfig.Load(configFilePath: path, envLookup: env);
                Assert.Equal(!value, fromEnv.EnableSendCode);
                Assert.Equal(!value, fromEnv.EnableCallLog);
                var cli = DwgMcpConfig.Load(new[] { value ? "--enable-send-code" : "--disable-send-code", value ? "--enable-call-log" : "--disable-call-log" }, path, env);
                Assert.Equal(value, cli.EnableSendCode);
                Assert.Equal(value, cli.EnableCallLog);
            }
            finally { File.Delete(path); }
        }

        [Theory]
        [InlineData("on", true)]
        [InlineData("1", true)]
        [InlineData("YES", true)]
        [InlineData("off", false)]
        [InlineData("0", false)]
        [InlineData("no", false)]
        public void Switch_environment_uses_existing_boolean_semantics(string raw, bool expected)
        {
            var config = Load(env: name => name == DwgMcpConfig.EnvEnableSendCode || name == DwgMcpConfig.EnvEnableCallLog ? raw : null);
            Assert.Equal(expected, config.EnableSendCode);
            Assert.Equal(expected, config.EnableCallLog);
        }

        [Fact]
        public void Invalid_env_values_do_not_override_defaults()
        {
            var config = Load(env: _ => "invalid");
            Assert.True(config.EnableSendCodeOrDefault);
            Assert.False(config.EnableCallLogOrDefault);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Last_explicit_cli_occurrence_wins(bool enabled)
        {
            var config = Load(new[] { enabled ? "--disable-send-code" : "--enable-send-code", enabled ? "--enable-send-code" : "--disable-send-code",
                enabled ? "--disable-call-log" : "--enable-call-log", enabled ? "--enable-call-log" : "--disable-call-log" });
            Assert.Equal(enabled, config.EnableSendCode);
            Assert.Equal(enabled, config.EnableCallLog);
        }

        [Fact]
        public void Disabled_switch_filters_only_send_code_and_keeps_lisp_refusal_metadata()
        {
            var methods = SendCodePolicy.ExposedMethods(typeof(CodeTools), false).ToArray();
            Assert.Equal("RunLisp", Assert.Single(methods).Name);
            var catalog = ToolCatalogBuilder.Build(new[] { typeof(CodeTools) }, false);
            Assert.Equal("dwg_run_lisp", Assert.Single((JArray)catalog["tools"])["name"].Value<string>());
            Assert.Equal(2, SendCodePolicy.ExposedMethods(typeof(CodeTools), true).Count());
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Read_only_wins_over_send_code_switch(bool enabled)
        {
            var config = new DwgMcpConfig { ReadOnly = true, EnableSendCode = enabled };
            Assert.True(SendCodePolicy.IsBlocked("send_code", null, config));
            var resolver = typeof(Program).GetMethod("ResolveToolTypesForRegistration", BindingFlags.NonPublic | BindingFlags.Static);
            var types = (System.Collections.Generic.IEnumerable<Type>)resolver.Invoke(null, new object[] { ToolsetFilter.Resolve(config), true });
            Assert.DoesNotContain(typeof(CodeTools), types);
        }

        [Theory]
        [InlineData("send_code", "{}")]
        [InlineData("dwg_send_code", "{}")]
        [InlineData("batch_execute", "{\"commands\":[{\"cmd\":\"get_drawing_info\"},{\"cmd\":\"send_code\",\"params\":{\"code\":\"return 1;\"}}]}")]
        [InlineData("batch_execute", "{\"commands\":[{\"cmd\":\"batch_execute\",\"params\":{\"commands\":[{\"cmd\":\"send_code\"}]}}]}")]
        public async Task Disabled_code_is_refused_before_wire_dispatch(string command, string json)
        {
            var previous = ServerState.Config;
            try
            {
                ServerState.Config = new DwgMcpConfig { EnableSendCode = false };
                var response = await Capture.Local(() => ToolGateway.LoggedCall(command, JObject.Parse(json), JObject.Parse(json)));
                Assert.False(response.Value<bool>("ok"));
                Assert.Contains("disabled", response.Value<string>("error"));
                response = await Capture.Local(async () => Newtonsoft.Json.JsonConvert.SerializeObject(await ToolGateway.SendRaw(command, JObject.Parse(json))));
                Assert.False(response.Value<bool>("ok"));
            }
            finally { ServerState.Config = previous; }
        }

        [Fact]
        public async Task Enabled_code_reaches_plugin_with_original_body()
        {
            var previous = ServerState.Config;
            try
            {
                ServerState.Config = new DwgMcpConfig();
                var capture = await Capture.Send(() => CodeTools.SendCode("return \"Xin chào\";"));
                Assert.Equal("send_code", capture.Command);
                Assert.Equal("return \"Xin chào\";", capture.Json().Value<string>("code"));
            }
            finally { ServerState.Config = previous; }
        }

        [Fact]
        public void Disabled_code_does_not_block_non_code_batch()
        {
            var parameters = JObject.Parse("{\"commands\":[{\"cmd\":\"get_drawing_info\"}]}");
            Assert.False(SendCodePolicy.IsBlocked("batch_execute", parameters, new DwgMcpConfig { EnableSendCode = false }));
        }

        private static DwgMcpConfig Load(string[] args = null, Func<string, string> env = null)
            => DwgMcpConfig.Load(args, Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json"), env ?? (_ => null));
    }
}
