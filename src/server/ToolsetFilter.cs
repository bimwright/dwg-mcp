using System;
using System.Collections.Generic;

namespace Bimwright.Dwg.Server
{
    public static class ToolsetFilter
    {
        public static readonly string[] KnownToolsets = { "query", "modify", "code", "meta", "toolbaker", "annotation", "block", "dimension" };
        public static readonly string[] DefaultOn = { "query", "modify", "meta" };

        private static readonly HashSet<string> WriteCapable = new HashSet<string>(
            new[] { "modify", "code", "annotation", "dimension" },
            StringComparer.OrdinalIgnoreCase);

        public static HashSet<string> Resolve(DwgMcpConfig config)
        {
            config = config ?? new DwgMcpConfig();

            var requested = config.Toolsets == null || config.Toolsets.Count == 0
                ? DefaultOn
                : config.Toolsets.ToArray();

            var enabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in requested)
            {
                if (string.Equals(item, "all", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var known in KnownToolsets)
                    {
                        enabled.Add(known);
                    }
                    continue;
                }

                if (IsKnown(item))
                {
                    enabled.Add(item.ToLowerInvariant());
                }
            }

            if (config.EnableSendCodeOrDefault && (config.Toolsets == null || config.Toolsets.Count == 0))
            {
                enabled.Add("code");
            }

            if (!config.EnableSendCodeOrDefault)
            {
                enabled.Remove("code");
            }

            if (!config.EnableToolbakerOrDefault)
            {
                enabled.Remove("toolbaker");
            }

            if (config.ReadOnlyOrDefault)
            {
                enabled.RemoveWhere(name => WriteCapable.Contains(name));
            }

            return enabled;
        }

        private static bool IsKnown(string value)
        {
            foreach (var known in KnownToolsets)
            {
                if (string.Equals(known, value, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
