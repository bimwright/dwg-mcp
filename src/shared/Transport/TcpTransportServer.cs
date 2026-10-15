using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Bimwright.Dwg.Plugin
{
    public class TcpTransportServer : ITransportServer
    {
        private readonly string _target;
        private readonly object _gate = new object();
        private readonly HashSet<TcpClient> _clients = new HashSet<TcpClient>();
        private TcpListener _listener;
        private Thread _acceptThread;
        private volatile bool _running;
        private bool _started, _stopped;
        private int _workers;
        private CommandDispatcher _dispatcher;
        private TransportDiscovery _discovery;

        public TcpTransportServer(string target = "2024")
        { _target = string.IsNullOrWhiteSpace(target) ? "2024" : target; }

        public int? Port { get; private set; }
        public string PipeName => null;
        public string AuthToken { get; private set; }
        public bool IsRunning => _running;
        public bool IsClientConnected { get { lock (_gate) return _clients.Count > 0; } }
        internal int ActiveConnections { get { lock (_gate) return _clients.Count; } }
        internal int ActiveWorkers => Volatile.Read(ref _workers);
        internal bool IsAcceptThreadAlive => _acceptThread?.IsAlive ?? false;
        public DateTime? LastCommandTime { get; private set; }
        public TransportKind Kind => TransportKind.Tcp;
        public string ConnectionInfo => $"TCP:{Port}";

        public void Start()
        {
            lock (_gate)
            {
                if (_started || _stopped) throw new InvalidOperationException("Create a new DWG transport for each listener lifetime.");
                _started = true;
                AuthToken = Guid.NewGuid().ToString("N");
                _dispatcher = new CommandDispatcher(AuthToken, () => _running);
                _discovery = new TransportDiscovery(_target, AuthToken);
                try
                {
                    _listener = new TcpListener(IPAddress.Loopback, 0);
                    _listener.Start(TransportFraming.MaxConnections);
                    Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                    _discovery.Publish("tcp", Port, null);
                    _running = true;
                    _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "BimwrightDwg-TcpAccept" };
                    _acceptThread.Start();
                }
                catch
                {
                    _running = false; _stopped = true;
                    try { _listener?.Stop(); } catch { }
                    Port = null;
                    try { _discovery.Close(); } catch { }
                    throw;
                }
            }
        }

        public void Stop()
        {
            Thread thread;
            lock (_gate)
            {
                _running = false; _stopped = true;
                try { _listener?.Stop(); } catch { }
                foreach (var client in _clients) { try { client.Close(); } catch { } }
                _clients.Clear();
                thread = _acceptThread;
            }
            try { _discovery?.Close(); } catch { }
            if (thread != null && thread != Thread.CurrentThread) thread.Join(250);
        }

        private void AcceptLoop()
        {
            try
            {
                while (_running)
                {
                    var client = _listener.AcceptTcpClient();
                    lock (_gate)
                    {
                        if (!_running || _clients.Count >= TransportFraming.MaxConnections) { client.Close(); continue; }
                        _clients.Add(client);
                        try
                        {
                            Interlocked.Increment(ref _workers);
                            new Thread(() => HandleClient(client)) { IsBackground = true, Name = "BimwrightDwg-TcpClient" }.Start();
                        }
                        catch { Interlocked.Decrement(ref _workers); _clients.Remove(client); client.Close(); throw; }
                    }
                }
            }
            catch (Exception ex)
            {
                if (_running) PluginLog.Debug("TCP listener stopped: " + ex.Message);
            }
            finally { Stop(); }
        }

        private void HandleClient(TcpClient client)
        {
            try
            {
                using (client)
                using (var stream = client.GetStream())
                {
                    var reader = new TransportFraming.BoundedLineReader(stream);
                    while (_running)
                    {
                        var line = TransportFraming.ReadAsync(reader, TransportFraming.RequestBytes, TransportFraming.InputTimeout).GetAwaiter().GetResult();
                        if (line == null) return;
                        lock (_gate) { if (!_running) return; LastCommandTime = DateTime.UtcNow; }
                        string response;
                        try { response = _dispatcher.Dispatch(line); }
                        catch (Exception ex) { response = CommandDispatcher.ErrorJson(null, "dispatch error: " + ex.Message); }
                        if (!_running) return;
                        TransportFraming.WriteAsync(stream, response, TransportFraming.ResponseBytes, TransportFraming.WriteTimeout).GetAwaiter().GetResult();
                    }
                }
            }
            catch { /* Disconnect, malformed/oversized frame, timeout or Stop; no retry of CAD work. */ }
            finally { lock (_gate) _clients.Remove(client); Interlocked.Decrement(ref _workers); }
        }
    }
}
