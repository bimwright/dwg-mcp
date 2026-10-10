using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Dwg.Server;
using Bimwright.Dwg.Server.Tools;
using ModelContextProtocol.Protocol;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class HealthToolsTests
    {
        private static JObject Gateway(string version = "2.1.0+development") => new JObject
        {
            ["assembly_informational_version"] = version,
            ["module_version_id"] = "3d72c7b7-7366-4990-a5f6-9a7a11a844df"
        };

        private static JObject Data(CallToolResult reply)
            => JObject.Parse(Assert.Single(reply.Content.OfType<TextContentBlock>()).Text);

        [Fact]
        public async Task Offline_probe_uses_authenticated_health_wire_and_keeps_loaded_plugin_identity()
        {
            using var server = new FakePluginServer(line =>
            {
                var request = JObject.Parse(line);
                Assert.Equal("health", request.Value<string>("cmd"));
                Assert.Equal("test-token", request.Value<string>("auth"));
                Assert.Empty((JObject)request["params"]);
                return new JObject
                {
                    ["id"] = request["id"], ["ok"] = true,
                    ["result"] = new JObject
                    {
                        ["ok"] = true, ["autocad_year"] = 2027, ["has_active_document"] = false,
                        ["setup_plugin"] = Gateway("2.1.0+plugin-development")
                    }
                }.ToString(Newtonsoft.Json.Formatting.None);
            });
            using var http = new HttpClient(new ReleaseUpdateCheckTests.Handler());
            var tools = new HealthTools(new PluginClient(() => new DiscoveryInfo { Port = server.Port, Token = "test-token" }),
                new ReleaseUpdateCheck(http), Gateway());
            var reply = await tools.Health();
            Assert.False(reply.IsError, Data(reply).ToString());
            var health = Data(reply);
            Assert.True(health.Value<bool>("host_connected"));
            Assert.Equal(2027, health.Value<int>("autocad_year"));
            Assert.False(health.Value<bool>("has_active_document"));
            Assert.Equal("2.1.0", health["mcp"].Value<string>("plugin_version"));
            Assert.Equal("plugin-development", health["mcp"].Value<string>("plugin_build_id"));
            Assert.Equal("match", health["mcp"].Value<string>("version_alignment"));
            Assert.Equal("unknown", health["mcp"].Value<string>("revision_alignment"));
            Assert.Equal("not_checked", health["update"].Value<string>("status"));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task No_target_preserves_gateway_identity_and_explicit_update_does_not_hide_host_failure(bool checkUpdates)
        {
            var handler = new ReleaseUpdateCheckTests.Handler();
            using var http = new HttpClient(handler);
            var tools = new HealthTools(new PluginClient(() => throw new InvalidOperationException("absent target")),
                new ReleaseUpdateCheck(http), Gateway("0.2.2+aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"));
            var reply = await tools.Health(checkUpdates);
            Assert.True(reply.IsError);
            var health = Data(reply);
            Assert.False(health.Value<bool>("host_connected"));
            Assert.Equal("NO_TARGET", health["error"].Value<string>("code"));
            Assert.Equal("0.2.2", health["mcp"].Value<string>("server_version"));
            Assert.Null(health["mcp"].Value<string>("plugin_version"));
            Assert.Equal(checkUpdates ? 2 : 0, handler.Calls);
            Assert.Equal(checkUpdates ? "checked" : "not_checked", health["update"].Value<string>("status"));
            if (checkUpdates)
            {
                Assert.Equal(new[] { "plugin" }, health["update"]["notification"]["unobserved_components"].Values<string>());
                Assert.False(health["update"]["notification"].Value<bool>("bundle_update_proposal_allowed"));
            }
        }

        [Fact]
        public async Task Legacy_dispatcher_is_responsive_but_health_and_plugin_version_remain_unverified()
        {
            using var server = new FakePluginServer(line => new JObject
            {
                ["id"] = JObject.Parse(line)["id"], ["ok"] = false, ["error"] = "unknown command: health"
            }.ToString(Newtonsoft.Json.Formatting.None));
            using var http = new HttpClient(new ReleaseUpdateCheckTests.Handler());
            var reply = await new HealthTools(new PluginClient(() => new DiscoveryInfo { Port = server.Port }),
                new ReleaseUpdateCheck(http), Gateway()).Health();
            Assert.True(reply.IsError);
            var health = Data(reply);
            Assert.True(health.Value<bool>("host_connected"));
            Assert.False(health.Value<bool>("health_supported"));
            Assert.Equal("HEALTH_UNSUPPORTED", health["error"].Value<string>("code"));
            Assert.Null(health["mcp"].Value<string>("plugin_version"));
        }

        [Fact]
        public async Task Plugin_without_identity_does_not_inherit_gateway_version()
        {
            using var server = new FakePluginServer(line => new JObject
            {
                ["id"] = JObject.Parse(line)["id"], ["ok"] = true, ["result"] = new JObject { ["healthy"] = true }
            }.ToString(Newtonsoft.Json.Formatting.None));
            using var http = new HttpClient(new ReleaseUpdateCheckTests.Handler());
            var health = Data(await new HealthTools(new PluginClient(() => new DiscoveryInfo { Port = server.Port }),
                new ReleaseUpdateCheck(http), Gateway()).Health());
            Assert.True(health.Value<bool>("host_connected"));
            Assert.Null(health["mcp"].Value<string>("plugin_version"));
            Assert.False(health["update"]["components"]["plugin"].Value<bool>("identity_observed"));
        }

        [Fact]
        public async Task Wrong_response_id_does_not_verify_the_intended_host()
        {
            using var server = new FakePluginServer(_ => "{\"id\":\"foreign\",\"ok\":true,\"result\":{\"healthy\":true}}");
            using var http = new HttpClient(new ReleaseUpdateCheckTests.Handler());
            var reply = await new HealthTools(new PluginClient(() => new DiscoveryInfo { Port = server.Port }),
                new ReleaseUpdateCheck(http), Gateway()).Health();
            Assert.True(reply.IsError);
            Assert.False(Data(reply).Value<bool>("host_connected"));
            Assert.Equal("INVALID_HEALTH_RESPONSE", Data(reply)["error"].Value<string>("code"));
        }

        [Theory]
        [InlineData("{\"ok\":{}}")]
        [InlineData("{\"ok\":\"true\"}")]
        [InlineData("{}")]
        public async Task Malformed_health_status_is_rejected_without_losing_gateway_identity(string payload)
        {
            using var server = new FakePluginServer(line => new JObject
            {
                ["id"] = JObject.Parse(line)["id"], ["ok"] = true, ["result"] = JObject.Parse(payload)
            }.ToString(Newtonsoft.Json.Formatting.None));
            using var http = new HttpClient(new ReleaseUpdateCheckTests.Handler());
            var reply = await new HealthTools(new PluginClient(() => new DiscoveryInfo { Port = server.Port }),
                new ReleaseUpdateCheck(http), Gateway()).Health();
            Assert.True(reply.IsError);
            var health = Data(reply);
            Assert.False(health.Value<bool>("host_connected"));
            Assert.Equal("INVALID_HEALTH_RESPONSE", health["error"].Value<string>("code"));
            Assert.Equal("2.1.0+development", health["setup_gateway"].Value<string>("assembly_informational_version"));
        }

        [Fact]
        public async Task Busy_plugin_retains_observed_identity_but_does_not_claim_document_state_or_success()
        {
            using var server = new FakePluginServer(line => new JObject
            {
                ["id"] = JObject.Parse(line)["id"], ["ok"] = true,
                ["result"] = new JObject
                {
                    ["ok"] = false, ["healthy"] = false, ["state_probe"] = "busy",
                    ["has_active_document"] = JValue.CreateNull(), ["setup_plugin"] = Gateway("2.1.0+plugin-loaded"),
                    ["error"] = new JObject { ["code"] = "UI_BUSY" }
                }
            }.ToString(Newtonsoft.Json.Formatting.None));
            using var http = new HttpClient(new ReleaseUpdateCheckTests.Handler());
            var reply = await new HealthTools(new PluginClient(() => new DiscoveryInfo { Port = server.Port }),
                new ReleaseUpdateCheck(http), Gateway()).Health();
            Assert.True(reply.IsError);
            var health = Data(reply);
            Assert.True(health.Value<bool>("host_connected"));
            Assert.Equal("busy", health.Value<string>("state_probe"));
            Assert.Equal(JTokenType.Null, health["has_active_document"].Type);
            Assert.Equal("plugin-loaded", health["mcp"].Value<string>("plugin_build_id"));
        }

        [Fact]
        public async Task Mutating_one_reply_cannot_change_the_gateway_capture()
        {
            using var http = new HttpClient(new ReleaseUpdateCheckTests.Handler());
            var original = Gateway();
            var tools = new HealthTools(new PluginClient(() => throw new InvalidOperationException()), new ReleaseUpdateCheck(http), original);
            original["assembly_informational_version"] = "0.0.0";
            var first = Data(await tools.Health());
            first["setup_gateway"]["assembly_informational_version"] = "0.0.0";
            Assert.Equal("2.1.0+development", Data(await tools.Health())["setup_gateway"].Value<string>("assembly_informational_version"));
        }

        [Fact]
        public async Task Caller_cancellation_is_not_converted_to_an_update_or_host_fault()
        {
            using var http = new HttpClient(new ReleaseUpdateCheckTests.Handler());
            using var token = new CancellationTokenSource();
            token.Cancel();
            var tools = new HealthTools(new PluginClient(() => throw new InvalidOperationException()), new ReleaseUpdateCheck(http), Gateway());
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tools.Health(true, token.Token));
        }
    }
}
