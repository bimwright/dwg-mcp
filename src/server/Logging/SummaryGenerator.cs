using System.IO;
using Newtonsoft.Json;

namespace Bimwright.Dwg.Server.Logging
{
    public sealed class McpSessionSummary
    {
        public int TotalCalls { get; set; }
        public int Successes { get; set; }
        public int Failures { get; set; }
    }

    public static class SummaryGenerator
    {
        public static McpSessionSummary Generate(string sessionLogPath)
        {
            var summary = new McpSessionSummary();
            if (string.IsNullOrWhiteSpace(sessionLogPath) || !File.Exists(sessionLogPath))
            {
                return summary;
            }

            foreach (var line in File.ReadLines(sessionLogPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var entry = JsonConvert.DeserializeObject<McpSessionLogEntry>(line);
                if (entry == null) continue;
                summary.TotalCalls++;
                if (entry.Success) summary.Successes++;
                else summary.Failures++;
            }

            return summary;
        }
    }
}
