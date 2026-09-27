using System;
using System.IO;
using Newtonsoft.Json.Linq;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.Localization;

namespace Bimwright.Dwg.Plugin.Views.Toast
{
    /// <summary>
    /// Builds human-readable toast copy from MCP call data (richer than a bare
    /// command name). dwg-mcp wire names — see CommandDispatcher.
    /// </summary>
    public static class ToastContentBuilder
    {
        public static McpToastViewModel BuildCompleted(
            string toolName,
            string resultJson,
            bool success,
            string errorMessage,
            string toolDescription)
        {
            if (string.IsNullOrEmpty(toolName))
                toolName = "unknown";

            var content = success
                ? BuildSuccessContent(toolName, resultJson)
                : BuildFailureContent(toolName, errorMessage, toolDescription);

            return new McpToastViewModel
            {
                Title = ToolNameFormatter.Format(toolName),
                Summary = content.Summary,
                Detail = content.Detail,
                ThumbnailPath = content.ThumbnailPath,
                Success = success
            };
        }

        private static ToastContent BuildSuccessContent(string toolName, string resultJson)
        {
            JToken result = null;
            try { if (!string.IsNullOrEmpty(resultJson)) result = JToken.Parse(resultJson); } catch { }

            var resultObj = result as JObject;

            switch (toolName)
            {
                case "capture_view_image":
                case "inspect_view_region":
                case "restore_view":
                    return BuildCaptureImageSuccess(resultObj);
                case "get_drawing_info":
                    return BuildDrawingInfoSuccess(resultObj);
                case "send_code":
                    return BuildSendCodeSuccess(resultObj);
                case "translate_and_rewrite":
                case "collapse_and_rewrite":
                case "update_texts":
                    return BuildRewriteSuccess(result);
                default:
                    return BuildGenericSuccess(toolName, result);
            }
        }

        private static ToastContent BuildFailureContent(string toolName, string errorMessage, string toolDescription)
        {
            var summary = Truncate(errorMessage ?? L.T("toast.failed.default"), 120);
            var detail = !string.IsNullOrWhiteSpace(toolDescription)
                ? Truncate(toolDescription, 100)
                : ToolNameFormatter.Format(toolName);
            return new ToastContent(summary, detail);
        }

        private static ToastContent BuildCaptureImageSuccess(JObject result)
        {
            var savedPath = result?.Value<string>("output_path") ?? result?.Value<string>("saved_path");
            var width = result?.Value<int?>("width");
            var height = result?.Value<int?>("height");
            var format = (result?.Value<string>("image_format") ?? "png").ToUpperInvariant();
            var captureId = result?.Value<string>("capture_id");

            var fileName = GetFileNameSafe(savedPath, L.T("toast.capture.imageFallback"));
            var summary = width.HasValue && height.HasValue
                ? L.T("toast.capture.savedSize", ("fileName", fileName), ("width", width.Value), ("height", height.Value), ("format", format))
                : L.T("toast.capture.saved", ("fileName", fileName));

            var thumb = IsSafeImagePath(savedPath) ? savedPath : null;
            var detailParts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrWhiteSpace(captureId))
                detailParts.Add(L.T("toast.capture.id", ("id", captureId.Length > 8 ? captureId.Substring(0, 8) : captureId)));
            if (thumb != null)
                detailParts.Add(L.T("toast.capture.clickToOpen"));
            var detail = string.Join(" · ", detailParts);
            return new ToastContent(summary, detail, thumb);
        }

        private static ToastContent BuildDrawingInfoSuccess(JObject result)
        {
            var name = result?.Value<string>("drawing_name") ?? result?.Value<string>("document_name");
            if (string.IsNullOrWhiteSpace(name))
                name = L.T("toast.drawing.unsaved");

            var layout = result?.Value<string>("current_layout");
            var layer = result?.Value<string>("current_layer");

            var detailParts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrWhiteSpace(layout))
                detailParts.Add(L.T("toast.drawing.layout", ("layout", layout)));
            if (!string.IsNullOrWhiteSpace(layer))
                detailParts.Add(L.T("toast.drawing.layer", ("layer", layer)));

            return new ToastContent(name, string.Join(" · ", detailParts));
        }

        private static ToastContent BuildSendCodeSuccess(JObject result)
        {
            // dwg send_code reports script failures inside a successful envelope:
            // ok=false + error for compile/runtime faults.
            var innerOk = result?.Value<bool?>("ok");
            var innerError = result?.Value<string>("error");
            if (innerOk == false && !string.IsNullOrWhiteSpace(innerError))
                return new ToastContent(Truncate(FirstLine(innerError), 100), L.T("toast.sendCode.detail"));

            var text = result?.Value<string>("result");
            var summary = string.IsNullOrWhiteSpace(text) ? L.T("toast.sendCode.finished") : Truncate(FirstLine(text), 100);
            return new ToastContent(summary, L.T("toast.sendCode.detail"));
        }

        private static ToastContent BuildRewriteSuccess(JToken result)
        {
            var items = (result as JObject)?["results"] as JArray ?? result as JArray;
            if (items == null)
                return new ToastContent(L.T("toast.generic.completed"), null);

            var failed = 0;
            foreach (var item in items)
            {
                if ((item as JObject)?["ok"]?.Value<bool?>() == false)
                    failed++;
            }

            var summary = L.T("toast.rewrite.done", ("count", items.Count));
            var detail = failed > 0 ? L.T("toast.rewrite.someFailed", ("count", failed)) : null;
            return new ToastContent(summary, detail);
        }

        private static ToastContent BuildGenericSuccess(string toolName, JToken result)
        {
            if (result == null)
                return new ToastContent(L.T("toast.generic.completed"), ToolNameFormatter.Format(toolName));

            // Top-level array results (e.g. update_texts) report their length.
            var array = result as JArray;
            if (array != null)
                return new ToastContent(L.T("toast.generic.items", ("count", array.Count)), ToolNameFormatter.Format(toolName));

            var obj = result as JObject;
            if (obj == null)
                return new ToastContent(L.T("toast.generic.completed"), ToolNameFormatter.Format(toolName));

            var total = obj.Value<int?>("total");
            var returned = obj.Value<int?>("returned");
            var count = obj.Value<int?>("count");
            var rowCount = obj.Value<int?>("rowCount");
            var drawingName = obj.Value<string>("drawing_name") ?? obj.Value<string>("document_name");
            var savedPath = obj.Value<string>("saved_path") ?? obj.Value<string>("output_path") ?? obj.Value<string>("path");

            if (total.HasValue || returned.HasValue)
            {
                var n = total ?? returned;
                return new ToastContent(L.T("toast.generic.results", ("count", n.Value)), ToolNameFormatter.Format(toolName));
            }
            if (count.HasValue)
                return new ToastContent(L.T("toast.generic.items", ("count", count.Value)), ToolNameFormatter.Format(toolName));
            if (rowCount.HasValue)
                return new ToastContent(L.T("toast.generic.rows", ("count", rowCount.Value)), ToolNameFormatter.Format(toolName));
            if (!string.IsNullOrWhiteSpace(drawingName))
                return new ToastContent(drawingName, ToolNameFormatter.Format(toolName));
            if (!string.IsNullOrWhiteSpace(savedPath))
            {
                var thumb = IsSafeImagePath(savedPath) ? savedPath : null;
                return new ToastContent(GetFileNameSafe(savedPath, L.T("toast.generic.fileFallback")), ToolNameFormatter.Format(toolName), thumb);
            }

            // First array property stands in for a count (layers, entities, blocks…).
            foreach (var prop in obj.Properties())
            {
                if (prop.Value is JArray propArray)
                    return new ToastContent(L.T("toast.generic.items", ("count", propArray.Count)), ToolNameFormatter.Format(toolName));
            }

            var message = obj.Value<string>("message") ?? obj.Value<string>("summary");
            if (!string.IsNullOrWhiteSpace(message))
                return new ToastContent(Truncate(message, 100), ToolNameFormatter.Format(toolName));

            return new ToastContent(L.T("toast.generic.completed"), ToolNameFormatter.Format(toolName));
        }

        internal static bool IsSafeImagePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                if (!File.Exists(path))
                    return false;

                var full = Path.GetFullPath(path);
                var ext = Path.GetExtension(full);
                if (ext == null)
                    return false;
                ext = ext.ToLowerInvariant();
                if (ext != ".png" && ext != ".jpg" && ext != ".jpeg" && ext != ".bmp")
                    return false;

                return PathAllowlist.IsUnderTempOrCaptures(full);
            }
            catch
            {
                return false;
            }
        }

        private static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;
            var idx = text.IndexOf('\n');
            return idx < 0 ? text : text.Substring(0, idx);
        }

        private static string Truncate(string text, int max)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max)
                return text ?? string.Empty;
            return text.Substring(0, max - 3) + "...";
        }

        private readonly struct ToastContent
        {
            public string Summary { get; }
            public string Detail { get; }
            public string ThumbnailPath { get; }

            public ToastContent(string summary, string detail, string thumbnailPath = null)
            {
                Summary = summary ?? string.Empty;
                Detail = detail ?? string.Empty;
                ThumbnailPath = thumbnailPath;
            }
        }

        private static string GetFileNameSafe(string path, string fallback)
        {
            if (string.IsNullOrEmpty(path))
                return fallback;
            if (path.Contains("<") || path.Contains(">") || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                return fallback;
            try
            {
                return Path.GetFileName(path);
            }
            catch
            {
                return fallback;
            }
        }
    }
}
