using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Views.Toast
{
    public static class ToastContentBuilder
    {
        public static McpToastViewModel BuildCompleted(string command, string resultJson, bool success, string error, long durationMs, string drawingName = null)
        {
            JObject result = null;
            try { if (!string.IsNullOrEmpty(resultJson)) result = JObject.Parse(resultJson); } catch { }
            return BuildCompleted(command, result, success, error, durationMs, drawingName);
        }

        public static McpToastViewModel BuildCompleted(string command, JObject result, bool success, string error, long durationMs, string drawingName = null)
        {
            command = command ?? "unknown";
            var failed = !success || Failed(command, result, 0);
            var kind = ToolActivityClassifier.Classify(command);
            var vm = new McpToastViewModel
            {
                CommandName = command, Title = ToolNameFormatter.Format(command), Kind = kind,
                Success = !failed, DurationMs = Math.Max(0, durationMs),
                CategoryLabel = failed ? "MCP · Failed" : Category(command, kind),
                Summary = failed ? Safe(error ?? Text(result, "error") ?? "Tool call failed", 120) : "Completed successfully",
                Detail = SafeFileName(drawingName)
            };
            if ((command == "batch_execute" || command == "run_baked_tool") && result?["results"] is JArray items)
            {
                int failures = items.OfType<JObject>().Count(item => ItemFailed(item, 0));
                vm.Summary = failures > 0 ? $"{items.Count - failures} succeeded · {failures} failed"
                    : failed ? "Batch reported partial completion" : $"{items.Count} operations completed";
                return vm;
            }
            if (failed) return vm;
            if (command == "send_code")
            {
                vm.Summary = "Script finished";
                vm.Detail = "Custom C# executed in AutoCAD";
            }
            else if (command == "capture_view_image")
            {
                var path = Text(result, "output_path");
                var width = Number(result, "width");
                var height = Number(result, "height");
                vm.Summary = "Saved " + (SafeFileName(path) ?? "image");
                if (width.HasValue && height.HasValue)
                    vm.Summary += $" · {width}×{height} {(Text(result, "image_format") ?? "image").ToUpperInvariant()}";
                vm.ThumbnailPath = IsSafeImagePath(path) ? path : null;
                if (vm.ThumbnailPath != null) vm.Detail = "Click to open";
            }
            else if (command == "get_drawing_info")
            {
                vm.Summary = Safe(Text(result, "drawing_name") ?? "Active drawing", 100);
                vm.Detail = Safe(Text(result, "current_layout"), 100);
            }
            else
            {
                var count = Number(result, "count") ?? Number(result, "total") ?? Number(result, "returned");
                if (count.HasValue) vm.Summary = $"{count} results";
                else
                    foreach (var field in new[] { "entities", "handles", "clusters", "layers", "blocks", "updated" })
                        if (result?[field] is JArray array) { vm.Summary = $"{array.Count} {field}"; break; }
            }
            vm.Summary = Safe(vm.Summary, 120);
            vm.Detail = Safe(vm.Detail, 100);
            return vm;
        }

        private static bool Failed(string command, JObject result, int depth)
        {
            if (depth > 8) return true;
            if (command == "send_code") return Flag(result, "ok") == false;
            if (command != "batch_execute" && command != "run_baked_tool") return false;
            return Flag(result, "ok") == false || Flag(result, "partial_failure") == true
                || (result?["results"] is JArray items && items.OfType<JObject>().Any(item => ItemFailed(item, depth + 1)));
        }

        private static bool ItemFailed(JObject item, int depth) => Flag(item, "ok") == false
            || Failed(Text(item, "cmd"), item["result"] as JObject, depth + 1);

        private static string Category(string command, ToolActivityKind kind)
        {
            if (command == "send_code" || command == "batch_execute" || command == "run_baked_tool") return "MCP · Script";
            if (command == "capture_view_image") return "MCP · Snapshot";
            if (command.StartsWith("export_", StringComparison.Ordinal)) return "MCP · Export";
            return kind == ToolActivityKind.Write ? "MCP · Modified" : "MCP · Query";
        }

        private static string Text(JObject value, string key) => value?[key]?.Type == JTokenType.String ? (string)value[key] : null;
        private static bool? Flag(JObject value, string key) => value?[key]?.Type == JTokenType.Boolean ? (bool?)value[key] : null;
        private static long? Number(JObject value, string key)
        {
            try { return value?[key]?.Type == JTokenType.Integer ? (long?)value[key] : null; } catch { return null; }
        }
        private static string Safe(string text, int limit)
        {
            var clean = ErrorSanitizer.Sanitize(text ?? string.Empty);
            return clean.Length <= limit ? clean : clean.Substring(0, limit - 3) + "...";
        }
        private static string SafeFileName(string path)
        {
            try { return string.IsNullOrWhiteSpace(path) ? null : Safe(Path.GetFileName(path), 100); } catch { return null; }
        }
        internal static bool IsSafeImagePath(string path) => ToastImagePolicy.IsSafeImagePath(path);
    }
}
