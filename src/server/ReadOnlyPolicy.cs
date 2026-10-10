using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ModelContextProtocol.Server;

namespace Bimwright.Dwg.Server
{
    internal static class ReadOnlyPolicy
    {
        private static readonly HashSet<string> ReadCommands = new HashSet<string>(
            typeof(Program).Assembly.GetTypes()
                .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() != null)
                .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                .Select(method => method.GetCustomAttribute<McpServerToolAttribute>())
                .Where(tool => tool != null && tool.ReadOnly)
                .Select(tool => Normalize(tool.Name)), StringComparer.OrdinalIgnoreCase);

        internal static bool IsBlocked(string command, DwgMcpConfig config)
            => (config?.ReadOnlyOrDefault ?? false) && !ReadCommands.Contains(Normalize(command));

        internal static IEnumerable<MethodInfo> ExposedMethods(Type type, bool readOnly)
            => SendCodePolicy.ExposedMethods(type, true)
                .Where(method => !readOnly || method.GetCustomAttribute<McpServerToolAttribute>().ReadOnly);

        private static string Normalize(string command)
        {
            command = command?.Trim();
            return command != null && command.StartsWith("dwg_", StringComparison.OrdinalIgnoreCase)
                ? command.Substring(4) : command;
        }
    }
}
