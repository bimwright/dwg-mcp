using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;

namespace Bimwright.Dwg.Tests
{
    /// <summary>
    /// A real NamedPipeServerStream that accepts one connection and replies to
    /// each NDJSON line with a caller-supplied canned response. Mirrors the
    /// real plugin's PipeTransportServer closely enough to exercise
    /// PluginClient.SendPipeAsync end to end, including stream disposal.
    /// </summary>
    public class FakeNamedPipeServer : IDisposable
    {
        private readonly Func<string, string> _responder;
        public string PipeName { get; }

        public FakeNamedPipeServer(Func<string, string> responder)
        {
            _responder = responder;
            PipeName = "bimwright-dwg-test-" + Guid.NewGuid().ToString("N");
            Task.Run(AcceptLoop);
        }

        private async Task AcceptLoop()
        {
            while (true)
            {
                NamedPipeServerStream pipe;
                try
                {
                    pipe = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.InOut,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);
                }
                catch
                {
                    return;
                }

                try
                {
                    await pipe.WaitForConnectionAsync();
                }
                catch
                {
                    pipe.Dispose();
                    return;
                }

                using (pipe)
                using (var reader = new StreamReader(pipe, new UTF8Encoding(false)))
                using (var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true })
                {
                    string line;
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        var reply = _responder(line);
                        await writer.WriteLineAsync(reply);
                    }
                }
            }
        }

        public void Dispose()
        {
        }
    }
}
