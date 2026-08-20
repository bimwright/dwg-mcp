using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Bimwright.Dwg.Tests
{
    /// <summary>
    /// A real NamedPipeServerStream that accepts connections and replies to
    /// each NDJSON line with a caller-supplied canned response. Mirrors the
    /// real plugin's PipeTransportServer closely enough to exercise
    /// PluginClient.SendPipeAsync end to end, including stream disposal.
    /// </summary>
    public class FakeNamedPipeServer : IDisposable
    {
        private readonly Func<string, string> _responder;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly TaskCompletionSource<bool> _listening =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private NamedPipeServerStream _listeningPipe;
        public string PipeName { get; }

        public FakeNamedPipeServer(Func<string, string> responder)
        {
            _responder = responder;
            PipeName = "bimwright-dwg-test-" + Guid.NewGuid().ToString("N");
            Task.Run(() => AcceptLoop(_cts.Token));
            if (!_listening.Task.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("FakeNamedPipeServer did not start listening.");
        }

        private async Task AcceptLoop(CancellationToken ct)
        {
            var signaled = false;
            while (!ct.IsCancellationRequested)
            {
                NamedPipeServerStream pipe;
                try
                {
                    pipe = await CreateServerPipeAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch
                {
                    return;
                }

                _listeningPipe = pipe;
                if (!signaled)
                {
                    signaled = true;
                    _listening.TrySetResult(true);
                }

                try
                {
                    await pipe.WaitForConnectionAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    pipe.Dispose();
                    return;
                }
                catch
                {
                    pipe.Dispose();
                    return;
                }

                using (pipe)
                using (var reader = new StreamReader(pipe, new UTF8Encoding(false), true, -1, leaveOpen: true))
                using (var writer = new StreamWriter(pipe, new UTF8Encoding(false), -1, leaveOpen: true) { AutoFlush = true })
                {
                    string line;
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        var reply = _responder(line);
                        await writer.WriteLineAsync(reply);
                    }
                }

                _listeningPipe = null;
            }
        }

        private async Task<NamedPipeServerStream> CreateServerPipeAsync(CancellationToken ct)
        {
            // Linux keeps the pipe name busy briefly after the previous instance
            // is disposed; aborting the loop there made sequential PluginClient
            // calls fail on CI while Windows usually reused the name immediately.
            for (var attempt = 0; attempt < 50; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    return new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.InOut,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);
                }
                catch (IOException) when (attempt < 49)
                {
                    await Task.Delay(20, ct);
                }
            }

            throw new IOException("Could not recreate named pipe server '" + PipeName + "'.");
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listeningPipe?.Dispose(); } catch { }
            _cts.Dispose();
        }
    }
}
