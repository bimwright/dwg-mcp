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
        private readonly Func<string, DiscoveryInfo> _select;
        private readonly TargetBinding _binding;
        private readonly SemaphoreSlim _writeGate = new SemaphoreSlim(1, 1);
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
        private static readonly SemaphoreSlim Calls = new SemaphoreSlim(TransportFraming.MaxConnections);

        public PluginClient(Func<DiscoveryInfo> discoveryProvider) : this(discoveryProvider, null, null) { }

        internal PluginClient(Func<DiscoveryInfo> discoveryProvider, Func<string, DiscoveryInfo> select, Func<int, DateTime?> processStart = null)
        {
            _discoveryProvider = discoveryProvider;
            _select = select ?? (_ => discoveryProvider());
            _binding = new TargetBinding(processStart);
        }

        public static PluginClient FromDiscoveryFile()
        {
            return new PluginClient(() => AuthToken.Resolve(ServerState.Config?.Target), target => AuthToken.Resolve(target));
        }

        internal JObject CurrentTarget => _binding.Snapshot();
        internal TargetCall Capture(string command) => _binding.Capture(_discoveryProvider, command);

        internal async Task<JObject> SwitchTargetAsync(string selector, bool verify = true, string expectedDocumentId = null, bool rebind = false)
        {
            selector = AuthToken.NormalizeTarget(selector);
            var before = _binding.Generation;
            var candidate = _binding.Candidate(_select(selector));
            _binding.PrepareSwitch(before, candidate, selector, rebind, expectedDocumentId);
            JObject stamp = null;
            if (verify)
            {
                var id = Guid.NewGuid().ToString("N");
                var response = await SendToAsync(candidate, "health", new JObject(), id, TimeSpan.FromSeconds(20), false);
                var health = response?.Result as JObject;
                stamp = health?["target"] as JObject;
                if (response?.Id == id && (response.Ok && health?.Value<bool?>("ok") == true && stamp?.Value<int?>("protocol") != 1
                    || !response.Ok && response.Error == "unknown command: health"))
                    throw new InvalidOperationException("PAIRING_REQUIRED: gateway and plugin must both support target/document guards. Update them together; previous binding was preserved.");
                if (response?.Id != id || response.Ok != true || health?.Value<bool?>("ok") != true || stamp?.Value<int?>("protocol") != 1)
                    throw new InvalidOperationException("TARGET_VERIFICATION_FAILED: health/document identity was not verified. Update gateway and plugin together if the target protocol is missing. Previous binding was preserved.");
                TargetContract.CheckInstance(stamp, candidate.Discovery.Pid, candidate.Discovery.ProcessStartUtc ?? DateTime.MinValue, false);
            }
            await _writeGate.WaitAsync();
            try { _binding.Commit(before, candidate, stamp, verify, selector, rebind); }
            finally { _writeGate.Release(); }
            return CurrentTarget;
        }

        public Task<McpResponse> SendAsync(string cmd, object parameters, string requestId = null)
            => SendBoundAsync(cmd, parameters, requestId, null);

        internal async Task<McpResponse> SendBoundAsync(string cmd, object parameters, string requestId, TargetCall pinned)
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
                // Capture once before the first await. Discovery never redirects a bound call.
                var call = pinned ?? Capture(cmd);
                try
                {
                    if (cmd == "send_code" && ScriptInputContract.IsEnhanced(parameters as JToken ?? (parameters == null ? null : JToken.FromObject(parameters))))
                    {
                        var healthId = Guid.NewGuid().ToString("N");
                        var health = await SendToAsync(call, "health", new JObject(), healthId, Timeout, true);
                        if (health?.Id != healthId || health.Ok != true ||
                            (health.Result as JObject)?.Value<int?>("script_inputs_version") != ScriptInputContract.Version)
                            return Error(requestId, "SCRIPT_INPUTS_UNSUPPORTED: the plugin did not confirm data/compile_only support. No script was sent or executed. Update gateway and plugin together.");
                    }
                    // Older plugins ignore unknown keys and would run an atomic batch non-atomically.
                    if (cmd == "batch_execute" && BatchContract.IsAtomic(parameters as JToken ?? (parameters == null ? null : JToken.FromObject(parameters))))
                    {
                        var healthId = Guid.NewGuid().ToString("N");
                        var health = await SendToAsync(call, "health", new JObject(), healthId, Timeout, true);
                        if (health?.Id != healthId || health.Ok != true ||
                            (health.Result as JObject)?.Value<int?>("atomic_batch_version") != BatchContract.AtomicVersion)
                            return Error(requestId, "ATOMIC_BATCH_UNSUPPORTED: the plugin did not confirm atomic batch support. No command was sent or executed. Update gateway and plugin together.");
                    }
                    var response = await SendToAsync(call, cmd, parameters, requestId, readTimeout, true);
                    if (ServerState.IsReadOnly && response?.Ok == false
                        && (response.Error?.StartsWith("TARGET_LOST:", StringComparison.Ordinal) == true
                            || response.Error?.StartsWith("TARGET_CHANGED:", StringComparison.Ordinal) == true))
                        response.Error += " " + _binding.Recovery(call);
                    return response;
                }
                catch (InvalidOperationException ex) when (ex.Message.StartsWith("TARGET_", StringComparison.Ordinal)
                    || ex.Message.StartsWith("DOCUMENT_", StringComparison.Ordinal))
                {
                    return Error(requestId, ex.Message);
                }
                catch (Exception ex)
                {
                    return Error(requestId, $"plugin communication error: {ex.Message}");
                }
            }
            finally { Calls.Release(); }
        }

        private Task<McpResponse> SendToAsync(TargetCall call, string cmd, object parameters, string id, TimeSpan readTimeout, bool validate)
        {
            var discovery = call.Discovery;
            var json = JsonConvert.SerializeObject(new McpRequest { Id = id, Cmd = cmd, Params = parameters, Auth = discovery.Token, Target = call.Context });
            Func<Func<Task>, Task> write = async action =>
            {
                await _writeGate.WaitAsync();
                try { if (validate) _binding.Validate(call); await action(); }
                finally { _writeGate.Release(); }
            };
            return string.Equals(discovery.Transport, "pipe", StringComparison.OrdinalIgnoreCase)
                ? SendPipeAsync(discovery, id, json, readTimeout, cmd, write)
                : SendTcpAsync(discovery, id, json, readTimeout, cmd, write);
        }

        private static async Task<McpResponse> SendTcpAsync(DiscoveryInfo discovery, string requestId, string json, TimeSpan readTimeout, string command, Func<Func<Task>, Task> write)
        {
            using var tcp = new TcpClient();
            var host = string.IsNullOrWhiteSpace(discovery.Host) ? "127.0.0.1" : discovery.Host;
            var connectTask = tcp.ConnectAsync(host, discovery.Port ?? 0);
            if (await Task.WhenAny(connectTask, Task.Delay(Timeout)) != connectTask)
                return Error(requestId, "plugin connect timeout");
            await connectTask;

            using var stream = tcp.GetStream();
            return await SendStreamAsync(stream, requestId, json, readTimeout, command, write);
        }

        private static async Task<McpResponse> SendPipeAsync(DiscoveryInfo discovery, string requestId, string json, TimeSpan readTimeout, string command, Func<Func<Task>, Task> write)
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


            return await SendStreamAsync(pipe, requestId, json, readTimeout, command, write);
        }

        internal static async Task<McpResponse> SendStreamAsync(Stream stream, string requestId, string json, TimeSpan readTimeout, string command, Func<Func<Task>, Task> write = null)
        {
            // The socket/pipe owner disposes once. Deadline cancellation closes the actual
            // transport to release outstanding IO; it never retries a possibly delivered mutation.
            Task Write() => TransportFraming.WriteAsync(stream, json, TransportFraming.RequestBytes, TransportFraming.WriteTimeout);
            if (write == null) await Write(); else await write(Write);
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
        [JsonProperty("process_start_utc")] public DateTime? ProcessStartUtc { get; set; }
        [JsonProperty("target_id")] public string TargetId { get; set; }
        [JsonIgnore] public string DiscoveryFile { get; set; }
    }
}
