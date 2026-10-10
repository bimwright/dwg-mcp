using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Bimwright.Dwg.Server
{
    public static class ServerState
    {
        public static DwgMcpConfig Config { get; set; } = new DwgMcpConfig();

        /// <summary>Tool classes registered for this process. The catalog push reads this list.</summary>
        public static IReadOnlyList<Type> EnabledToolTypes { get; set; } = Array.Empty<Type>();

        public static bool IsReadOnly => Config?.ReadOnlyOrDefault ?? false;

        internal static string ReadOnlyMessage(string toolName)
            => $"Tool '{toolName}' is disabled by read-only mode (--read-only / BIMWRIGHT_DWG_READ_ONLY); document and file changes are blocked.";

        public static string ReadOnlyError(string toolName)
            => JsonConvert.SerializeObject(new
            {
                ok = false,
                error = ReadOnlyMessage(toolName)
            });
    }
}
