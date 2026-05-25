using System;
using System.IO;
using Newtonsoft.Json;

namespace Bimwright.Dwg.Server.Logging
{
    public sealed class McpSessionLog
    {
        public McpSessionLog(string root, string sessionId)
        {
            if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("Session log root is required.", nameof(root));
            sessionId = string.IsNullOrWhiteSpace(sessionId) ? Guid.NewGuid().ToString("N") : sessionId;
            Directory.CreateDirectory(root);
            Path = System.IO.Path.Combine(root, sessionId + ".jsonl");
        }

        public string Path { get; }

        public void Append(McpSessionLogEntry entry)
        {
            File.AppendAllText(Path, JsonConvert.SerializeObject(entry ?? new McpSessionLogEntry(), Formatting.None) + Environment.NewLine);
        }
    }
}
