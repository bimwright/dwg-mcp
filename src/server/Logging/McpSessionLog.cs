using System;

namespace Bimwright.Dwg.Server.Logging
{
    public sealed class McpSessionLog
    {
        private readonly CallLogWriter _writer;

        public McpSessionLog(string root, string sessionId, Func<bool> enabled = null)
        {
            if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("Session log root is required.", nameof(root));
            sessionId = string.IsNullOrWhiteSpace(sessionId) ? Guid.NewGuid().ToString("N") : sessionId;
            Path = System.IO.Path.Combine(root, sessionId + ".jsonl");
            _writer = new CallLogWriter(Path, enabled);
        }

        public string Path { get; }

        public void Append(McpSessionLogEntry entry)
        {
            _writer.Append(entry ?? new McpSessionLogEntry());
        }
    }
}
