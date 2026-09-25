using System;
using System.Collections.Generic;
using System.Globalization;

namespace Bimwright.Dwg.Plugin.Localization
{
    /// <summary>
    /// English-only string table for plugin UI strings. Keeps the same call
    /// signature as rvt-mcp's L.T (key + named placeholders, "{name}" or
    /// "{name:n}" for thousands-separated numbers) so a future catalog port
    /// does not touch call sites. Missing keys return the key, never throw.
    /// </summary>
    public static class L
    {
        private static readonly Dictionary<string, string> En = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["toast.category.query"] = "MCP · Query",
            ["toast.category.modified"] = "MCP · Modified",
            ["toast.category.script"] = "MCP · Script",
            ["toast.category.export"] = "MCP · Export",
            ["toast.category.snapshot"] = "MCP · Snapshot",
            ["toast.category.connected"] = "MCP · Connected",
            ["toast.category.failed"] = "MCP · Failed",
            ["toast.connected.title"] = "Agent connected",
            ["toast.connected.summary"] = "dwg-mcp is ready",
            ["toast.failed.default"] = "Tool call failed",
            ["toast.capture.saved"] = "Saved {fileName}",
            ["toast.capture.savedSize"] = "Saved {fileName} · {width}×{height} {format}",
            ["toast.capture.id"] = "Capture {id}",
            ["toast.capture.clickToOpen"] = "Click to open",
            ["toast.capture.imageFallback"] = "image",
            ["toast.drawing.unsaved"] = "Unsaved drawing",
            ["toast.drawing.layout"] = "Layout {layout}",
            ["toast.drawing.layer"] = "Layer {layer}",
            ["toast.rewrite.done"] = "Processed {count:n} texts",
            ["toast.rewrite.someFailed"] = "{count:n} failed",
            ["toast.sendCode.finished"] = "Script finished",
            ["toast.sendCode.detail"] = "Custom C# executed in AutoCAD",
            ["toast.selected.count"] = "Entities selected: {count:n}",
            ["toast.selected.done"] = "Selection read",
            ["toast.generic.completed"] = "Completed successfully",
            ["toast.generic.results"] = "Results: {count:n}",
            ["toast.generic.items"] = "Items: {count:n}",
            ["toast.generic.rows"] = "Rows: {count:n}",
            ["toast.generic.fileFallback"] = "file",
        };

        public static string T(string key, params (string Name, object Value)[] args)
        {
            if (key == null)
                return string.Empty;
            if (!En.TryGetValue(key, out var template))
                return key;
            if (args == null || args.Length == 0)
                return template;

            var text = template;
            foreach (var (name, value) in args)
            {
                var formatted = FormatValue(value, text, name);
                text = text.Replace("{" + name + ":n}", formatted)
                           .Replace("{" + name + "}", formatted);
            }
            return text;
        }

        private static string FormatValue(object value, string template, string name)
        {
            if (value == null)
                return string.Empty;
            if (template != null && template.Contains("{" + name + ":n}")
                && value is IConvertible c)
            {
                try { return c.ToDouble(CultureInfo.InvariantCulture).ToString("n0", CultureInfo.InvariantCulture); }
                catch { }
            }
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }
    }
}
