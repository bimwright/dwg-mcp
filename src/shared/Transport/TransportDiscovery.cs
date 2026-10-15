using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin
{
    /// <summary>Atomic schema-2 descriptors. Cleanup only removes this listener's token.</summary>
    internal sealed class TransportDiscovery
    {
        private readonly string _root, _target, _path, _token;
        private readonly int _pid = Process.GetCurrentProcess().Id;
        private readonly object _gate = new object();
        private bool _closed;

        internal TransportDiscovery(string target, string token)
        {
            _target = target; _token = token;
            _root = Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Bimwright");
            _path = Path.Combine(_root, "dwg-mcp", "acad-" + target + ".json");
        }

        internal void Publish(string transport, int? port, string pipeName)
        {
            WithLock(() =>
            {
                if (_closed) throw new ObjectDisposedException(nameof(TransportDiscovery));
                var info = new
                {
                    schema_version = 2, acad_year = int.Parse(_target, CultureInfo.InvariantCulture),
                    transport, host = "127.0.0.1", port, pipe_name = pipeName,
                    auth_token = _token, pid = _pid, process_name = Process.GetCurrentProcess().ProcessName,
                    started_at_utc = DateTime.UtcNow
                };
                AtomicWrite(_path, JsonConvert.SerializeObject(info, Formatting.Indented));
                if (_target == "2024" && transport == "tcp")
                    AtomicWrite(Path.Combine(_root, "portAcad24.txt"), port + "\n" + _token + "\n" + _pid + "\n");
            });
        }

        internal void Close()
        {
            WithLock(() =>
            {
                _closed = true;
                try
                {
                    BoundedLogFile.RefuseLink(_path);
                    var descriptor = File.Exists(_path) ? JObject.Parse(File.ReadAllText(_path)) : null;
                    if (descriptor?.Value<int?>("pid") == _pid && descriptor.Value<string>("auth_token") == _token)
                        File.Delete(_path);
                }
                catch { }
                if (_target != "2024") return;
                try
                {
                    var legacy = Path.Combine(_root, "portAcad24.txt");
                    BoundedLogFile.RefuseLink(legacy);
                    var lines = File.Exists(legacy) ? File.ReadAllLines(legacy) : new string[0];
                    if (lines.Length >= 3 && lines[1] == _token && lines[2].Trim() == _pid.ToString(CultureInfo.InvariantCulture))
                        File.Delete(legacy);
                }
                catch { }
            });
        }

        private void WithLock(Action action)
            => RunLocked(_path, () => { lock (_gate) action(); });

        internal static void RunLocked(string path, Action action)
        {
            var canonical = Path.GetFullPath(path);
            if (Path.DirectorySeparatorChar == '\\') canonical = canonical.ToUpperInvariant();
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(canonical))).Replace("-", "");
            using (var mutex = new Mutex(false, (Path.DirectorySeparatorChar == '\\' ? @"Global\" : "") + "Bimwright.Dwg.Discovery." + hash))
            {
                var acquired = false;
                try
                {
                    try { acquired = mutex.WaitOne(1000); } catch (AbandonedMutexException) { acquired = true; }
                    if (!acquired) throw new TimeoutException("Timed out waiting for DWG discovery publication.");
                    action();
                }
                finally { if (acquired) mutex.ReleaseMutex(); }
            }
        }

        private static void AtomicWrite(string path, string value)
        {
            BoundedLogFile.RefuseLink(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporary, value, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
