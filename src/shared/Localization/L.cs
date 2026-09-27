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
            ["toast.activity.success"] = "Success",
            ["toast.activity.failed"] = "Failed",
            ["toast.activity.capture"] = "Capture",
            ["toast.activity.counts"] = "Counts are commands in this card. Capture is how many of the successes saved an image.",
            ["toast.status.enabled"] = "Toast notifications enabled",
            ["toast.status.enabled.summary"] = "New activity will appear here.",
            ["toast.status.disabled"] = "Toast notifications disabled",
            ["toast.status.disabled.summary"] = "New activity is hidden until toast notifications are enabled.",
            ["toast.status.saveFailed"] = "Preference could not be saved; this session is still using the new state.",
            ["settings.title"] = "BIMwright Settings",
            ["settings.tab.general"] = "General",
            ["settings.tab.toast"] = "Toast",
            ["settings.tab.tools"] = "Tools",
            ["settings.tab.about"] = "About",
            ["settings.general.connection"] = "Connection",
            ["settings.general.transport"] = "Transport",
            ["settings.general.port"] = "Port",
            ["settings.general.pipe"] = "Named pipe",
            ["settings.general.port.na"] = "Not applicable",
            ["settings.general.copy"] = "Copy",
            ["settings.general.copied"] = "Copied",
            ["settings.general.copy.disabled"] = "There is no TCP port to copy.",
            ["settings.general.listener"] = "Listener",
            ["settings.general.listener.on"] = "On",
            ["settings.general.listener.off"] = "Off",
            ["settings.general.client"] = "AI client",
            ["settings.general.client.connected"] = "Connected",
            ["settings.general.client.idle"] = "No recent activity",
            ["settings.general.stopped"] = "Listener is stopped.",
            ["settings.toast.enabled"] = "Show activity notifications",
            ["settings.toast.enabled.help"] = "Takes effect immediately.",
            ["settings.toast.brand"] = "Show branding",
            ["settings.toast.brand.help"] = "Appears when you point at the activity card. Applies immediately and lasts until AutoCAD restarts.",
            ["settings.toast.idle"] = "Idle duration",
            ["settings.toast.idle.help"] = "Hides the card when no new results arrive. Hover to keep it open. Applies from the next activity.",
            ["settings.toast.idle.seconds"] = "{seconds} seconds",
            ["settings.apply"] = "Apply",
            ["settings.cancel"] = "Cancel",
            ["settings.close"] = "Close",
            ["settings.discard"] = "Discard unsaved idle duration?",
            ["settings.tools.search"] = "Search",
            ["settings.tools.search.placeholder"] = "Name or description",
            ["settings.tools.name"] = "Tool name",
            ["settings.tools.description"] = "Description",
            ["settings.tools.source"] = "Source",
            ["settings.tools.timeout"] = "Time-out",
            ["settings.tools.empty"] = "No MCP server connected.",
            ["settings.tools.waiting"] = "Connected. Waiting for the tool list.",
            ["settings.tools.count"] = "{count} tools",
            ["settings.tools.none"] = "No tools match.",
            ["settings.tools.invalid"] = "The tool list could not be read.",
            ["settings.about.version"] = "Version",
            ["settings.about.description"] = "MCP connectivity and activity tools for Autodesk AutoCAD.",
            ["settings.about.author"] = "Author",
            ["settings.about.license"] = "License",
            ["settings.about.github"] = "GitHub",
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
