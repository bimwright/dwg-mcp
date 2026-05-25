using System;

namespace Bimwright.Dwg.Server.Memory
{
    public sealed class SessionContext
    {
        public string SessionId { get; set; }
        public string Target { get; set; }
        public string StartedAt { get; set; }

        public static SessionContext Create(string sessionId, string target)
        {
            return new SessionContext
            {
                SessionId = string.IsNullOrWhiteSpace(sessionId) ? Guid.NewGuid().ToString("N") : sessionId,
                Target = target,
                StartedAt = DateTimeOffset.UtcNow.ToString("o")
            };
        }
    }
}
