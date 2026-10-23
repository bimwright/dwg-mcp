using System;
using System.Reflection;
using System.Threading.Tasks;
using Bimwright.Dwg.Server;
using Bimwright.Dwg.Server.Tools;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests;

[Collection("Script handlers")]
public class DirectSwitchReadOnlyTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Read_only_refuses_direct_switch_before_selector_validation_or_binding_changes(bool verify, bool rebind)
    {
        var previous = ServerState.Config;
        try
        {
            ServerState.Config = new DwgMcpConfig { ReadOnly = true, EnableCallLog = false };
            var before = ToolGateway.Client.CurrentTarget;
            // This selector must never reach normalization or discovery in read-only mode.
            var reply = JObject.Parse(await MetaTools.SwitchTarget("invalid-selector", verify,
                expected_document_id: "untrusted-document", rebind: rebind));
            Assert.False(reply.Value<bool>("ok"));
            Assert.Contains("read-only", reply.Value<string>("error"), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Invalid --target", reply.Value<string>("error"));
            Assert.True(JToken.DeepEquals(before, ToolGateway.Client.CurrentTarget));
        }
        finally { ServerState.Config = previous; }
    }

    [Fact]
    public void Switch_metadata_declares_a_non_destructive_non_idempotent_write()
    {
        var tool = typeof(MetaTools).GetMethod(nameof(MetaTools.SwitchTarget)).GetCustomAttribute<McpServerToolAttribute>();
        Assert.False(tool.ReadOnly);
        Assert.False(tool.Destructive);
        Assert.False(tool.Idempotent);
        Assert.False(tool.OpenWorld);
    }

    [Theory]
    [InlineData("lost", "TARGET_LOST")]
    [InlineData("reused", "TARGET_CHANGED")]
    [InlineData("stale", "TARGET_CHANGED")]
    public void Read_only_binding_refusals_preserve_error_codes_and_give_available_recovery(string scenario, string code)
    {
        var previous = ServerState.Config;
        try
        {
            ServerState.Config = new DwgMcpConfig { ReadOnly = true, EnableCallLog = false };
            var start = new DateTime(2026, 10, 23, 1, 0, 0, DateTimeKind.Utc);
            DateTime? actual = start;
            var binding = new TargetBinding(_ => actual);
            var call = binding.Capture(() => new DiscoveryInfo
            {
                Pid = 100, Target = "2024", TargetId = "acad-2024-100", ProcessStartUtc = start,
                Port = 1234, Token = "readonly-local-fixture"
            }, "get_drawing_info");
            var generation = binding.Generation;
            if (scenario == "stale") call.Generation--;
            else actual = scenario == "lost" ? null : start.AddMinutes(1);

            var error = Assert.Throws<InvalidOperationException>(() => binding.Validate(call));
            Assert.StartsWith(code + ":", error.Message);
            Assert.Contains("dwg_list_available_targets", error.Message);
            Assert.Contains("restart", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("--target", error.Message);
            Assert.Contains("pid:", error.Message);
            Assert.DoesNotContain("dwg_switch_target", error.Message);
            Assert.Equal(generation, binding.Generation);
            Assert.Equal(100, binding.Snapshot().Value<int>("pid"));
        }
        finally { ServerState.Config = previous; }
    }
}
