using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Bimwright.Dwg.Tests
{
    /// <summary>
    /// A real TcpListener on 127.0.0.1 that accepts one connection and replies
    /// to each NDJSON line with a caller-supplied canned response.
    /// </summary>
    public class FakePluginServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly Func<string, string> _responder;
        public int Port { get; }

        public FakePluginServer(Func<string, string> responder)
        {
            _responder = responder;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Task.Run(AcceptLoop);
        }

        private async Task AcceptLoop()
        {
            while (true)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(); }
                catch { return; }

                _ = Task.Run(async () =>
                {
                    using (client)
                    using (var stream = client.GetStream())
                    using (var reader = new StreamReader(stream, new UTF8Encoding(false)))
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true })
                    {
                        string line;
                        while ((line = await reader.ReadLineAsync()) != null)
                        {
                            var reply = _responder(line);
                            await writer.WriteLineAsync(reply);
                        }
                    }
                });
            }
        }

        public void Dispose() => _listener.Stop();
    }
}
