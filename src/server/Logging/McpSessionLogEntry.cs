using System;

namespace Bimwright.Dwg.Server.Logging
{
    public sealed class McpSessionLogEntry
    {
        public string Timestamp { get; set; } = DateTimeOffset.UtcNow.ToString("o");
        public string Tool { get; set; }
        public bool Success { get; set; }
        public long DurationMs { get; set; }
        public string Error { get; set; }
    }
}
