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
            ["toast.activity.open_image"] = "Open image",
            ["toast.activity.counts"] = "Counts are commands in this card. Capture is how many of the successes saved an image.",
            ["toast.status.enabled"] = "Toast notifications enabled",
            ["toast.status.enabled.summary"] = "New activity will appear here.",
            ["toast.status.disabled"] = "Toast notifications disabled",
            ["toast.status.disabled.summary"] = "New activity is hidden until toast notifications are enabled.",
            ["toast.status.saveFailed"] = "Preference could not be saved; this session is still using the new state.",
            ["history.title"] = "{identity} — History",
            ["history.time"] = "Time",
            ["history.tool"] = "Tool",
            ["history.summary"] = "Summary",
            ["history.status"] = "Status",
            ["history.search"] = "Search",
            ["history.kind"] = "Kind",
            ["history.all"] = "All",
            ["history.read"] = "Read",
            ["history.write"] = "Write",
            ["history.newSession"] = "New session",
            ["history.footer"] = "{count} entries · Session only (latest 1,000). Raw inputs and JSON results are not retained; closing a card does not clear history.",
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
            ["settings.toast.brand.help"] = "Appears when you point at the activity card. Applies immediately and is remembered after AutoCAD restarts.",
            ["settings.toast.idle"] = "Idle duration",
            ["settings.toast.idle.saveFailed"] = "Toast duration could not be saved. The previous duration is still active. Try Apply again.",
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
            ["toast.category.query"] = "MCP · Query",
            ["toast.category.modified"] = "MCP · Modified",
            ["toast.category.script"] = "MCP · Script",
            ["toast.category.export"] = "MCP · Export",
            ["toast.category.snapshot"] = "MCP · Snapshot",
            ["toast.category.connected"] = "MCP · Connected",
            ["toast.category.failed"] = "MCP · Failed",
            ["toast.activity.header"] = "MCP · Activity",
            ["toast.activity.latest"] = "Latest",
            ["toast.activity.counts.tooltip"] = "Success / failed / images",
            ["toast.activity.recent"] = "Activity · {count:n} calls",
            ["toast.generic.fields"] = "Fields: {count:n}",
            ["toast.survey.partial"] = "Survey: coverage incomplete",
            ["toast.survey.complete"] = "Survey: supported checks complete",
            ["toast.survey.review"] = "Review scope and blind spots before changes",
            ["toast.capture.savedPx"] = "Saved {fileName} · {pixelSize}px {format}",
            ["toast.capture.viewId"] = "View id {viewId}",
            ["toast.currentView.active"] = "Active view",
            ["toast.currentView.scale"] = "Scale 1:{scale}",
            ["toast.rooms.none"] = "No rooms in model",
            ["toast.rooms.found"] = "Rooms found: {count:n}",
            ["toast.rooms.complete"] = "Room query complete",
            ["toast.rooms.placedStats"] = "Placed {placed:n}, unplaced {unplaced:n}",
            ["toast.filters.allLevelsPhases"] = "All levels · all phases",
            ["toast.filters.allLevels"] = "All levels",
            ["toast.filters.level"] = "Level: {level}",
            ["toast.filters.allPhases"] = "All phases",
            ["toast.filters.phase"] = "Phase: {phase}",
            ["toast.filters.status"] = "Status: {status}",
            ["toast.sheets.none"] = "No sheets in project",
            ["toast.sheets.found"] = "Drawing sheets: {count:n}",
            ["toast.sheets.ready"] = "Sheet list ready",
            ["toast.sheets.first"] = "First: {number} {name}",
            ["toast.sheets.more"] = "(+{count:n} more)",
            ["toast.sheets.fallback"] = "Sheets",
            ["toast.worksets.notWorkshared"] = "Model is not workshared",
            ["toast.worksets.none"] = "No worksets to list",
            ["toast.worksets.found"] = "Worksets: {count:n}",
            ["toast.worksets.complete"] = "Workset query complete",
            ["toast.worksets.active"] = "Active: {name}",
            ["toast.filter.matched"] = "Matched {count:n} {category}",
            ["toast.filter.elementsFallback"] = "elements",
            ["toast.filter.complete"] = "Element filter complete",
            ["settings.toast.heading"] = "Activity notifications",
            ["settings.toast.help"] = "Toast On/Off is immediate. Apply saves the duration and privacy settings.",
            ["settings.toast.description"] = "Control how activity appears while you work.",
            ["settings.toast.horizontal"] = "Horizontal alignment",
            ["settings.toast.vertical"] = "Vertical alignment",
            ["settings.toast.left"] = "Left",
            ["settings.toast.right"] = "Right",
            ["settings.toast.top"] = "Top",
            ["settings.toast.bottom"] = "Bottom",
            ["settings.toast.position.help"] = "Applies and saves immediately. Changing a corner clears the saved drag position.",
            ["settings.toast.drag"] = "Allow dragging the card",
            ["settings.toast.drag.help"] = "Drag the title row to move the card. Turning this off keeps the saved position; turning it on restores it.",
            ["settings.toast.reset"] = "Reset position",
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
