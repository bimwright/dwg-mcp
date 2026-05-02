using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Bimwright.Dwg.Server
{
    public class PluginClient
    {
        private readonly Func<DiscoveryInfo> _discoveryProvider;
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

        public PluginClient(Func<DiscoveryInfo> discoveryProvider)
        {
            _discoveryProvider = discoveryProvider;
        }

        public static PluginClient FromDiscoveryFile()
        {
            return new PluginClient(ReadDiscovery);
        }

        private static DiscoveryInfo ReadDiscovery()
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Bimwright", "portAcad24.txt");
            if (!File.Exists(path))
                throw new InvalidOperationException(
                    "Plugin not responding — run MCPSTART in AutoCAD (portAcad24.txt missing)");

            var lines = File.ReadAllLines(path);
            if (lines.Length < 3)
                throw new InvalidOperationException(
                    "Discovery file malformed (expected 3 lines: port, token, pid)");

            var port = int.Parse(lines[0].Trim());
            var token = lines[1].Trim();
            var pid = int.Parse(lines[2].Trim());

            // Verify PID alive
            try { Process.GetProcessById(pid); }
            catch (ArgumentException)
            {
                try { File.Delete(path); } catch { }
                throw new InvalidOperationException(
                    "AutoCAD process not running (stale discovery file deleted). Start AutoCAD and load the plugin.");
            }

            return new DiscoveryInfo { Port = port, Token = token };
        }

        public async Task<McpResponse> SendAsync(string cmd, object parameters, string requestId = null)
        {
            var discovery = _discoveryProvider();
            var request = new McpRequest
            {
                Id = string.IsNullOrWhiteSpace(requestId) ? Guid.NewGuid().ToString("N") : requestId,
                Cmd = cmd,
                Params = parameters,
                Auth = discovery.Token
            };
            var json = JsonConvert.SerializeObject(request);

            try
            {
                using var tcp = new TcpClient();
                var connectTask = tcp.ConnectAsync("127.0.0.1", discovery.Port);
                if (await Task.WhenAny(connectTask, Task.Delay(Timeout)) != connectTask)
                    return Error(request.Id, "plugin connect timeout");
                await connectTask;

                using var stream = tcp.GetStream();
                var utf8 = new UTF8Encoding(false);
                using var writer = new StreamWriter(stream, utf8) { AutoFlush = true };
                using var reader = new StreamReader(stream, utf8);

                await writer.WriteLineAsync(json);
                var readTask = reader.ReadLineAsync();
                if (await Task.WhenAny(readTask, Task.Delay(Timeout)) != readTask)
                    return Error(request.Id, "plugin read timeout");

                var line = await readTask;
                if (line == null) return Error(request.Id, "plugin closed connection");
                return JsonConvert.DeserializeObject<McpResponse>(line);
            }
            catch (Exception ex)
            {
                return Error(request.Id, $"plugin communication error: {ex.Message}");
            }
        }

        private static McpResponse Error(string id, string message) =>
            new McpResponse { Id = id, Ok = false, Error = message };
    }

    public class DiscoveryInfo
    {
        public int Port { get; set; }
        public string Token { get; set; }
    }
}
