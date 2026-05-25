using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;

namespace Bimwright.Dwg.Plugin
{
    public class TcpTransportServer : ITransportServer
    {
        private readonly string _target;
        private TcpListener _listener;
        private Thread _acceptThread;
        private volatile bool _running;
        private CommandDispatcher _dispatcher;

        public TcpTransportServer(string target = "2024")
        {
            _target = string.IsNullOrWhiteSpace(target) ? "2024" : target;
        }

        public int Port { get; private set; }
        public string AuthToken { get; private set; }
        public bool IsRunning => _running;

        public void Start()
        {
            AuthToken = Guid.NewGuid().ToString("N");
            _dispatcher = new CommandDispatcher(AuthToken);

            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            WriteDiscoveryFiles();
            _running = true;
            _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "BimwrightDwg-TcpAccept" };
            _acceptThread.Start();
        }

        public void Stop()
        {
            _running = false;
            try { _listener?.Stop(); } catch { }
            DeleteDiscoveryFiles();
        }

        private void AcceptLoop()
        {
            while (_running)
            {
                TcpClient client;
                try { client = _listener.AcceptTcpClient(); }
                catch { return; }
                var t = new Thread(() => HandleClient(client)) { IsBackground = true, Name = "BimwrightDwg-TcpClient" };
                t.Start();
            }
        }

        private void HandleClient(TcpClient client)
        {
            try
            {
                using (client)
                using (var stream = client.GetStream())
                using (var reader = new StreamReader(stream, new UTF8Encoding(false)))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true })
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
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
            catch
            {
                // Client disconnect or IO error; accept loop continues.
            }
        }

        private void WriteDiscoveryFiles()
        {
            var root = DiscoveryRoot;
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, "Dwg"));

            if (_target == "2024")
            {
                File.WriteAllText(LegacyDiscoveryPath, $"{Port}\n{AuthToken}\n{Process.GetCurrentProcess().Id}\n");
            }

            var info = new
            {
                schema_version = 2,
                target = _target,
                version = _target,
                transport = "tcp",
                host = "127.0.0.1",
                port = Port,
                auth_token = AuthToken,
                pid = Process.GetCurrentProcess().Id,
                process_name = Process.GetCurrentProcess().ProcessName,
                started_at_utc = DateTime.UtcNow
            };
            File.WriteAllText(JsonDiscoveryPath, JsonConvert.SerializeObject(info, Formatting.Indented));
        }

        private void DeleteDiscoveryFiles()
        {
            try { if (File.Exists(JsonDiscoveryPath)) File.Delete(JsonDiscoveryPath); } catch { }
            if (_target == "2024")
            {
                try { if (File.Exists(LegacyDiscoveryPath)) File.Delete(LegacyDiscoveryPath); } catch { }
            }
        }

        private string JsonDiscoveryPath => Path.Combine(DiscoveryRoot, "Dwg", "acad-" + _target + ".json");

        private static string DiscoveryRoot =>
            Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Bimwright");

        private static string LegacyDiscoveryPath =>
            Path.Combine(DiscoveryRoot, "portAcad24.txt");
    }
}
