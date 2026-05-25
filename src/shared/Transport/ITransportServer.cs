namespace Bimwright.Dwg.Plugin
{
    public interface ITransportServer
    {
        int Port { get; }
        string AuthToken { get; }
        bool IsRunning { get; }
        void Start();
        void Stop();
    }
}
