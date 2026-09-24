using System;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.View
{
    // Keep document binding and snapshot consistency independent of the native renderer.
    internal static class CaptureViewContract
    {
        internal static void ValidateExpectedDocument(string expectedFingerprint, string actualFingerprint)
        {
            if (expectedFingerprint == null) return;
            if (!Guid.TryParse(expectedFingerprint, out var expected))
                throw new ArgumentException("expected_document_fingerprint must be a GUID");
            if (!Guid.TryParse(actualFingerprint, out var actual) || expected != actual)
                throw new InvalidOperationException("active drawing does not match expected_document_fingerprint");
        }

        internal static void EnsureUnchanged(JObject before, JObject after)
        {
            if (!JToken.DeepEquals(before, after))
                throw new InvalidOperationException("drawing, layout, viewport or camera changed during capture; retry after the view settles");
        }

        internal static JObject BuildResult(
            JObject context, string outputPath, string imageFormat, int actualWidth, int actualHeight,
            int requestedWidth, int requestedHeight, string sha256, string capturedAtUtc, long durationMs)
        {
            if (actualWidth <= 0 || actualHeight <= 0)
                throw new InvalidOperationException("AutoCAD returned an empty capture image");

            var result = (JObject)context.DeepClone();
            result["output_path"] = outputPath;
            result["width"] = actualWidth;
            result["height"] = actualHeight;
            result["image_format"] = imageFormat;
            result["capture_id"] = Guid.NewGuid().ToString("N");
            result["source"] = "autocad_document_preview";
            result["capture_scope"] = "active_document";
            result["camera_scope"] = "active_viewport";
            result["captured_at"] = capturedAtUtc;
            result["duration_ms"] = durationMs;
            result["sha256"] = sha256;
            result["requested_size"] = new JObject { ["width"] = requestedWidth, ["height"] = requestedHeight };
            return result;
        }
    }
}
