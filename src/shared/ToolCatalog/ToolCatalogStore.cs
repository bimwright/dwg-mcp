using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.ToolCatalog
{
    public sealed class ToolCatalogEntry
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Source { get; set; }
        public int? TimeoutSeconds { get; set; }
    }

    public enum ToolCatalogStatus
    {
        Empty,
        Current,
        Invalid
    }

    /// <summary>
    /// Last catalog pushed by the MCP server. dwg-mcp closes the socket after each
    /// request, so this store outlives the connection until the listener stops.
    /// </summary>
    public static class ToolCatalogStore
    {
        private static readonly object Gate = new object();
        private static ToolCatalogEntry[] _tools = Array.Empty<ToolCatalogEntry>();

        public static ToolCatalogStatus Status { get; private set; } = ToolCatalogStatus.Empty;

        public static ToolCatalogEntry[] Snapshot()
        {
            lock (Gate)
                return (ToolCatalogEntry[])_tools.Clone();
        }

        /// <summary>Returns null when the payload was stored. Otherwise a safe error string.</summary>
        public static string AcceptJson(JToken parameters)
        {
            var tools = parameters?["tools"] as JArray;
            if (tools == null)
            {
                lock (Gate)
                {
                    Status = ToolCatalogStatus.Invalid;
                    _tools = Array.Empty<ToolCatalogEntry>();
                }
                return "tool catalog is missing a tools array";
            }

            var parsed = new List<ToolCatalogEntry>(tools.Count);
            foreach (var item in tools)
            {
                var obj = item as JObject;
                if (obj == null)
                    continue;
                var name = obj["name"]?.Value<string>();
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                int? timeout = null;
                var timeoutToken = obj["timeout_seconds"];
                if (timeoutToken != null && timeoutToken.Type != JTokenType.Null)
                {
                    if (timeoutToken.Type != JTokenType.Integer)
                    {
                        lock (Gate)
                        {
                            Status = ToolCatalogStatus.Invalid;
                            _tools = Array.Empty<ToolCatalogEntry>();
                        }
                        return "tool catalog timeout_seconds must be an integer";
                    }
                    timeout = timeoutToken.Value<int>();
                }

                var source = obj["source"]?.Value<string>();
                if (!string.Equals(source, "built-in", StringComparison.Ordinal)
                    && !string.Equals(source, "baked", StringComparison.Ordinal))
                    source = "built-in";

                parsed.Add(new ToolCatalogEntry
                {
                    Name = name.Trim(),
                    Description = obj["description"]?.Value<string>() ?? string.Empty,
                    Source = source,
                    TimeoutSeconds = timeout
                });
            }

            parsed.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            lock (Gate)
            {
                _tools = parsed.ToArray();
                Status = ToolCatalogStatus.Current;
            }
            return null;
        }

        public static void Clear()
        {
            lock (Gate)
            {
                _tools = Array.Empty<ToolCatalogEntry>();
                Status = ToolCatalogStatus.Empty;
            }
        }
    }
}
