using System;
using System.IO;
using Newtonsoft.Json;

namespace Bimwright.Dwg.Server
{
    internal static class ServerLogger
    {
        private static readonly string LogPath;
        private static readonly object Gate = new object();

        static ServerLogger()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Bimwright");
            Directory.CreateDirectory(dir);
            LogPath = Path.Combine(dir, "dwg-mcp-calls.jsonl");
        }

        public static void LogStart(string requestId, string toolName, string paramsJson)
        {
            try
            {
                WriteEntry(new
                {
                    timestamp = DateTime.UtcNow.ToString("o"),
                    session_id = "server",
                    request_id = requestId,
                    tool = toolName,
                    phase = "start",
                    @params = paramsJson
                });
            }
            catch { }
        }

        public static void LogFinish(string requestId, string toolName, bool success,
                                     long durationMs, string errorMsg = null)
        {
            try
            {
                WriteEntry(new
                {
                    timestamp = DateTime.UtcNow.ToString("o"),
                    session_id = "server",
                    request_id = requestId,
                    tool = toolName,
                    phase = "finish",
                    success,
                    duration_ms = durationMs,
                    error = errorMsg
                });
            }
            catch { }
        }

        private static void WriteEntry(object entry)
        {
            var line = JsonConvert.SerializeObject(entry, Formatting.None);
            lock (Gate)
            {
                File.AppendAllText(LogPath, line + "\n");
            }
        }
    }
}
