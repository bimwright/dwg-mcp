using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;

namespace Bimwright.Dwg.Plugin
{
    public class PipeTransportServer : ITransportServer
    {
        private readonly string _target, _pipeName;
        private readonly object _gate = new object();
        private volatile bool _running;
        private bool _started, _stopped;
        private Thread _acceptThread;
        private CommandDispatcher _dispatcher;
        private NamedPipeServerStream _activeServer;
        private TransportDiscovery _discovery;

        public PipeTransportServer(string target, string pipeName = null)
        {
            _target = string.IsNullOrWhiteSpace(target) ? "2025" : target;
            _pipeName = string.IsNullOrWhiteSpace(pipeName)
                ? "BimwrightDwg-" + _target + "-" + Process.GetCurrentProcess().Id : pipeName;
        }

        public int? Port => null;
        public string PipeName => _pipeName;
        public string AuthToken { get; private set; }
        public bool IsRunning => _running;
        internal bool IsAcceptThreadAlive => _acceptThread?.IsAlive ?? false;
        public bool IsClientConnected
        {
            get { lock (_gate) { try { return _activeServer?.IsConnected ?? false; } catch { return false; } } }
        }
        public DateTime? LastCommandTime { get; private set; }
        public TransportKind Kind => TransportKind.Pipe;
        public string ConnectionInfo => $"Pipe:{_pipeName}";

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
                    _activeServer = CreatePipe(); // Reserve synchronously before advertising readiness.
                    _discovery.Publish("pipe", null, _pipeName);
                    _running = true;
                    _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "BimwrightDwg-PipeAccept" };
                    _acceptThread.Start();
                }
                catch
                {
                    _running = false; _stopped = true;
                    try { _activeServer?.Dispose(); } catch { }
                    _activeServer = null;
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
                try { _activeServer?.Dispose(); } catch { }
                _activeServer = null;
                thread = _acceptThread;
            }
            try { _discovery?.Close(); } catch { }
            // Dispatch may be waiting for this UI thread; never join a running CAD operation.
            if (thread != null && thread != Thread.CurrentThread) thread.Join(250);
        }

        private NamedPipeServerStream CreatePipe() => new NamedPipeServerStream(
            _pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

        private void AcceptLoop()
        {
            try
            {
                while (_running)
                {
                    NamedPipeServerStream pipe;
                    lock (_gate)
                    {
                        if (!_running) return;
                        pipe = _activeServer ?? (_activeServer = CreatePipe());
                    }
                    try
                    {
                        // Asynchronous wait/read are aborted by closing the actual stream in Stop.
                        pipe.WaitForConnectionAsync().GetAwaiter().GetResult();
                        HandleClient(pipe);
                    }
                    catch (IOException) { }
                    catch (ObjectDisposedException) { }
                    catch (OperationCanceledException) { }
                    finally
                    {
                        pipe.Dispose();
                        lock (_gate) { if (ReferenceEquals(_activeServer, pipe)) _activeServer = null; }
                    }
                }
            }
            catch (Exception ex)
            {
                PluginLog.Debug("Pipe listener stopped: " + ex.Message);
            }
            finally
            {
                _running = false;
                try { _discovery.Close(); } catch { }
            }
        }

        private void HandleClient(Stream stream)
        {
            var reader = new TransportFraming.BoundedLineReader(stream);
            while (_running)
            {
                string line;
                try { line = TransportFraming.ReadAsync(reader, TransportFraming.RequestBytes, TransportFraming.InputTimeout).GetAwaiter().GetResult(); }
                catch (TimeoutException) { return; }
                catch (InvalidDataException) { return; }
                if (line == null) return;
                lock (_gate) { if (!_running) return; LastCommandTime = DateTime.UtcNow; }
                string response;
                try { response = _dispatcher.Dispatch(line); }
                catch (Exception ex) { response = CommandDispatcher.ErrorJson(null, "dispatch error: " + ex.Message); }
                if (!_running) return;
                try { TransportFraming.WriteAsync(stream, response, TransportFraming.ResponseBytes, TransportFraming.WriteTimeout).GetAwaiter().GetResult(); }
                catch (TimeoutException) { return; }
                catch (InvalidDataException) { return; }
            }
        }
    }
}
