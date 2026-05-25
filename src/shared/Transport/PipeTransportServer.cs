using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using Newtonsoft.Json;

namespace Bimwright.Dwg.Plugin
{
    public class PipeTransportServer : ITransportServer
    {
        private readonly string _target;
        private readonly string _pipeName;
        private volatile bool _running;
        private Thread _acceptThread;
        private CommandDispatcher _dispatcher;
        private NamedPipeServerStream _activeServer;

        public PipeTransportServer(string target, string pipeName = null)
        {
            _target = string.IsNullOrWhiteSpace(target) ? "2025" : target;
            _pipeName = string.IsNullOrWhiteSpace(pipeName)
                ? "BimwrightDwg-" + _target + "-" + Process.GetCurrentProcess().Id
                : pipeName;
        }

        public int? Port => null;
        public string PipeName => _pipeName;
        public string AuthToken { get; private set; }
        public bool IsRunning => _running;
        public bool IsClientConnected
        {
            get
            {
                try { return _activeServer?.IsConnected ?? false; }
                catch { return false; }
            }
        }
        public DateTime? LastCommandTime { get; private set; }
        public TransportKind Kind => TransportKind.Pipe;

        public void Start()
        {
            AuthToken = Guid.NewGuid().ToString("N");
            _dispatcher = new CommandDispatcher(AuthToken);
            WriteDiscoveryFile();
            _running = true;
            _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "BimwrightDwg-PipeAccept" };
            _acceptThread.Start();
        }

        public void Stop()
        {
            _running = false;
            try { _activeServer?.Dispose(); } catch { }
            DeleteDiscoveryFile();
        }

        private void AcceptLoop()
        {
            while (_running)
            {
                try
                {
                    using (var pipe = new NamedPipeServerStream(
                        _pipeName,
                        PipeDirection.InOut,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous))
                    {
                        _activeServer = pipe;
                        pipe.WaitForConnection();
                        HandleClient(pipe);
                    }
                    _activeServer = null;
                }
                catch
                {
                    _activeServer = null;
                    if (!_running)
                    {
                        return;
                    }
                }
            }
        }

        private void HandleClient(Stream stream)
        {
            using (var reader = new StreamReader(stream, new UTF8Encoding(false)))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true })
            {
                string line;
                while (_running && (line = reader.ReadLine()) != null)
                {
                    LastCommandTime = DateTime.UtcNow;
                    string responseJson;
                    try
                    {
                        responseJson = _dispatcher.Dispatch(line);
                    }
                    catch (Exception ex)
                    {
                        responseJson = CommandDispatcher.ErrorJson(null, $"dispatch error: {ex.Message}");
                    }
                    writer.WriteLine(responseJson);
                }
            }
        }

        private void WriteDiscoveryFile()
        {
            Directory.CreateDirectory(Path.Combine(DiscoveryRoot, "Dwg"));
            var info = new
            {
                schema_version = 2,
                acad_year = int.Parse(_target),
                transport = "pipe",
                port = (int?)null,
                pipe_name = _pipeName,
                auth_token = AuthToken,
                pid = Process.GetCurrentProcess().Id,
                process_name = Process.GetCurrentProcess().ProcessName,
                started_at_utc = DateTime.UtcNow
            };
            File.WriteAllText(JsonDiscoveryPath, JsonConvert.SerializeObject(info, Formatting.Indented));
        }

        private void DeleteDiscoveryFile()
        {
            try { if (File.Exists(JsonDiscoveryPath)) File.Delete(JsonDiscoveryPath); } catch { }
        }

        private string JsonDiscoveryPath => Path.Combine(DiscoveryRoot, "Dwg", "acad-" + _target + ".json");

        private static string DiscoveryRoot =>
            Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Bimwright");
    }
}
