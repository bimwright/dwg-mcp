using System;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.View
{
    // Image coordinates have their origin at the top left. Navigation deliberately
    // supports only one unrotated, orthographic, top-down model-space viewport.
    internal static class ViewReadingContract
    {
        internal static string UnsupportedReason(JObject context)
        {
            var doc = context["document"];
            var vp = context["viewport"];
            var camera = context["camera"];
            if (doc?["tile_mode"]?.Value<bool>() != true)
                return "Region navigation requires model space.";
            if (vp?["active_tiled_viewports"]?.Value<int>() != 1)
                return "Region navigation requires a single model-space viewport.";
            if (camera?["perspective"]?.Value<bool>() != false ||
                camera["front_clip_enabled"]?.Value<bool>() != false ||
                camera["back_clip_enabled"]?.Value<bool>() != false)
                return "Region navigation requires an orthographic view without clipping.";
            var direction = camera["direction_wcs"] as JArray;
            if (direction == null || direction.Count != 3 ||
                !Near((double)direction[0], 0) || !Near((double)direction[1], 0) ||
                !Finite((double)direction[2]) || (double)direction[2] <= 0 ||
                !Near((double?)camera["twist_radians"] ?? double.NaN, 0))
                return "Region navigation requires a top-down view with zero view twist.";
            double width = (double?)camera["width"] ?? 0, height = (double?)camera["height"] ?? 0;
            double screenWidth = (double?)vp["screen_width"] ?? 0, screenHeight = (double?)vp["screen_height"] ?? 0;
            if (!Finite(width) || !Finite(height) || width <= 0 || height <= 0 ||
                screenWidth <= 0 || screenHeight <= 0 ||
                Math.Abs(width / height * screenHeight - screenWidth) > 2)
                return "Camera and viewport aspect ratios do not agree.";
            return null;
        }

        internal static void ValidateSource(JObject source, JObject current, bool restoring)
        {
            string reason = UnsupportedReason(source) ?? UnsupportedReason(current);
            if (reason != null) throw new InvalidOperationException(reason);
            if (source["region_navigation"]?["supported"]?.Value<bool>() != true)
                throw new InvalidOperationException("This capture does not support region navigation; take a new supported capture.");
            if (!JToken.DeepEquals(source["document"], current["document"]) ||
                !JToken.DeepEquals(source["viewport"], current["viewport"]))
                throw new InvalidOperationException("Source capture is stale or belongs to another document, revision, layout or viewport; capture again.");
            if (!restoring && !JToken.DeepEquals(source["camera"], current["camera"]))
                throw new InvalidOperationException("View changed since the source capture; capture again or restore that capture first.");
        }

        internal static JObject RegionCamera(JObject source, JObject region)
        {
            double left = Coordinate(region, "left"), top = Coordinate(region, "top");
            double right = Coordinate(region, "right"), bottom = Coordinate(region, "bottom");
            if (right <= left || bottom <= top ||
                (right - left) * (int)source["width"] < 4 ||
                (bottom - top) * (int)source["height"] < 4)
                throw new ArgumentException("region must be ordered and span at least four source-image pixels on each axis.");

            var camera = (JObject)source["camera"].DeepClone();
            double width = (double)camera["width"], height = (double)camera["height"];
            var center = (JArray)camera["center_dcs"];
            camera["center_dcs"] = new JArray(
                (double)center[0] + ((left + right) / 2 - 0.5) * width,
                (double)center[1] + (0.5 - (top + bottom) / 2) * height);
            // Fit the whole selected rectangle, preserve the viewport aspect ratio,
            // and retain a small amount of surrounding context.
            double scale = Math.Min(1, Math.Max(right - left, bottom - top) * 1.1);
            camera["width"] = width * scale;
            camera["height"] = height * scale;
            return camera;
        }

        internal static void ValidateApplied(JObject before, JObject after, JObject expectedCamera)
        {
            if (!JToken.DeepEquals(before["document"], after["document"]) ||
                !JToken.DeepEquals(before["viewport"], after["viewport"]))
                throw new InvalidOperationException("Drawing or viewport changed while navigating; no image published.");
            var actual = (JObject)after["camera"];
            foreach (var field in expectedCamera.Properties())
            {
                if (field.Name == "center_dcs")
                {
                    if (!Near((double)field.Value[0], (double)actual[field.Name][0]) ||
                        !Near((double)field.Value[1], (double)actual[field.Name][1]))
                        throw new InvalidOperationException("AutoCAD did not apply the requested view center.");
                }
                else if (field.Name == "width" || field.Name == "height")
                {
                    if (!Near((double)field.Value, (double)actual[field.Name]))
                        throw new InvalidOperationException("AutoCAD did not apply the requested view size.");
                }
                else if (!JToken.DeepEquals(field.Value, actual[field.Name]))
                    throw new InvalidOperationException("Camera changed unexpectedly while navigating.");
            }
        }

        private static double Coordinate(JObject region, string name)
        {
            var token = region?[name];
            if (token == null || (token.Type != JTokenType.Float && token.Type != JTokenType.Integer))
                throw new ArgumentException("region requires numeric left, top, right and bottom coordinates.");
            double value = (double)token;
            if (!Finite(value) || value < 0 || value > 1)
                throw new ArgumentException("region coordinates must be finite values between 0 and 1.");
            return value;
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static bool Near(double a, double b) => Finite(a) && Finite(b) &&
            Math.Abs(a - b) <= 1e-8 + Math.Max(Math.Abs(a), Math.Abs(b)) * 1e-9;
    }
}
