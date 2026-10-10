using System;
using System.IO;

namespace Bimwright.Dwg.Server.Logging
{
    /// <summary>Lazy, switch-gated JSONL persistence shared by call-log writers.</summary>
    internal sealed class CallLogWriter
    {
        private readonly Func<bool> _enabled;
        private readonly object _gate = new object();

        internal CallLogWriter(string path, Func<bool> enabled = null)
        {
            Path = path ?? throw new ArgumentNullException(nameof(path));
            _enabled = enabled ?? (() => ServerState.Config?.EnableCallLogOrDefault ?? false);
        }

        internal string Path { get; }

        internal void Append(object entry)
        {
            lock (_gate)
            {
                if (!_enabled()) return;
                // Redact before filesystem access; no side effects when disabled.
                var line = CallLogPrivacy.Serialize(entry);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path) ?? ".");
                File.AppendAllText(Path, line + Environment.NewLine);
            }
        }
    }
}
