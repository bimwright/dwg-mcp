using System;
using System.IO;
using Bimwright.Dwg.Server.Logging;

namespace Bimwright.Dwg.Server
{
    internal sealed class ServerLogger
    {
        internal static ServerLogger Default { get; } = new ServerLogger(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Bimwright", "dwg-mcp", "dwg-mcp-calls.jsonl"));

        private readonly CallLogWriter _writer;

        internal ServerLogger(string path, Func<bool> enabled = null)
        {
            _writer = new CallLogWriter(path, enabled);
        }

        public void LogStart(string requestId, string toolName, object parameters)
        {
            try
            {
                _writer.Append(new
                {
                    timestamp = DateTime.UtcNow.ToString("o"),
                    session_id = "server",
                    request_id = requestId,
                    tool = toolName,
                    phase = "start",
                    @params = parameters
                });
            }
            catch { } // Diagnostics must not affect tool execution.
        }

        public void LogFinish(string requestId, string toolName, bool success,
                              long durationMs, string errorMsg = null)
        {
            try
            {
                // Compiler/runtime errors can echo arbitrary source text, including
                // non-secret literals. No code-related error body belongs in a log.
                var codeRelated = SendCodePolicy.IsSendCode(toolName)
                    || string.Equals(toolName, "batch_execute", StringComparison.OrdinalIgnoreCase);
                _writer.Append(new
                {
                    timestamp = DateTime.UtcNow.ToString("o"),
                    session_id = "server",
                    request_id = requestId,
                    tool = toolName,
                    phase = "finish",
                    success,
                    duration_ms = durationMs,
                    error = codeRelated && errorMsg != null ? "<code error omitted>" : errorMsg
                });
            }
            catch { }
        }
    }
}
