using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Bimwright.Dwg.Server.Prompts
{
    /// <summary>
    /// Loads embedded prompt bodies (resource Bimwright.Dwg.Prompts.&lt;name&gt;.md) and renders them against
    /// the active toolsets, as rvt-mcp does: when a toolset the prompt needs is not enabled, the renderer
    /// returns a short enable notice instead of the body, so the agent never follows steps it cannot run.
    /// </summary>
    internal static class PromptBody
    {
        public const string ResourcePrefix = "Bimwright.Dwg.Prompts.";

        /// <summary>Null when the resource is not embedded. Never throws.</summary>
        public static string Load(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            try
            {
                using (var stream = typeof(PromptBody).Assembly.GetManifestResourceStream(ResourcePrefix + name + ".md"))
                {
                    if (stream == null) return null;
                    using (var reader = new StreamReader(stream)) return reader.ReadToEnd();
                }
            }
            catch (Exception) { return null; }
        }

        public static string Render(string body, string[] requiredToolsets, IReadOnlyDictionary<string, string> args = null)
        {
            if (body == null) return "This prompt's body was not found in the installed server build.";
            var enabled = ToolsetFilter.Resolve(ServerState.Config);
            var missing = (requiredToolsets ?? Array.Empty<string>()).Where(t => !enabled.Contains(t)).OrderBy(t => t, StringComparer.Ordinal).ToArray();
            if (missing.Length > 0) return MissingToolsetsNotice(missing, enabled);
            if (args != null)
                foreach (var pair in args) body = body.Replace("{" + pair.Key + "}", pair.Value ?? string.Empty);
            return body;
        }

        private static string MissingToolsetsNotice(string[] missing, HashSet<string> enabled)
        {
            var merged = ToolsetFilter.KnownToolsets.Where(t => enabled.Contains(t) || missing.Contains(t)).ToArray();
            return "This prompt needs toolsets that are not enabled on this server.\n" +
                   "Missing: " + string.Join(", ", missing) + ".\n" +
                   "Add them to the server command line, e.g.:  --toolsets " + string.Join(",", merged) + "\n" +
                   "(or --toolsets all), then restart the MCP connection so the client picks up the new tool list.";
        }
    }
}
