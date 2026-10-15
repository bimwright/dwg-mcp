using System;
using System.IO;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Bimwright.Dwg.Plugin;

namespace Bimwright.Dwg.Server
{
    public class PluginClient
    {
        private readonly Func<DiscoveryInfo> _discoveryProvider;
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
        private static readonly SemaphoreSlim Calls = new SemaphoreSlim(TransportFraming.MaxConnections);

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
            TimeSpan readTimeout;
            try
            {
                var token = cmd != "open_drawing" || parameters == null ? null : parameters as JToken ?? JToken.FromObject(parameters);
                readTimeout = TimeSpan.FromSeconds(DrawingSessionContract.ReadTimeoutSeconds(cmd, token));
            }
            catch (Exception ex) { return Error(requestId, ex.Message); }
            if (!Calls.Wait(0)) return Error(requestId, "plugin connection limit reached; command was not sent.");
            requestId = string.IsNullOrWhiteSpace(requestId) ? Guid.NewGuid().ToString("N") : requestId;
            try
            {
                // Discovery failures propagate so callers can report NO_TARGET.
                var discovery = _discoveryProvider();
                var request = new McpRequest { Id = requestId, Cmd = cmd, Params = parameters, Auth = discovery.Token };
                var json = JsonConvert.SerializeObject(request);
                try
                {
                    return string.Equals(discovery.Transport, "pipe", StringComparison.OrdinalIgnoreCase)
                        ? await SendPipeAsync(discovery, request.Id, json, readTimeout, cmd)
                        : await SendTcpAsync(discovery, request.Id, json, readTimeout, cmd);
                }
                catch (Exception ex)
                {
                    return Error(requestId, $"plugin communication error: {ex.Message}");
                }
            }
            finally { Calls.Release(); }
        }

        private static async Task<McpResponse> SendTcpAsync(DiscoveryInfo discovery, string requestId, string json, TimeSpan readTimeout, string command)
        {
            using var tcp = new TcpClient();
            var host = string.IsNullOrWhiteSpace(discovery.Host) ? "127.0.0.1" : discovery.Host;
            var connectTask = tcp.ConnectAsync(host, discovery.Port ?? 0);
            if (await Task.WhenAny(connectTask, Task.Delay(Timeout)) != connectTask)
                return Error(requestId, "plugin connect timeout");
            await connectTask;

            using var stream = tcp.GetStream();
            return await SendStreamAsync(stream, requestId, json, readTimeout, command);
        }

        private static async Task<McpResponse> SendPipeAsync(DiscoveryInfo discovery, string requestId, string json, TimeSpan readTimeout, string command)
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


            return await SendStreamAsync(pipe, requestId, json, readTimeout, command);
        }

        internal static async Task<McpResponse> SendStreamAsync(Stream stream, string requestId, string json, TimeSpan readTimeout, string command)
        {
            // The socket/pipe owner disposes once. Deadline cancellation closes the actual
            // transport to release outstanding IO; it never retries a possibly delivered mutation.
            await TransportFraming.WriteAsync(stream, json, TransportFraming.RequestBytes, TransportFraming.WriteTimeout);
            string line;
            try
            {
                line = await TransportFraming.ReadAsync(new TransportFraming.BoundedLineReader(stream), TransportFraming.ResponseBytes, readTimeout);
            }
            catch (TimeoutException)
            {
                return Error(requestId, command == "open_drawing"
                    ? "plugin read timeout; opening may still be running in AutoCAD. A timeout does not cancel host work. Inspect AutoCAD state/native dialogs before retrying dwg_open_drawing."
                    : "plugin read timeout; host work may still be running. Inspect its outcome before retrying writes.");
            }
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
        // Pipe transport (AutoCAD 2025-2027) writes "port": null in the discovery
        // file. Deserializing null into a non-nullable int throws inside
        // AuthToken.TryReadJson's try/catch, which is swallowed and reported as
        // "invalid" discovery file, causing the file to be deleted on every call.
        [JsonProperty("port")] public int? Port { get; set; }
        [JsonProperty("pipe_name")] public string PipeName { get; set; }
        [JsonProperty("pipe_path")] public string PipePath { set => PipeName = value; }
        [JsonProperty("auth_token")] public string Token { get; set; }
        [JsonProperty("pid")] public int Pid { get; set; }
        [JsonProperty("process_name")] public string ProcessName { get; set; }
        [JsonProperty("started_at_utc")] public DateTime? StartedAtUtc { get; set; }
        [JsonIgnore] public string DiscoveryFile { get; set; }
    }
}
