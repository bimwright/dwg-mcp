using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server
{
    /// <summary>
    /// Builds the plugin tool-catalog payload from the tool classes registered
    /// for this process. Every entry is an MCP tool the client can call.
    /// </summary>
    public static class ToolCatalogBuilder
    {
        public const int TimeoutSeconds = 30;

        public static JObject Build(IEnumerable<Type> toolTypes)
        {
            var tools = new List<JObject>();
            if (toolTypes != null)
            {
                foreach (var type in toolTypes)
                {
                    if (type == null)
                        continue;
                    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                    {
                        var tool = method.GetCustomAttribute<McpServerToolAttribute>();
                        if (tool == null || string.IsNullOrWhiteSpace(tool.Name))
                            continue;
                        var description = method.GetCustomAttribute<DescriptionAttribute>();
                        tools.Add(new JObject
                        {
                            ["name"] = tool.Name,
                            ["description"] = description == null ? string.Empty : description.Description ?? string.Empty,
                            ["source"] = "built-in",
                            ["timeout_seconds"] = TimeoutSeconds
                        });
                    }
                }
            }

            tools.Sort((a, b) => string.Compare(a.Value<string>("name"), b.Value<string>("name"), StringComparison.OrdinalIgnoreCase));
            return new JObject
            {
                ["schema_version"] = 1,
                ["tools"] = new JArray(tools)
            };
        }
    }
}
