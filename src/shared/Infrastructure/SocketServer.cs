using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Bimwright.Dwg.Plugin
{
    public class SocketServer
    {
        private TcpListener _listener;
        private Thread _acceptThread;
        private volatile bool _running;
        private CommandDispatcher _dispatcher;

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
            WriteDiscoveryFile(Port, AuthToken);
            _running = true;
            _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "BimwrightDwg-Accept" };
            _acceptThread.Start();
        }

        public void Stop()
        {
            _running = false;
            try { _listener?.Stop(); } catch { }
            DeleteDiscoveryFile();
        }

        private void AcceptLoop()
        {
            while (_running)
            {
                TcpClient client;
                try { client = _listener.AcceptTcpClient(); }
                catch { return; } // listener stopped
                var t = new Thread(() => HandleClient(client)) { IsBackground = true, Name = "BimwrightDwg-Client" };
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
                // client disconnect / IO error — ignore, loop continues
            }
        }

        private static string DiscoveryFilePath =>
            Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Bimwright\portAcad24.txt");

        private static void WriteDiscoveryFile(int port, string token)
        {
            var dir = Path.GetDirectoryName(DiscoveryFilePath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var content = $"{port}\n{token}\n{Process.GetCurrentProcess().Id}\n";
            File.WriteAllText(DiscoveryFilePath, content);
        }

        private static void DeleteDiscoveryFile()
        {
            try { if (File.Exists(DiscoveryFilePath)) File.Delete(DiscoveryFilePath); } catch { }
        }
    }
}
