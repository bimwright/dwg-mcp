using System;
using System.IO;
using System.IO.Pipes;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    [CollectionDefinition("Issue22 transport", DisableParallelization = true)]
    public class Issue22TransportCollection { }

    [Collection("Issue22 transport")]
    public sealed class Issue22TransportTests
    {
        [Fact]
        public void Occupied_pipe_Start_fails_before_advertising()
        {
            using var root = new DiscoveryRoot();
            var name = "dwg-issue22-" + Guid.NewGuid().ToString("N");
            using var occupied = new NamedPipeServerStream(name, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            var server = new PipeTransportServer("2027", name);
            try
            {
                Assert.ThrowsAny<IOException>(() => server.Start());
                Assert.False(server.IsRunning);
                Assert.False(File.Exists(root.Descriptor("2027")));
            }
            finally { server.Stop(); }
        }

        [Fact]
        public async Task Tcp_Stop_closes_already_accepted_idle_client()
        {
            using var root = new DiscoveryRoot();
            var server = new TcpTransportServer("2024");
            using var client = new TcpClient();
            try
            {
                server.Start();
                await client.ConnectAsync("127.0.0.1", server.Port.Value);
                using var writer = new StreamWriter(client.GetStream(), new UTF8Encoding(false), 1024, true) { AutoFlush = true };
                using var reader = new StreamReader(client.GetStream(), Encoding.UTF8, true, 1024, true);
                await writer.WriteLineAsync(JsonConvert.SerializeObject(new { id = "first", auth = server.AuthToken, cmd = "ping" }));
                Assert.Contains("pong", await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3)));
                server.Stop();
                var end = reader.ReadLineAsync();
                Assert.Same(end, await Task.WhenAny(end, Task.Delay(500)));
                Assert.Null(await end);
            }
            finally { server.Stop(); }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Stop_cancels_actual_partial_read_and_replacement_serves(bool pipe)
        {
            using var root = new DiscoveryRoot();
            var name = "dwg-issue22-" + Guid.NewGuid().ToString("N");
            ITransportServer server = pipe ? new PipeTransportServer("2027", name) : new TcpTransportServer("2024");
            try
            {
                server.Start();
                using var stream = await Connect(server);
                await stream.WriteAsync(Encoding.UTF8.GetBytes("{"));
                server.Stop();
                await WaitStopped(server);
                ITransportServer replacement = pipe ? new PipeTransportServer("2027", name) : new TcpTransportServer("2024");
                try
                {
                    replacement.Start();
                    Assert.True((await SendPing(replacement)).Value<bool>("ok"));
                    Assert.Throws<InvalidOperationException>(() => server.Start());
                }
                finally { replacement.Stop(); }
            }
            finally { server.Stop(); }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Publish_failure_rolls_back_reserved_listener(bool pipe)
        {
            using var root = new DiscoveryRoot();
            var name = "dwg-issue22-" + Guid.NewGuid().ToString("N");
            var product = System.IO.Path.GetDirectoryName(root.Descriptor("2027"));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(product));
            File.WriteAllText(product, "blocked by fixture");
            ITransportServer server = pipe ? new PipeTransportServer("2027", name) : new TcpTransportServer("2024");
            try
            {
                Assert.ThrowsAny<IOException>(() => server.Start());
                Assert.False(server.IsRunning);
                if (pipe)
                {
                    using var reusable = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                }
                else
                {
                    Assert.Null(server.Port);
                    var listener = (System.Net.Sockets.TcpListener)typeof(TcpTransportServer)
                        .GetField("_listener", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(server);
                    Assert.False(listener.Server.IsBound);
                }
            }
            finally { server.Stop(); }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Old_Stop_preserves_new_listener_descriptor(bool pipe)
        {
            using var root = new DiscoveryRoot();
            var name = "dwg-issue22-" + Guid.NewGuid().ToString("N");
            ITransportServer old = pipe ? new PipeTransportServer("2027", name) : new TcpTransportServer("2024");
            ITransportServer current = pipe ? new PipeTransportServer("2027", name + "-new") : new TcpTransportServer("2024");
            try
            {
                old.Start(); current.Start(); old.Stop();
                Assert.Equal(current.AuthToken, JObject.Parse(File.ReadAllText(root.Descriptor(pipe ? "2027" : "2024"))).Value<string>("auth_token"));
                if (!pipe) Assert.Equal(current.AuthToken, File.ReadAllLines(System.IO.Path.Combine(root.Path, "Bimwright", "portAcad24.txt"))[1]);
                Assert.True((await SendPing(current)).Value<bool>("ok"));
            }
            finally { old.Stop(); current.Stop(); }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Stop_does_not_wait_for_running_dispatch_or_retry_it(bool pipe)
        {
            using var root = new DiscoveryRoot();
            using var release = new ManualResetEventSlim();
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0;
            CommandDispatcher.Override = _ => { Interlocked.Increment(ref calls); started.TrySetResult(true); release.Wait(); return "{}"; };
            ITransportServer server = pipe ? new PipeTransportServer("2027", "dwg-issue22-" + Guid.NewGuid().ToString("N")) : new TcpTransportServer("2024");
            try
            {
                server.Start();
                using var stream = await Connect(server);
                await stream.WriteAsync(Encoding.UTF8.GetBytes("{}\n"));
                await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
                var watch = Stopwatch.StartNew(); server.Stop();
                Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2));
                release.Set();
                await WaitStopped(server);
                Assert.Equal(1, calls);
            }
            finally { release.Set(); CommandDispatcher.Override = null; server.Stop(); }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Stop_releases_actual_connection_blocked_writing_large_response(bool pipe)
        {
            using var root = new DiscoveryRoot();
            var dispatched = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var response = new string('x', 24 * 1024 * 1024);
            CommandDispatcher.Override = _ => { dispatched.TrySetResult(true); return response; };
            ITransportServer server = pipe ? new PipeTransportServer("2027", "dwg-issue22-" + Guid.NewGuid().ToString("N")) : new TcpTransportServer("2024");
            try
            {
                server.Start();
                using var stream = await Connect(server);
                await stream.WriteAsync(Encoding.UTF8.GetBytes("{}\n"));
                await dispatched.Task.WaitAsync(TimeSpan.FromSeconds(3));
                await Task.Delay(150); // Peer never drains a response far larger than its IPC buffers.
                server.Stop();
                await WaitStopped(server);
            }
            finally { CommandDispatcher.Override = null; server.Stop(); }
        }

        [Fact]
        public async Task Tcp_rejects_connections_above_bound_without_creating_more_workers()
        {
            using var root = new DiscoveryRoot();
            var server = new TcpTransportServer("2024");
            var clients = Enumerable.Range(0, TransportFraming.MaxConnections).Select(_ => new TcpClient()).ToArray();
            try
            {
                server.Start();
                foreach (var client in clients) await client.ConnectAsync("127.0.0.1", server.Port.Value);
                Assert.True(SpinWait.SpinUntil(() => server.ActiveConnections == clients.Length, 3000));
                using var excess = new TcpClient();
                await excess.ConnectAsync("127.0.0.1", server.Port.Value);
                var count = await excess.GetStream().ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(3));
                Assert.Equal(0, count);
                Assert.Equal(TransportFraming.MaxConnections, server.ActiveWorkers);
            }
            finally { foreach (var client in clients) client.Dispose(); server.Stop(); }
            await WaitStopped(server);
        }

        [Fact]
        public async Task Byte_framing_keeps_buffered_lines_and_rejects_giant_UTF8_frame()
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("éé\nnext\n"));
            var reader = new TransportFraming.BoundedLineReader(stream);
            Assert.Equal("éé", await TransportFraming.ReadAsync(reader, 4, TimeSpan.FromSeconds(1)));
            Assert.Equal("next", await TransportFraming.ReadAsync(reader, 4, TimeSpan.FromSeconds(1)));
            using var overflow = new MemoryStream(Encoding.UTF8.GetBytes("ééa\n"));
            await Assert.ThrowsAsync<InvalidDataException>(() => TransportFraming.ReadAsync(new TransportFraming.BoundedLineReader(overflow), 4, TimeSpan.FromSeconds(1)));
        }

        [Fact]
        public void Transient_descriptor_parse_or_read_failure_does_not_delete_it()
        {
            using var root = new DiscoveryRoot();
            var path = root.Descriptor("2027");
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            File.WriteAllText(path, "{\"pid\":");
            AuthToken.CleanupLegacyDiscoveryFiles(System.IO.Path.Combine(root.Path, "Bimwright"));
            Assert.True(File.Exists(path));
            using (var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                AuthToken.CleanupLegacyDiscoveryFiles(System.IO.Path.Combine(root.Path, "Bimwright"));
            Assert.True(File.Exists(path));
        }

        [Fact]
        public async Task Atomic_republication_never_hides_a_live_known_descriptor()
        {
            using var root = new DiscoveryRoot();
            var publisher = new TransportDiscovery("2027", "test-owned-token");
            publisher.Publish("pipe", null, "dwg-test-only-not-connected");
            try
            {
                var writer = Task.Run(() => { for (var i = 0; i < 100; i++) publisher.Publish("pipe", null, "dwg-test-only-not-connected"); });
                for (var i = 0; i < 100; i++)
                    Assert.Single(AuthToken.ListAvailable(System.IO.Path.Combine(root.Path, "Bimwright")));
                await writer;
            }
            finally { publisher.Close(); }
        }

        [Fact]
        public async Task Write_deadline_closes_actual_stream_and_does_not_leave_pending_IO()
        {
            using var stream = new BlockedWriteStream();
            await Assert.ThrowsAsync<TimeoutException>(() => TransportFraming.WriteAsync(stream, "frame", 100, TimeSpan.FromMilliseconds(50)));
            Assert.True(stream.Closed);
            Assert.True(stream.Pending.IsCompleted);
        }

        private static async Task<Stream> Connect(ITransportServer server)
        {
            if (server.Kind == TransportKind.Pipe)
            {
                var pipe = new NamedPipeClientStream(".", server.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(3000);
                return pipe;
            }
            var client = new TcpClient { ReceiveBufferSize = 1024 };
            await client.ConnectAsync("127.0.0.1", server.Port.Value);
            return client.GetStream();
        }

        private static async Task<JObject> SendPing(ITransportServer server)
        {
            using var stream = await Connect(server);
            await TransportFraming.WriteAsync(stream, JsonConvert.SerializeObject(new { id = "ping", auth = server.AuthToken }), 1000, TimeSpan.FromSeconds(3));
            return JObject.Parse(await TransportFraming.ReadAsync(new TransportFraming.BoundedLineReader(stream), 1000, TimeSpan.FromSeconds(3)));
        }

        private static async Task WaitStopped(ITransportServer server)
        {
            for (var i = 0; i < 100; i++)
            {
                var busy = server is PipeTransportServer pipe ? pipe.IsAcceptThreadAlive
                    : ((TcpTransportServer)server).IsAcceptThreadAlive || ((TcpTransportServer)server).ActiveWorkers > 0;
                if (!busy) return;
                await Task.Delay(20);
            }
            Assert.Fail("Transport worker did not stop within two seconds.");
        }

        private sealed class BlockedWriteStream : MemoryStream
        {
            private readonly TaskCompletionSource<bool> _pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal Task Pending => _pending.Task;
            internal bool Closed { get; private set; }
            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => _pending.Task;
            protected override void Dispose(bool disposing) { Closed = true; _pending.TrySetException(new IOException("closed")); base.Dispose(disposing); }
        }

        private sealed class DiscoveryRoot : IDisposable
        {
            private readonly string _previous = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            private readonly string _previousDiagnostics = DiagnosticLog.LocalAppDataOverride;
            internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dwg-issue22-" + Guid.NewGuid().ToString("N"));
            internal DiscoveryRoot()
            {
                Directory.CreateDirectory(Path);
                Environment.SetEnvironmentVariable("LOCALAPPDATA", Path);
                DiagnosticLog.LocalAppDataOverride = Path;
            }
            internal string Descriptor(string year) => System.IO.Path.Combine(Path, "Bimwright", "dwg-mcp", "acad-" + year + ".json");
            public void Dispose()
            {
                DiagnosticLog.Flush();
                DiagnosticLog.LocalAppDataOverride = _previousDiagnostics;
                Environment.SetEnvironmentVariable("LOCALAPPDATA", _previous);
                try { Directory.Delete(Path, true); } catch { }
            }
        }
    }
}
