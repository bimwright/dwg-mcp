using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Dwg.Server;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
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
            await Assert.ThrowsAsync<InvalidOperationException>(() => client.SwitchTargetAsync("pid:200"));
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

        [Fact]
        public async Task Call_captured_before_switch_is_refused_before_wire_write()
        {
            using var first = new Host(100);
            using var second = new Host(200);
            var client = Client(first, second);
            await client.SwitchTargetAsync("pid:100");
            var pending = client.Capture("send_code");
            await client.SwitchTargetAsync("pid:200");
            var result = await client.SendBoundAsync("send_code", new { }, "pending", pending);
            Assert.False(result.Ok);
            Assert.Contains("TARGET_CHANGED", result.Error);
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
            var slow = client.SwitchTargetAsync("pid:200");
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
        public async Task Plugin_without_target_protocol_cannot_authorize_writes()
        {
            using var host = new Host(100) { Legacy = true };
            var client = Client(host);
            await Assert.ThrowsAsync<InvalidOperationException>(() => client.SwitchTargetAsync("pid:100"));
            Assert.Equal("none", client.CurrentTarget.Value<string>("state"));
            Assert.True((await client.SendAsync("get_drawing_info", new { })).Ok);
            await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync("send_code", new { }));
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

        private static PluginClient Client(Host first, Host second = null)
            => new PluginClient(() => first.Info, selector => selector == "pid:200" ? second.Info : first.Info, _ => Start);

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
                    }
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
        }

        private sealed class Host : IDisposable
        {
            private readonly FakePluginServer server;
            private int requests;
            internal int Requests => requests;
            internal DiscoveryInfo Info { get; }
            internal JObject LastRequest;
            internal bool VerifiedHealth = true, Legacy;
            internal string DocumentId;
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
                        ["id"] = LastRequest["id"], ["ok"] = true,
                        ["result"] = LastRequest.Value<string>("cmd") == "health" ? new JObject
                        {
                            ["ok"] = VerifiedHealth,
                            ["target"] = Legacy ? null : new JObject
                            {
                                ["protocol"] = 1, ["pid"] = pid, ["process_start_utc"] = Start,
                                ["document_id"] = DocumentId, ["document_title"] = "sample.dwg"
                            }
                        } : new JObject { ["pid"] = pid }
                    }.ToString(Newtonsoft.Json.Formatting.None);
                });
                Info = new DiscoveryInfo { Target = "2024", TargetId = "acad-2024-" + pid, Pid = pid,
                    ProcessStartUtc = Start, Port = server.Port, Token = "token-" + pid };
            }
            public void Dispose() => server.Dispose();
        }
    }
}
