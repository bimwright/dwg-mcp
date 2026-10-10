using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server
{
    internal static class SendCodePolicy
    {
        internal const string Refusal = "send_code is disabled by --disable-send-code or read-only mode. MCPENABLECODE cannot override the server setting.";

        internal static IEnumerable<MethodInfo> ExposedMethods(Type type, bool enabled)
            => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is McpServerToolAttribute tool
                    && (enabled || !IsSendCode(tool.Name)));

        // Enforce at the shared wire boundary as well as tools/list. A batch must
        // be refused before sending any preceding writes, including nested batches.
        internal static bool IsBlocked(string command, object parameters, DwgMcpConfig config)
        {
            if ((config?.EnableSendCodeOrDefault ?? true) && !(config?.ReadOnlyOrDefault ?? false))
                return false;
            if (IsSendCode(command))
                return true;
            if (!string.Equals(Normalize(command), "batch_execute", StringComparison.OrdinalIgnoreCase))
                return false;

            var token = parameters as JToken ?? (parameters == null ? null : JToken.FromObject(parameters));
            var commands = token?["commands"] as JArray;
            return commands != null && commands.OfType<JObject>()
                .Any(item => IsBlocked((string)item["cmd"], item["params"], config));
        }

        internal static bool IsSendCode(string command)
            => string.Equals(Normalize(command), "send_code", StringComparison.OrdinalIgnoreCase);

        private static string Normalize(string command)
        {
            command = command?.Trim();
            return command != null && command.StartsWith("dwg_", StringComparison.OrdinalIgnoreCase)
                ? command.Substring(4) : command;
        }
    }
}
