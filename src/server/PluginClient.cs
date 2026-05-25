using System;
using System.IO;
using System.IO.Pipes;
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
            return new PluginClient(() => AuthToken.Resolve(ServerState.Config?.Target));
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
                return string.Equals(discovery.Transport, "pipe", StringComparison.OrdinalIgnoreCase)
                    ? await SendPipeAsync(discovery, request.Id, json)
                    : await SendTcpAsync(discovery, request.Id, json);
            }
            catch (Exception ex)
            {
                return Error(request.Id, $"plugin communication error: {ex.Message}");
            }
        }

        private static async Task<McpResponse> SendTcpAsync(DiscoveryInfo discovery, string requestId, string json)
        {
            using var tcp = new TcpClient();
            var host = string.IsNullOrWhiteSpace(discovery.Host) ? "127.0.0.1" : discovery.Host;
            var connectTask = tcp.ConnectAsync(host, discovery.Port);
            if (await Task.WhenAny(connectTask, Task.Delay(Timeout)) != connectTask)
                return Error(requestId, "plugin connect timeout");
            await connectTask;

            using var stream = tcp.GetStream();
            return await SendStreamAsync(stream, requestId, json);
        }

        private static async Task<McpResponse> SendPipeAsync(DiscoveryInfo discovery, string requestId, string json)
        {
            using var pipe = new NamedPipeClientStream(
                ".",
                discovery.PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            var connectTask = pipe.ConnectAsync((int)Timeout.TotalMilliseconds);
            if (await Task.WhenAny(connectTask, Task.Delay(Timeout)) != connectTask)
                return Error(requestId, "plugin pipe connect timeout");
            await connectTask;

            return await SendStreamAsync(pipe, requestId, json);
        }

        private static async Task<McpResponse> SendStreamAsync(Stream stream, string requestId, string json)
        {
            var utf8 = new UTF8Encoding(false);
            using var writer = new StreamWriter(stream, utf8) { AutoFlush = true };
            using var reader = new StreamReader(stream, utf8);

            await writer.WriteLineAsync(json);
            var readTask = reader.ReadLineAsync();
            if (await Task.WhenAny(readTask, Task.Delay(Timeout)) != readTask)
                return Error(requestId, "plugin read timeout");

            var line = await readTask;
            if (line == null) return Error(requestId, "plugin closed connection");
            return JsonConvert.DeserializeObject<McpResponse>(line);
        }

        private static McpResponse Error(string id, string message) =>
            new McpResponse { Id = id, Ok = false, Error = message };
    }

    public class DiscoveryInfo
    {
        [JsonProperty("schema_version")] public int SchemaVersion { get; set; }
        [JsonProperty("acad_year")] public int AcadYear { get; set; }
        [JsonProperty("target")] public string Target { get; set; }
        [JsonProperty("version")] public string Version { get; set; }
        [JsonProperty("transport")] public string Transport { get; set; } = "tcp";
        [JsonProperty("host")] public string Host { get; set; } = "127.0.0.1";
        [JsonProperty("port")] public int Port { get; set; }
        [JsonProperty("pipe_name")] public string PipeName { get; set; }
        [JsonProperty("pipe_path")] public string PipePath { set => PipeName = value; }
        [JsonProperty("auth_token")] public string Token { get; set; }
        [JsonProperty("pid")] public int Pid { get; set; }
        [JsonProperty("process_name")] public string ProcessName { get; set; }
        [JsonProperty("started_at_utc")] public DateTime? StartedAtUtc { get; set; }
        [JsonIgnore] public string DiscoveryFile { get; set; }
    }
}
