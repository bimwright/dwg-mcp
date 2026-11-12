using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Dwg.Server;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    [Collection("Script handlers")]
    public class TargetBindingTests
    {
        private static readonly DateTime Start = new DateTime(2026, 10, 18, 1, 0, 0, DateTimeKind.Utc);

        [Theory]
        [InlineData("create_line")]
        [InlineData("send_code")]
        [InlineData("batch_execute")]
        [InlineData("run_baked_tool")]
        public async Task Implicit_binding_cannot_write(string command)
        {
            using var host = new Host(100);
            var client = Client(host);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync(command, new { }));
            Assert.Contains("TARGET_UNCONFIRMED", error.Message);
            Assert.Equal(0, host.Requests);
        }

        [Theory]
        [InlineData("create_line")]
        [InlineData("send_code")]
        [InlineData("batch_execute")]
        [InlineData("open_drawing")]
        public async Task Verified_binding_attaches_process_and_document_to_every_request(string command)
        {
            using var host = new Host(100);
            var client = Client(host);
            await client.SwitchTargetAsync("pid:100");
            var result = await client.SendAsync(command, new { });
            Assert.True(result.Ok, result.Error);
            var expected = host.LastRequest["target"];
            Assert.Equal(100, expected.Value<int>("pid"));
            Assert.Equal(Start, expected.Value<DateTime>("process_start_utc"));
            Assert.Equal("doc-100", expected.Value<string>("document_id"));
            Assert.True(expected.Value<bool>("selection_confirmed"));
        }

        [Fact]
        public async Task Failed_verification_preserves_previous_binding()
        {
            using var first = new Host(100);
            using var second = new Host(200) { VerifiedHealth = false };
            var client = Client(first, second);
            await client.SwitchTargetAsync("pid:100");
            var before = client.CurrentTarget;
            var failed = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SwitchTargetAsync("pid:200", rebind: true));
            Assert.Contains("TARGET_VERIFICATION_FAILED", failed.Message);
            Assert.Equal(1, second.Requests); // Reach the health failure, not the departure policy.
            Assert.True(JToken.DeepEquals(before, client.CurrentTarget));
            await Assert.ThrowsAsync<InvalidOperationException>(() => client.SwitchTargetAsync("pid:100", expectedDocumentId: "other-document"));
            Assert.True(JToken.DeepEquals(before, client.CurrentTarget));
            Assert.True((await client.SendAsync("send_code", new { })).Ok);
        }

        [Fact]
        public async Task Unverified_switch_permits_reads_but_never_writes()
        {
            using var host = new Host(100);
            var client = Client(host);
            await client.SwitchTargetAsync("pid:100", verify: false);
            Assert.True((await client.SendAsync("get_drawing_info", new { })).Ok);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync("send_code", new { }));
            Assert.Contains("TARGET_UNCONFIRMED", error.Message);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Lost_or_reused_PID_does_not_select_another_instance(bool reuse)
        {
            using var first = new Host(100);
            using var second = new Host(200);
            DateTime? actual = Start;
            var selected = first.Info;
            var client = new PluginClient(() => selected, _ => selected, _ => actual);
            await client.SwitchTargetAsync("pid:100");
            var requests = first.Requests;
            selected = second.Info;
            actual = reuse ? Start.AddMinutes(1) : null;
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync("get_drawing_info", new { }));
            Assert.Contains(reuse ? "TARGET_CHANGED" : "TARGET_LOST", error.Message);
            Assert.Equal("lost", client.CurrentTarget.Value<string>("state"));
            actual = Start; // Even an apparently recovered PID requires explicit reselection.
            await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync("get_drawing_info", new { }));
            Assert.Equal(requests, first.Requests);
            Assert.Equal(0, second.Requests);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Routing_failure_after_capture_preserves_health_error_prefix_without_sending(bool reuse)
        {
            using var host = new Host(100);
            DateTime? actualStart = Start;
            var client = new PluginClient(() => host.Info, _ => host.Info, _ => actualStart);
            await client.SwitchTargetAsync("pid:100");
            var pending = client.Capture("health");
            actualStart = reuse ? Start.AddMinutes(1) : null;
            var result = await client.SendBoundAsync("health", new { }, "pending-health", pending);
            Assert.False(result.Ok);
            Assert.StartsWith(reuse ? "TARGET_CHANGED:" : "TARGET_LOST:", result.Error);
            Assert.DoesNotContain("plugin communication error:", result.Error);
            Assert.Equal(1, host.Requests); // Only initial verification; no post-capture health bytes arrived.
        }

        [Fact]
        public async Task Call_captured_before_switch_is_refused_before_wire_write()
        {
            using var first = new Host(100);
            using var second = new Host(200);
            var client = Client(first, second);
            await client.SwitchTargetAsync("pid:100");
            var pending = client.Capture("send_code");
            await client.SwitchTargetAsync("pid:200", rebind: true);
            var result = await client.SendBoundAsync("send_code", new { }, "pending", pending);
            Assert.False(result.Ok);
            Assert.StartsWith("TARGET_CHANGED:", result.Error);
            Assert.Contains("Current binding: pid:200", result.Error);
            Assert.Contains("document_id=\"doc-200\"", result.Error);
            Assert.Contains("target=\"pid:200\"", result.Error);
            Assert.DoesNotContain("pid:100", result.Error);
            Assert.Equal(1, first.Requests); // Health only: the stale command never arrived.
            Assert.Equal(1, second.Requests);
        }

        [Fact]
        public async Task Slow_verification_cannot_overwrite_a_newer_switch()
        {
            using var first = new Host(100);
            using var second = new Host(200);
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var client = Client(first, second);
            await client.SwitchTargetAsync("pid:100");
            second.BeforeReply = () => { entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(5))); };
            var slow = client.SwitchTargetAsync("pid:200", rebind: true);
            try
            {
                Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
                await client.SwitchTargetAsync("pid:100");
            }
            finally { release.Set(); }
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => slow);
            Assert.Contains("TARGET_CHANGED", error.Message);
            Assert.Equal(100, client.CurrentTarget.Value<int>("pid"));
        }

        [Fact]
        public async Task Slow_resolver_cannot_overwrite_a_newer_switch_or_send_its_health_probe()
        {
            using var first = new Host(100);
            using var second = new Host(200);
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var client = new PluginClient(() => first.Info, selector =>
            {
                if (selector != "pid:200") return first.Info;
                entered.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
                return second.Info;
            }, _ => Start);
            await client.SwitchTargetAsync("pid:100");
            var slow = Task.Run(() => client.SwitchTargetAsync("pid:200", rebind: true));
            JObject newer = null;
            try
            {
                Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
                await client.SwitchTargetAsync("pid:100");
                newer = client.CurrentTarget;
            }
            finally { release.Set(); }
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => slow);
            Assert.Contains("TARGET_CHANGED", error.Message);
            Assert.True(JToken.DeepEquals(newer, client.CurrentTarget));
            Assert.Equal(2, first.Requests);
            Assert.Equal(0, second.Requests);
        }

        [Fact]
        public async Task Plugin_without_target_protocol_cannot_authorize_writes()
        {
            using var host = new Host(100) { Legacy = true };
            var client = Client(host);
            var pairing = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SwitchTargetAsync("pid:100"));
            Assert.StartsWith("PAIRING_REQUIRED:", pairing.Message);
            Assert.Equal("none", client.CurrentTarget.Value<string>("state"));
            Assert.True((await client.SendAsync("get_drawing_info", new { })).Ok);
            await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync("send_code", new { }));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Missing_guard_capability_refuses_pairing_and_preserves_confirmed_binding(bool healthUnsupported)
        {
            using var first = new Host(100);
            using var legacy = new Host(200) { Legacy = true, Refusal = healthUnsupported ? "unknown command: health" : null };
            var client = Client(first, legacy);
            await client.SwitchTargetAsync("pid:100");
            var before = client.CurrentTarget;
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SwitchTargetAsync("pid:200", rebind: true));
            Assert.StartsWith("PAIRING_REQUIRED:", error.Message);
            Assert.True(JToken.DeepEquals(before, client.CurrentTarget));
            Assert.True((await client.SendAsync("send_code", new { })).Ok);
            Assert.Equal(1, legacy.Requests);
        }

        [Fact]
        public async Task No_document_allows_open_but_blocks_document_writes()
        {
            using var host = new Host(100) { DocumentId = null };
            var client = Client(host);
            await client.SwitchTargetAsync("pid:100");
            Assert.True((await client.SendAsync("open_drawing", new { })).Ok);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync("create_line", new { }));
            Assert.Contains("DOCUMENT_UNCONFIRMED", error.Message);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("2024")]
        public async Task First_unique_auto_or_year_selection_is_still_allowed(string selector)
        {
            using var host = new Host(100);
            var client = Client(host);
            await client.SwitchTargetAsync(selector);
            Assert.Equal(100, client.CurrentTarget.Value<int>("pid"));
            Assert.True(client.CurrentTarget.Value<bool>("selection_confirmed"));
            Assert.Equal("doc-100", client.CurrentTarget.Value<string>("document_id"));
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData("2024", false)]
        [InlineData(null, true)]
        [InlineData("2024", true)]
        public async Task Auto_or_year_cannot_move_an_existing_binding_even_with_rebind(string selector, bool rebind)
        {
            using var first = new Host(100);
            using var second = new Host(200);
            var selected = first.Info;
            var client = new PluginClient(() => selected, _ => selected, _ => Start);
            await client.SwitchTargetAsync("pid:100");
            var before = client.CurrentTarget;
            selected = second.Info;
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SwitchTargetAsync(selector, rebind: rebind));
            Assert.Contains("TARGET_REBIND_REQUIRED", error.Message);
            Assert.Contains("Current binding: pid:100", error.Message);
            Assert.Contains("document_id=\"doc-100\"", error.Message);
            Assert.Contains("target=\"pid:200\", verify=true, rebind=true", error.Message);
            Assert.True(JToken.DeepEquals(before, client.CurrentTarget));
            Assert.Equal(0, second.Requests); // Refuse before the candidate's health probe.
        }

        [Theory]
        [InlineData("pid:200")]
        [InlineData("id:acad-2024-200")]
        public async Task Explicit_departure_from_live_confirmed_instance_requires_rebind_before_probe(string selector)
        {
            using var first = new Host(100);
            using var second = new Host(200);
            var client = Client(first, second);
            await client.SwitchTargetAsync("pid:100");
            var before = client.CurrentTarget;
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SwitchTargetAsync(selector));
            Assert.Contains("TARGET_REBIND_REQUIRED", error.Message);
            Assert.Contains("pid:100", error.Message);
            Assert.Contains("document_id=\"doc-100\"", error.Message);
            Assert.Contains("target=\"" + selector + "\", verify=true, rebind=true", error.Message);
            Assert.True(JToken.DeepEquals(before, client.CurrentTarget));
            Assert.Equal(0, second.Requests);
            await client.SwitchTargetAsync(selector, rebind: true);
            Assert.Equal(200, client.CurrentTarget.Value<int>("pid"));
            Assert.Equal("doc-200", client.CurrentTarget.Value<string>("document_id"));
            Assert.Equal(1, second.Requests);
        }

        [Theory]
        [InlineData("pid:200")]
        [InlineData("id:acad-2024-200")]
        public async Task Explicit_switch_from_unconfirmed_binding_does_not_inherit_its_document_pin(string selector)
        {
            using var first = new Host(100);
            using var second = new Host(200);
            var client = Client(first, second);
            await client.SwitchTargetAsync("pid:100", verify: false, expectedDocumentId: "first-only");
            await client.SwitchTargetAsync(selector);
            Assert.Equal(200, client.CurrentTarget.Value<int>("pid"));
            Assert.Equal("doc-200", client.CurrentTarget.Value<string>("document_id"));
            Assert.Null(client.CurrentTarget.Value<string>("expected_document_id"));
            Assert.Equal(0, first.Requests);
            Assert.Equal(1, second.Requests);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Explicit_switch_from_lost_or_reused_process_needs_no_live_departure_override(bool reuse)
        {
            using var first = new Host(100);
            using var second = new Host(200);
            DateTime? firstStart = Start;
            var client = new PluginClient(() => first.Info,
                selector => selector == "pid:200" ? second.Info : first.Info,
                pid => pid == 100 ? firstStart : Start);
            await client.SwitchTargetAsync("pid:100");
            firstStart = reuse ? Start.AddMinutes(1) : null;
            Assert.Equal("lost", client.CurrentTarget.Value<string>("state"));
            await client.SwitchTargetAsync("pid:200");
            Assert.Equal("bound", client.CurrentTarget.Value<string>("state"));
            Assert.Equal("doc-200", client.CurrentTarget.Value<string>("document_id"));
            Assert.Equal(1, first.Requests);
            Assert.Equal(1, second.Requests);
        }

        [Fact]
        public async Task Same_process_unverified_reselection_preserves_pin_but_demotes_write_authority()
        {
            using var host = new Host(100);
            var client = Client(host);
            await client.SwitchTargetAsync("pid:100");
            host.DocumentId = "different-drawing-lifetime";
            await client.SwitchTargetAsync("pid:100", verify: false);
            await client.SwitchTargetAsync("pid:100", verify: false);
            var pinned = client.CurrentTarget;
            Assert.False(pinned.Value<bool>("selection_confirmed"));
            Assert.Null(pinned.Value<string>("document_id"));
            Assert.Equal("doc-100", pinned.Value<string>("expected_document_id"));
            var write = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync("send_code", new { }));
            Assert.Contains("TARGET_UNCONFIRMED", write.Message);
            Assert.Equal(1, host.Requests); // Unverified switches and refused write sent nothing.
            var changed = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SwitchTargetAsync("pid:100"));
            Assert.Contains("DOCUMENT_CHANGED", changed.Message);
            Assert.True(JToken.DeepEquals(pinned, client.CurrentTarget));
            host.DocumentId = "doc-100";
            await client.SwitchTargetAsync("pid:100");
            Assert.True(client.CurrentTarget.Value<bool>("selection_confirmed"));
            Assert.Equal("doc-100", client.CurrentTarget.Value<string>("document_id"));
            Assert.Null(client.CurrentTarget.Value<string>("expected_document_id"));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Changed_or_reopened_drawing_needs_explicit_document_intent(bool rebind)
        {
            using var host = new Host(100);
            var client = Client(host);
            await client.SwitchTargetAsync("pid:100");
            var before = client.CurrentTarget;
            host.DocumentId = "reopened-same-path-new-lifetime";
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SwitchTargetAsync("pid:100"));
            Assert.Contains("DOCUMENT_CHANGED", error.Message);
            Assert.Contains("pid:100", error.Message);
            Assert.Contains("expected document_id=\"doc-100\"", error.Message);
            Assert.Contains("observed document_id=\"reopened-same-path-new-lifetime\"", error.Message);
            Assert.Contains("target=\"pid:100\", verify=true, expected_document_id=\"reopened-same-path-new-lifetime\"", error.Message);
            Assert.Contains("Inspect the observed drawing before confirming", error.Message);
            Assert.True(JToken.DeepEquals(before, client.CurrentTarget));
            await client.SwitchTargetAsync("pid:100", expectedDocumentId: rebind ? null : host.DocumentId, rebind: rebind);
            Assert.Equal(host.DocumentId, client.CurrentTarget.Value<string>("document_id"));
            Assert.True(client.CurrentTarget.Value<bool>("selection_confirmed"));
        }

        [Fact]
        public async Task Document_mismatch_recovery_escapes_identities_in_retry_command()
        {
            using var host = new Host(100) { DocumentId = "pinned\"old" };
            var client = Client(host);
            await client.SwitchTargetAsync("pid:100");
            var before = client.CurrentTarget;
            host.DocumentId = "draw\"ing\\path\nlifetime";
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SwitchTargetAsync("pid:100"));
            Assert.StartsWith("DOCUMENT_CHANGED:", error.Message);
            Assert.Contains("expected document_id=\"pinned\\\"old\"", error.Message);
            Assert.Contains("observed document_id=\"draw\\\"ing\\\\path\\nlifetime\"", error.Message);
            Assert.Contains("target=\"pid:100\", verify=true, expected_document_id=\"draw\\\"ing\\\\path\\nlifetime\"", error.Message);
            Assert.DoesNotContain(host.DocumentId, error.Message);
            Assert.True(JToken.DeepEquals(before, client.CurrentTarget));
            Assert.Equal(2, host.Requests); // Verification only; no confirmation or write was accepted.
        }

        [Fact]
        public async Task Cross_instance_document_mismatch_retry_preserves_explicit_rebind_consent()
        {
            using var first = new Host(100);
            using var second = new Host(200);
            var client = Client(first, second);
            await client.SwitchTargetAsync("pid:100");
            var before = client.CurrentTarget;
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                client.SwitchTargetAsync("pid:200", expectedDocumentId: "reviewed-other-drawing", rebind: true));
            Assert.StartsWith("DOCUMENT_CHANGED:", error.Message);
            Assert.Contains("dwg_switch_target(target=\"pid:200\", verify=true, expected_document_id=\"doc-200\", rebind=true)", error.Message);
            Assert.Contains("Inspect the observed drawing before confirming", error.Message);
            Assert.True(JToken.DeepEquals(before, client.CurrentTarget));
            await client.SwitchTargetAsync("pid:200", expectedDocumentId: "doc-200", rebind: true);
            Assert.Equal(200, client.CurrentTarget.Value<int>("pid"));
            Assert.Equal("doc-200", client.CurrentTarget.Value<string>("document_id"));
            Assert.True(client.CurrentTarget.Value<bool>("selection_confirmed"));
            Assert.Equal(1, first.Requests);
            Assert.Equal(2, second.Requests); // Only the failed and successful verification probes.
        }

        [Fact]
        public async Task Explicit_expected_document_survives_unverified_reselection_until_matching_verification()
        {
            using var host = new Host(100);
            var client = Client(host);
            await client.SwitchTargetAsync("pid:100", verify: false, expectedDocumentId: "reviewed-drawing");
            await client.SwitchTargetAsync("pid:100", verify: false);
            var before = client.CurrentTarget;
            Assert.Equal("reviewed-drawing", before.Value<string>("expected_document_id"));
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SwitchTargetAsync("pid:100"));
            Assert.Contains("DOCUMENT_CHANGED", error.Message);
            Assert.True(JToken.DeepEquals(before, client.CurrentTarget));
            host.DocumentId = "reviewed-drawing";
            await client.SwitchTargetAsync("pid:100");
            Assert.Equal("reviewed-drawing", client.CurrentTarget.Value<string>("document_id"));
        }

        [Fact]
        public async Task Explicit_expected_document_takes_precedence_over_rebind()
        {
            using var host = new Host(100);
            var client = Client(host);
            await client.SwitchTargetAsync("pid:100");
            var before = client.CurrentTarget;
            host.DocumentId = "new-drawing";
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                client.SwitchTargetAsync("pid:100", expectedDocumentId: "reviewed-other-drawing", rebind: true));
            Assert.Contains("DOCUMENT_CHANGED", error.Message);
            Assert.True(JToken.DeepEquals(before, client.CurrentTarget));
            await client.SwitchTargetAsync("pid:100", expectedDocumentId: "new-drawing", rebind: true);
            Assert.Equal("new-drawing", client.CurrentTarget.Value<string>("document_id"));
        }

        [Fact]
        public async Task Same_process_unverified_rebind_intentionally_clears_old_document_pin()
        {
            using var host = new Host(100);
            var client = Client(host);
            await client.SwitchTargetAsync("pid:100");
            host.DocumentId = "reviewed-new-drawing";
            await client.SwitchTargetAsync("pid:100", verify: false, rebind: true);
            Assert.False(client.CurrentTarget.Value<bool>("selection_confirmed"));
            Assert.Null(client.CurrentTarget.Value<string>("document_id"));
            Assert.Null(client.CurrentTarget.Value<string>("expected_document_id"));
            await client.SwitchTargetAsync("pid:100");
            Assert.Equal("reviewed-new-drawing", client.CurrentTarget.Value<string>("document_id"));
        }

        [Fact]
        public async Task Same_process_reselection_refuses_previously_captured_write_before_bytes()
        {
            using var host = new Host(100);
            var client = Client(host);
            await client.SwitchTargetAsync("pid:100");
            var old = client.Capture("send_code");
            await client.SwitchTargetAsync("pid:100", verify: false);
            var result = await client.SendBoundAsync("send_code", new { }, "old-write", old);
            Assert.False(result.Ok);
            Assert.StartsWith("TARGET_CHANGED:", result.Error);
            Assert.Equal(1, host.Requests);
            Assert.Equal("doc-100", client.CurrentTarget.Value<string>("expected_document_id"));
        }

        [Fact]
        public async Task Process_identity_changed_during_verification_cannot_replace_previous_binding()
        {
            using var first = new Host(100);
            using var second = new Host(200);
            var secondStart = Start;
            var client = new PluginClient(() => first.Info,
                selector => selector == "pid:200" ? second.Info : first.Info,
                pid => pid == 200 ? secondStart : Start);
            await client.SwitchTargetAsync("pid:100");
            var before = client.CurrentTarget;
            second.BeforeReply = () => secondStart = Start.AddMinutes(1);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SwitchTargetAsync("pid:200", rebind: true));
            Assert.Contains("TARGET_CHANGED", error.Message);
            Assert.True(JToken.DeepEquals(before, client.CurrentTarget));
            Assert.Equal(1, second.Requests);
        }

        [Fact]
        public async Task Commit_rechecks_live_confirmed_departure_policy_after_probe()
        {
            using var first = new Host(100);
            using var second = new Host(200);
            DateTime? firstStart = Start;
            var client = new PluginClient(() => first.Info,
                selector => selector == "pid:200" ? second.Info : first.Info,
                pid => pid == 100 ? firstStart : Start);
            await client.SwitchTargetAsync("pid:100");
            var before = client.CurrentTarget;
            firstStart = null; // Preflight observes an unavailable process probe.
            second.BeforeReply = () => firstStart = Start;
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SwitchTargetAsync("pid:200"));
            Assert.Contains("TARGET_REBIND_REQUIRED", error.Message);
            Assert.True(JToken.DeepEquals(before, client.CurrentTarget));
            Assert.Equal(1, second.Requests); // Commit repeats policy after the read-only health probe.
        }

        [Fact]
        public async Task Reused_same_PID_is_a_different_instance_and_does_not_inherit_document_pin()
        {
            using var host = new Host(100);
            var actualStart = Start;
            var client = new PluginClient(() => host.Info, _ => host.Info, _ => actualStart);
            await client.SwitchTargetAsync("pid:100");
            actualStart = Start.AddMinutes(1);
            host.Info.ProcessStartUtc = actualStart;
            await client.SwitchTargetAsync("pid:100", verify: false);
            Assert.Equal(actualStart, client.CurrentTarget.Value<DateTime>("process_start_utc"));
            Assert.False(client.CurrentTarget.Value<bool>("selection_confirmed"));
            Assert.Null(client.CurrentTarget.Value<string>("expected_document_id"));
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task Recovery_identifies_lost_or_reused_pin_as_historical_without_a_concrete_retry(bool readOnly, bool reuse)
        {
            var previous = ServerState.Config;
            try
            {
                ServerState.Config = new DwgMcpConfig { ReadOnly = readOnly };
                using var host = new Host(100);
                DateTime? actualStart = Start;
                var client = new PluginClient(() => host.Info, _ => host.Info, _ => actualStart);
                await client.SwitchTargetAsync("pid:100");
                actualStart = reuse ? Start.AddMinutes(1) : null;
                var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync("health", new { }));
                Assert.StartsWith(reuse ? "TARGET_CHANGED:" : "TARGET_LOST:", error.Message);
                Assert.Contains("Historical binding identity: pid:100", error.Message);
                Assert.Contains("process_start_utc=" + Start.ToString("O"), error.Message);
                Assert.Contains("document_id=\"doc-100\"", error.Message);
                Assert.Contains("do not reuse this PID without checking its process start", error.Message);
                Assert.Contains("dwg_list_available_targets", error.Message);
                Assert.DoesNotContain("--target pid:100", error.Message);
                Assert.DoesNotContain("target=\"pid:100\"", error.Message);
                if (readOnly) Assert.DoesNotContain("dwg_switch_target", error.Message);
                Assert.Equal(1, host.Requests);
            }
            finally { ServerState.Config = previous; }
        }

        [Fact]
        public async Task Read_only_recovery_uses_current_live_identity_for_stale_call_and_keeps_list_fallback()
        {
            var previous = ServerState.Config;
            try
            {
                ServerState.Config = new DwgMcpConfig { ReadOnly = true };
                using var first = new Host(100);
                using var second = new Host(200);
                var client = Client(first, second);
                await client.SwitchTargetAsync("pid:100");
                var pending = client.Capture("health");
                await client.SwitchTargetAsync("pid:200", rebind: true);
                var result = await client.SendBoundAsync("health", new { }, "stale", pending);
                Assert.StartsWith("TARGET_CHANGED:", result.Error);
                Assert.Contains("Current binding: pid:200", result.Error);
                Assert.Contains("document_id=\"doc-200\"", result.Error);
                Assert.Contains("--read-only --target pid:200", result.Error);
                Assert.Contains("If it no longer matches, call dwg_list_available_targets", result.Error);
                Assert.DoesNotContain("dwg_switch_target", result.Error);
                Assert.DoesNotContain("pid:100", result.Error);
                Assert.Equal(1, first.Requests);
                Assert.Equal(1, second.Requests);
            }
            finally { ServerState.Config = previous; }
        }

        [Fact]
        public async Task Read_only_host_identity_refusal_does_not_present_matching_local_PID_as_a_trusted_retry()
        {
            var previous = ServerState.Config;
            try
            {
                ServerState.Config = new DwgMcpConfig { ReadOnly = true };
                var calls = 0;
                using var host = new FakePluginServer(line =>
                {
                    Interlocked.Increment(ref calls);
                    var request = JObject.Parse(line);
                    return new JObject { ["id"] = request["id"], ["ok"] = false,
                        ["error"] = "TARGET_CHANGED: host identity refusal; command was not started." }.ToString(Newtonsoft.Json.Formatting.None);
                });
                var info = new DiscoveryInfo { Pid = 100, ProcessStartUtc = Start, Port = host.Port };
                var client = new PluginClient(() => info, _ => info, _ => Start);
                var result = await client.SendAsync("health", new { });
                Assert.StartsWith("TARGET_CHANGED: host identity refusal; command was not started.", result.Error);
                Assert.Contains("Historical binding identity: pid:100", result.Error);
                Assert.Contains("process_start_utc=" + Start.ToString("O"), result.Error);
                Assert.Contains("dwg_list_available_targets", result.Error);
                Assert.DoesNotContain("--target pid:100", result.Error);
                Assert.DoesNotContain("dwg_switch_target", result.Error);
                Assert.Equal(1, calls);
            }
            finally { ServerState.Config = previous; }
        }

        [Fact]
        public async Task Read_only_late_host_refusal_uses_newer_binding_for_recovery()
        {
            var previous = ServerState.Config;
            try
            {
                ServerState.Config = new DwgMcpConfig { ReadOnly = true };
                using var first = new Host(100);
                using var second = new Host(200);
                using var entered = new ManualResetEventSlim();
                using var release = new ManualResetEventSlim();
                var client = Client(first, second);
                await client.SwitchTargetAsync("pid:100");
                first.Refusal = "TARGET_CHANGED: old host identity refusal.";
                first.BeforeReply = () => { entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(5))); };
                var pending = client.SendAsync("health", new { });
                try
                {
                    Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
                    await client.SwitchTargetAsync("pid:200", rebind: true);
                }
                finally { release.Set(); }
                var result = await pending;
                Assert.StartsWith("TARGET_CHANGED: old host identity refusal.", result.Error);
                Assert.Contains("Current binding: pid:200", result.Error);
                Assert.Contains("document_id=\"doc-200\"", result.Error);
                Assert.Contains("--read-only --target pid:200", result.Error);
                Assert.DoesNotContain("Historical binding identity", result.Error);
                Assert.DoesNotContain("pid:100", result.Error);
                Assert.Equal(200, client.CurrentTarget.Value<int>("pid"));
                Assert.Equal(2, first.Requests);
                Assert.Equal(1, second.Requests);
            }
            finally { ServerState.Config = previous; }
        }

        [Fact]
        public async Task Year_rebind_refusal_labels_lost_pin_historical_and_names_reviewed_candidate()
        {
            using var first = new Host(100);
            using var second = new Host(200);
            DateTime? firstStart = Start;
            var client = new PluginClient(() => first.Info,
                selector => selector == "pid:100" ? first.Info : second.Info,
                pid => pid == 100 ? firstStart : Start);
            await client.SwitchTargetAsync("pid:100");
            firstStart = null;
            Assert.Equal("lost", client.CurrentTarget.Value<string>("state"));
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SwitchTargetAsync("2024", rebind: true));
            Assert.StartsWith("TARGET_REBIND_REQUIRED:", error.Message);
            Assert.Contains("Historical binding identity: pid:100", error.Message);
            Assert.Contains("document_id=\"doc-100\"", error.Message);
            Assert.Contains("target=\"pid:200\", verify=true, rebind=true", error.Message);
            Assert.Contains("Inspect that intended instance", error.Message);
            Assert.DoesNotContain("target=\"pid:100\"", error.Message);
            Assert.Equal(1, first.Requests);
            Assert.Equal(0, second.Requests);
        }

        [Fact]
        public async Task Late_response_receipt_keeps_executed_target_after_parent_switches_binding()
        {
            using var first = new Host(100) { EmitReceipt = true };
            using var second = new Host(200) { EmitReceipt = true };
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var client = Client(first, second);
            await client.SwitchTargetAsync("pid:100");
            first.BeforeReply = () => { entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(5))); };
            var pending = client.SendAsync("get_drawing_info", new { });
            try
            {
                Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
                await client.SwitchTargetAsync("pid:200", rebind: true);
            }
            finally { release.Set(); }
            var oldResponse = await pending;
            var newResponse = await client.SendAsync("get_drawing_info", new { });
            Assert.Equal(100, oldResponse.Target.Value<int>("pid"));
            Assert.Equal("doc-100", oldResponse.Target.Value<string>("document_id"));
            Assert.Equal(200, newResponse.Target.Value<int>("pid"));
            Assert.Equal(200, client.CurrentTarget.Value<int>("pid"));
        }

        private static PluginClient Client(Host first, Host second = null)
            => new PluginClient(() => first.Info,
                selector => selector == "pid:200" || selector == "id:acad-2024-200" ? second.Info : first.Info, _ => Start);

        [Fact]
        public async Task Named_pipe_transport_carries_the_same_verified_target_context()
        {
            JObject last = null;
            using var pipe = new FakeNamedPipeServer(line =>
            {
                last = JObject.Parse(line);
                return new JObject
                {
                    ["id"] = last["id"], ["ok"] = true,
                    ["result"] = new JObject
                    {
                        ["ok"] = true, ["target"] = new JObject
                        {
                            ["protocol"] = 1, ["pid"] = 100, ["process_start_utc"] = Start,
                            ["document_id"] = "pipe-document"
                        }
                    },
                    ["_target"] = new JObject { ["protocol"] = 1, ["pid"] = 100, ["process_start_utc"] = Start,
                        ["acad_year"] = 2027, ["target_id"] = "acad-2027-100", ["document_id"] = "pipe-document" }
                }.ToString(Newtonsoft.Json.Formatting.None);
            });
            var info = new DiscoveryInfo { Pid = 100, Target = "2027", TargetId = "acad-2027-100",
                ProcessStartUtc = Start, Transport = "pipe", PipeName = pipe.PipeName, Token = "pipe-token" };
            var client = new PluginClient(() => info, _ => info, _ => Start);
            await client.SwitchTargetAsync("pid:100");
            var result = await client.SendAsync("send_code", new { code = "return 42;" });
            Assert.True(result.Ok, result.Error);
            Assert.Equal(100, last["target"].Value<int>("pid"));
            Assert.Equal("pipe-document", last["target"].Value<string>("document_id"));
            Assert.True(last["target"].Value<bool>("selection_confirmed"));
            Assert.Equal("pipe-document", result.Target.Value<string>("document_id"));
        }


        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Enhanced_scripts_probe_capability_before_sending_code(bool supported)
        {
            using var host = new Host(100) { ScriptInputsVersion = supported ? 1 : (int?)null };
            var client = Client(host);
            await client.SwitchTargetAsync("pid:100");
            var result = await client.SendAsync("send_code", new { code = "must-not-run-on-legacy", compile_only = true, script_inputs_version = 1 });
            Assert.Equal(supported, result.Ok);
            Assert.Equal(supported ? 3 : 2, host.Requests);
            Assert.Equal(supported ? "send_code" : "health", host.LastRequest.Value<string>("cmd"));
            if (!supported) Assert.Contains("SCRIPT_INPUTS_UNSUPPORTED", result.Error);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Atomic_batches_probe_capability_before_sending_commands(bool supported)
        {
            using var host = new Host(100) { AtomicBatchVersion = supported ? 1 : (int?)null };
            var client = Client(host);
            await client.SwitchTargetAsync("pid:100");
            var commands = new JArray { new JObject { ["cmd"] = "create_line", ["params"] = new JObject() } };
            var result = await client.SendAsync("batch_execute", new JObject { ["commands"] = commands, ["atomic"] = true });
            Assert.Equal(supported, result.Ok);
            Assert.Equal(supported ? 3 : 2, host.Requests);
            Assert.Equal(supported ? "batch_execute" : "health", host.LastRequest.Value<string>("cmd"));
            if (!supported) Assert.Contains("ATOMIC_BATCH_UNSUPPORTED", result.Error);

            // Non-atomic batches never need the extra probe.
            var before = host.Requests;
            await client.SendAsync("batch_execute", new JObject { ["commands"] = commands });
            Assert.Equal(before + 1, host.Requests);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Task_v1_scripts_probe_task_contract_before_sending_code(bool supported)
        {
            // A 2.2.0 plugin confirms script inputs but not task_v1.
            using var host = new Host(100) { ScriptInputsVersion = 1, ScriptTaskContractVersion = supported ? 1 : (int?)null };
            var client = Client(host);
            await client.SwitchTargetAsync("pid:100");
            var result = await client.SendAsync("send_code", new JObject {
                ["code"] = "return McpTaskResult.Pass(1);", ["script_inputs_version"] = 1, ["result_contract"] = "task_v1" });
            Assert.Equal(supported, result.Ok);
            Assert.Equal(supported ? 3 : 2, host.Requests);
            Assert.Equal(supported ? "send_code" : "health", host.LastRequest.Value<string>("cmd"));
            if (!supported) Assert.Contains("SCRIPT_TASK_CONTRACT_UNSUPPORTED", result.Error);
        }


        private sealed class Host : IDisposable
        {
            private readonly FakePluginServer server;
            private int requests;
            internal int Requests => requests;
            internal DiscoveryInfo Info { get; }
            internal JObject LastRequest;
            internal bool VerifiedHealth = true, Legacy, EmitReceipt;
            internal int? ScriptInputsVersion;
            internal int? AtomicBatchVersion;
            internal int? ScriptTaskContractVersion;
            internal string DocumentId;
            internal string Refusal;
            internal Action BeforeReply;

            internal Host(int pid)
            {
                DocumentId = "doc-" + pid;
                server = new FakePluginServer(line =>
                {
                    Interlocked.Increment(ref requests);
                    LastRequest = JObject.Parse(line);
                    BeforeReply?.Invoke();
                    return new JObject
                    {
                        ["id"] = LastRequest["id"], ["ok"] = Refusal == null, ["error"] = Refusal,
                        ["result"] = LastRequest.Value<string>("cmd") == "health" ? new JObject
                        {
                            ["ok"] = VerifiedHealth,
                            ["script_inputs_version"] = ScriptInputsVersion,
                            ["atomic_batch_version"] = AtomicBatchVersion,
                            ["script_task_contract_version"] = ScriptTaskContractVersion,
                            ["target"] = Legacy ? null : new JObject
                            {
                                ["protocol"] = 1, ["pid"] = pid, ["process_start_utc"] = Start,
                                ["document_id"] = DocumentId, ["document_title"] = "sample.dwg"
                            }
                        } : new JObject { ["pid"] = pid },
                        ["_target"] = EmitReceipt ? new JObject { ["protocol"] = 1, ["pid"] = pid,
                            ["process_start_utc"] = Start, ["target_id"] = "acad-2024-" + pid, ["acad_year"] = 2024,
                            ["document_id"] = DocumentId, ["document_title"] = "sample.dwg" } : null
                    }.ToString(Newtonsoft.Json.Formatting.None);
                });
                Info = new DiscoveryInfo { Target = "2024", TargetId = "acad-2024-" + pid, Pid = pid,
                    ProcessStartUtc = Start, Port = server.Port, Token = "token-" + pid };
            }
            public void Dispose() => server.Dispose();
        }
    }
}
