using System;

namespace Bimwright.Dwg.Plugin
{
    public enum TransportKind
    {
        Tcp,
        Pipe
    }

    public interface ITransportServer
    {
        int? Port { get; }
        string PipeName { get; }
        string AuthToken { get; }
        bool IsRunning { get; }
        bool IsClientConnected { get; }
        DateTime? LastCommandTime { get; }
        TransportKind Kind { get; }
        void Start();
        void Stop();
    }
}
