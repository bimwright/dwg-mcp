using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin
{
    public static partial class PluginSettings
    {
        public const int DefaultReportRetentionDays = 30;
        public const int MaxReportRetentionDays = 3650;

        /// <summary>reportRetentionDays: an integer 1..3650, otherwise 30.</summary>
        public static int LoadReportRetentionDays() => NormalizeReportRetentionDays(ReadRoot(FilePath)?["reportRetentionDays"]);

        internal static int NormalizeReportRetentionDays(JToken value)
            => (value as JValue)?.Value is long days && days >= 1 && days <= MaxReportRetentionDays ? (int)days : DefaultReportRetentionDays;

        public static ReportsWindowState LoadReportsWindow() => ReadReportsWindow(ReadRoot(FilePath)?["reportsWindow"]);

        /// <summary>Persist only reportsWindow; every other key is kept.</summary>
        public static bool SaveReportsWindow(ReportsWindowState state, out string error)
        {
            if (state == null) state = new ReportsWindowState();
            JObject Bounds(ReportsWindowBounds b) => new JObject { ["left"] = b.Left, ["top"] = b.Top, ["width"] = b.Width, ["height"] = b.Height };
            return TryUpdate(root =>
            {
                var box = new JObject { ["mode"] = state.Mode == ReportsWindowMode.Compact ? "compact" : "wide" };
                if (state.Wide != null) box["wide"] = Bounds(state.Wide);
                if (state.Compact != null) box["compact"] = Bounds(state.Compact);
                box["textScale"] = state.TextScale;
                root["reportsWindow"] = box;
            }, "SaveReportsWindow", out error);
        }

        /// <summary>
        /// Lenient read: a mode other than "compact" means wide, invalid bounds are dropped
        /// and a textScale that is not one of the steps means 100.
        /// </summary>
        internal static ReportsWindowState ReadReportsWindow(JToken token)
        {
            var box = token as JObject;
            var mode = box?["mode"];
            var scale = (box?["textScale"] as JValue)?.Value;
            return new ReportsWindowState(
                mode != null && mode.Type == JTokenType.String && (string)mode == "compact" ? ReportsWindowMode.Compact : ReportsWindowMode.Wide,
                ReadBounds(box?["wide"]), ReadBounds(box?["compact"]),
                scale is long number && number >= int.MinValue && number <= int.MaxValue ? (int)number : ReportsTextScale.Default);
        }

        private static ReportsWindowBounds ReadBounds(JToken token)
        {
            var box = token as JObject;
            double? Number(string key) => box?[key] is JValue v && (v.Type == JTokenType.Float || v.Type == JTokenType.Integer)
                ? v.Value<double>() : (double?)null;
            var left = Number("left");
            var top = Number("top");
            var width = Number("width");
            var height = Number("height");
            if (!left.HasValue || !top.HasValue || !width.HasValue || !height.HasValue) return null;
            var bounds = new ReportsWindowBounds(left.Value, top.Value, width.Value, height.Value);
            return bounds.IsValid ? bounds : null;
        }

        private static JObject ReadRoot(string path)
        {
            try { return !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? JToken.Parse(File.ReadAllText(path)) as JObject : null; }
            catch (Exception) { return null; }
        }
    }
}
