using System;

namespace Bimwright.Dwg.Server.Memory
{
    public sealed class JournalEntry
    {
        public string Timestamp { get; set; } = DateTimeOffset.UtcNow.ToString("o");
        public string SessionId { get; set; }
        public string EventName { get; set; }
        public string Detail { get; set; }
    }
}
