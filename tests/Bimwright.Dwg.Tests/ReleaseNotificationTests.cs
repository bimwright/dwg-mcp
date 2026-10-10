#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using System.Net;
using Bimwright.Dwg.Server;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Tests;

public sealed class ReleaseNotificationTests
{
    private const string ReleaseBuild = "0.2.2+aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private static async Task<JObject> Check(string server, string? plugin,
        ReleaseUpdateCheckTests.Handler? handler = null)
    {
        using var client = new HttpClient(handler ?? new ReleaseUpdateCheckTests.Handler())
            { Timeout = TimeSpan.FromMilliseconds(100) };
        return await new ReleaseUpdateCheck(client).CheckAsync(ReleaseUpdateCheckTests.Identity(server),
            plugin is null ? null : ReleaseUpdateCheckTests.Identity(plugin), CancellationToken.None);
    }

    private static JObject Notice(JObject update) => Assert.IsType<JObject>(update["notification"]);

    private static void AssertNoInstallWorkflow(JObject update, string expectedMode)
    {
        var guidance = HealthRuntimeInfo.UpdateGuidance(update);
        Assert.Equal(expectedMode, guidance.Value<string>("mode"));
        Assert.False(guidance.Value<bool>("requires_user_authorization"));
        Assert.False(guidance.Value<bool>("automatic_update_supported"));
        Assert.DoesNotContain("install.ps1", guidance["steps"]!.ToString());
        Assert.DoesNotContain("running the installer", guidance["steps"]!.ToString());
        if (update["notification"] is JObject notice)
        {
            Assert.False(notice.Value<bool>("bundle_update_proposal_allowed"));
            Assert.False(notice.Value<bool>("requires_user_authorization"));
        }
    }

    [Theory]
    [InlineData("0.2.1", ReleaseBuild, "server")]
    [InlineData(ReleaseBuild, "0.2.1", "plugin")]
    public async Task Confirmed_older_component_with_a_matching_peer_can_propose_an_authorized_bundle_update(
        string server, string plugin, string affected)
    {
        var update = await Check(server, plugin);
        var notice = Notice(update);
        Assert.Equal("NEW_RELEASE_AVAILABLE", notice.Value<string>("code"));
        Assert.Equal(new[] { affected }, notice["components"]!.Values<string>());
        Assert.Equal("dwg-mcp 0.2.2 is available for: " + affected + ".", notice.Value<string>("message"));
        Assert.Equal("notify_user_and_propose_update", notice.Value<string>("agent_action"));
        Assert.True(notice.Value<bool>("bundle_update_proposal_allowed"));
        Assert.True(notice.Value<bool>("requires_user_authorization"));
        Assert.Empty((JArray)notice["protected_components"]!);
        Assert.Empty((JArray)notice["unobserved_components"]!);
        var guidance = HealthRuntimeInfo.UpdateGuidance(update);
        Assert.Equal("prepare_update", guidance.Value<string>("mode"));
        Assert.True(guidance.Value<bool>("requires_user_authorization"));
        Assert.False(guidance.Value<bool>("automatic_update_supported"));
        Assert.Contains("install.ps1 -WhatIf", guidance["steps"]!.ToString());
        Assert.Contains("Back up the installed bundle, server and client configuration", guidance["steps"]!.ToString());
        Assert.Contains("does not provide atomic bundle+server rollback", guidance["steps"]!.ToString());
    }

    [Theory]
    [InlineData("0.3.0", "0.2.1", "server", "plugin")]
    [InlineData("0.2.1", "0.3.0", "plugin", "server")]
    public async Task Ahead_component_blocks_the_full_bundle_even_when_its_peer_is_older(
        string server, string plugin, string preserved, string older)
    {
        var update = await Check(server, plugin);
        var notice = Notice(update);
        Assert.Equal("NEW_RELEASE_AVAILABLE", notice.Value<string>("code"));
        Assert.Equal(new[] { older }, notice["components"]!.Values<string>());
        Assert.Equal(new[] { preserved }, notice["protected_components"]!.Values<string>());
        Assert.Equal("ahead_of_release", (string?)notice["protected_component_reasons"]?[preserved]);
        Assert.Contains(preserved, notice.Value<string>("message"));
        Assert.True(update.Value<bool>("update_available")); // Discovery is not authorization to replace the full bundle.
        Assert.Equal("notify_user_and_review_update_state", notice.Value<string>("agent_action"));
        AssertNoInstallWorkflow(update, "review");
    }

    [Fact]
    public async Task Local_ahead_without_an_older_peer_is_an_informational_notice_not_a_downgrade_proposal()
    {
        var update = await Check("0.3.0", ReleaseBuild);
        var notice = Notice(update);
        Assert.Equal("LOCAL_VERSION_AHEAD", notice.Value<string>("code"));
        Assert.Equal(new[] { "server" }, notice["components"]!.Values<string>());
        Assert.Equal("ahead_of_release", (string?)notice["protected_component_reasons"]?["server"]);
        Assert.Contains("0.3.0", notice.Value<string>("message"));
        Assert.Contains("0.2.2", notice.Value<string>("message"));
        AssertNoInstallWorkflow(update, "review");
    }

    [Theory]
    [InlineData("0.2.2+bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "RELEASE_REVIEW_REQUIRED", "release_mismatch")]
    [InlineData("0.2.2+development", "RELEASE_IDENTITY_UNVERIFIED", "runtime_revision_unavailable")]
    [InlineData("invalid-version", "RELEASE_IDENTITY_UNVERIFIED", "runtime_version_unavailable")]
    public async Task Same_or_unverifiable_local_build_is_protected_without_claiming_a_new_release(
        string server, string code, string reason)
    {
        var update = await Check(server, ReleaseBuild);
        var notice = Notice(update);
        Assert.Equal(code, notice.Value<string>("code"));
        Assert.Equal(new[] { "server" }, notice["components"]!.Values<string>());
        Assert.Equal(new[] { "server" }, notice["protected_components"]!.Values<string>());
        Assert.Equal(reason, (string?)notice["protected_component_reasons"]?["server"]);
        Assert.Null(update.Value<bool?>("update_available"));
        AssertNoInstallWorkflow(update, "review");
    }

    [Fact]
    public async Task Older_component_with_an_unverified_peer_reports_the_new_version_but_keeps_the_peer()
    {
        var update = await Check("0.2.2+development", "0.2.1");
        var notice = Notice(update);
        Assert.Equal("NEW_RELEASE_AVAILABLE", notice.Value<string>("code"));
        Assert.Equal(new[] { "plugin" }, notice["components"]!.Values<string>());
        Assert.Equal(new[] { "server" }, notice["protected_components"]!.Values<string>());
        AssertNoInstallWorkflow(update, "review");
    }

    [Fact]
    public async Task Unobserved_plugin_is_distinct_from_an_observed_plugin_with_missing_revision()
    {
        var unobserved = await Check(ReleaseBuild, null);
        Assert.False(unobserved["components"]!["plugin"]!.Value<bool>("identity_observed"));
        Assert.Equal("runtime_identity_not_observed", (string?)unobserved["components"]?["plugin"]?["comparison_reason"]);
        Assert.Equal(new[] { "plugin" }, Notice(unobserved)["unobserved_components"]!.Values<string>());
        Assert.Empty((JArray)Notice(unobserved)["protected_components"]!);
        Assert.Contains("AutoCAD connection", Notice(unobserved).Value<string>("message"));
        AssertNoInstallWorkflow(unobserved, "review");

        var observed = await Check(ReleaseBuild, "0.2.2+development");
        Assert.True(observed["components"]!["plugin"]!.Value<bool>("identity_observed"));
        Assert.Empty((JArray)Notice(observed)["unobserved_components"]!);
        Assert.Equal(new[] { "plugin" }, Notice(observed)["protected_components"]!.Values<string>());
        Assert.Equal("runtime_revision_unavailable", (string?)Notice(observed)["protected_component_reasons"]?["plugin"]);
        AssertNoInstallWorkflow(observed, "review");
    }

    [Fact]
    public async Task Newer_release_without_plugin_observation_is_discovered_but_does_not_allow_installation()
    {
        var update = await Check("0.2.1", null);
        var notice = Notice(update);
        Assert.Equal("NEW_RELEASE_AVAILABLE", notice.Value<string>("code"));
        Assert.Equal(new[] { "server" }, notice["components"]!.Values<string>());
        Assert.Equal(new[] { "plugin" }, notice["unobserved_components"]!.Values<string>());
        Assert.Empty((JArray)notice["protected_components"]!);
        AssertNoInstallWorkflow(update, "review");
    }

    [Theory]
    [InlineData("http", "http_403")]
    [InlineData("network", "network_unavailable")]
    [InlineData("timeout", "timeout")]
    [InlineData("metadata", "invalid_release_metadata")]
    public async Task External_lookup_failure_is_not_reported_as_a_local_runtime_fault(string failure, string reason)
    {
        var handler = new ReleaseUpdateCheckTests.Handler();
        if (failure == "http") handler.Status = HttpStatusCode.Forbidden;
        if (failure == "network") handler.BeforeResponse = _ => throw new HttpRequestException("offline");
        if (failure == "timeout") handler.BeforeResponse = ct => Task.Delay(Timeout.Infinite, ct);
        if (failure == "metadata") handler.Body = "not json";
        var update = await Check(ReleaseBuild, null, handler);
        var notice = Notice(update);
        Assert.Equal("UPDATE_CHECK_UNAVAILABLE", notice.Value<string>("code"));
        Assert.Equal("report_release_check_unavailable", notice.Value<string>("agent_action"));
        Assert.Equal(reason, notice.Value<string>("lookup_reason"));
        Assert.Contains(reason, notice.Value<string>("message"));
        Assert.Contains("runtime identities are unchanged", notice.Value<string>("message"));
        Assert.Empty((JArray)notice["components"]!);
        Assert.Empty((JArray)notice["protected_components"]!);
        Assert.Empty((JArray)notice["unobserved_components"]!);
        AssertNoInstallWorkflow(update, "retry_check");
    }

    [Theory]
    [InlineData("0.2.1", "NEW_RELEASE_AVAILABLE")]
    [InlineData(ReleaseBuild, "RELEASE_IDENTITY_UNVERIFIED")]
    public async Task Failed_tag_resolution_keeps_version_facts_without_claiming_a_local_identity_fault(
        string version, string code)
    {
        var update = await Check(version, version, new ReleaseUpdateCheckTests.Handler { RefStatus = HttpStatusCode.Forbidden });
        var notice = Notice(update);
        Assert.Equal("checked", update.Value<string>("status"));
        Assert.Equal(code, notice.Value<string>("code"));
        Assert.Equal("http_403", notice.Value<string>("lookup_reason"));
        Assert.Empty((JArray)notice["protected_components"]!);
        Assert.Empty((JArray)notice["unobserved_components"]!);
        Assert.Equal("release_revision_unavailable", (string?)update["components"]?["server"]?["comparison_reason"]);
        AssertNoInstallWorkflow(update, "review");
    }

    [Fact]
    public async Task Matching_release_pair_has_no_notification_or_installation_workflow()
    {
        var update = await Check(ReleaseBuild, ReleaseBuild);
        Assert.Equal(JTokenType.Null, update["notification"]!.Type);
        Assert.False(update.Value<bool>("update_available"));
        AssertNoInstallWorkflow(update, "none");
    }

    [Fact]
    public void Offline_guidance_only_invites_a_permitted_check_and_does_not_request_installation_authorization()
    {
        var update = ReleaseUpdateCheck.NotChecked(ReleaseUpdateCheckTests.Identity(ReleaseBuild), null);
        Assert.Equal(JTokenType.Null, update["notification"]!.Type);
        AssertNoInstallWorkflow(update, "check_only");
        Assert.Contains("dwg_health(check_updates=true)", HealthRuntimeInfo.UpdateGuidance(update)["steps"]!.ToString());
    }
}
