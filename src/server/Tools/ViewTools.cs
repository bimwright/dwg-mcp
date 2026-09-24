using System.ComponentModel;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server.Tools
{
    [McpServerToolType]
    public class ViewTools
    {
        [McpServerTool(Name = "dwg_zoom_extents", ReadOnly = true, Idempotent = true), Description(
            "Zoom to the extents of the drawing viewport.")]
        public static Task<string> ZoomExtents()
        {
            var request = new JObject();
            return ToolGateway.LoggedCall("zoom_extents", request, request);
        }

        [McpServerTool(Name = "dwg_zoom_window", ReadOnly = true, Idempotent = true), Description(
            "Zoom viewport to a window defined by two corner points.")]
        public static Task<string> ZoomWindow(
            [Description("First corner of the zoom window.")] JObject corner1,
            [Description("Second corner of the zoom window.")] JObject corner2)
        {
            var request = new JObject
            {
                ["corner1"] = corner1,
                ["corner2"] = corner2
            };
            return ToolGateway.LoggedCall("zoom_window", request, request);
        }

        [McpServerTool(Name = "dwg_zoom_to_entity", ReadOnly = true, Idempotent = true), Description(
            "Zoom viewport to the extents of a specific drawing entity identified by handle.")]
        public static Task<string> ZoomToEntity(
            [Description("AutoCAD handle of the entity to zoom to.")] string handle)
        {
            var request = new JObject
            {
                ["handle"] = handle
            };
            return ToolGateway.LoggedCall("zoom_to_entity", request, request);
        }

        [McpServerTool(Name = "dwg_capture_view_image", ReadOnly = true), Description(
            "Capture the active drawing to a raster image (.png/.jpg/.jpeg/.bmp) using AutoCAD's in-process document preview API. "
            + "No desktop automation, zoom, geometry edits or DWG save. Wait for zoom/commands to complete before capturing. "
            + "If output_path is omitted, saves to %LOCALAPPDATA%\\Bimwright\\Dwg\\captures\\ with an auto-generated name. "
            + "Returns the image path, actual dimensions, SHA-256, timestamp, document fingerprint, layout, viewport and camera. "
            + "Camera coordinates describe the active viewport; do not assume one pixel-to-world mapping for tiled views or paper layouts. "
            + "Persist the response alongside the image when building a reading atlas.")]
        public static Task<string> CaptureViewImage(
            [Description("Optional absolute output path ending in .png, .jpg, .jpeg, or .bmp. If omitted, an auto-named PNG is written to the captures directory.")] string output_path = null,
            [Description("Requested longer image dimension (default 1600, clamped to 64-8192), using the current viewport aspect ratio. AutoCAD may round the actual dimensions returned.")] int? pixel_size = null,
            [Description("Image format used only when output_path is omitted: png (default), jpeg, or bmp.")] string image_format = null,
            [Description("Overwrite the output file if it already exists.")] bool? overwrite_existing = null,
            [Description("Allow output to the repository root directory.")] bool? allow_repo_output = null,
            [Description("Optional drawing fingerprint from a previous capture. Rejects capture if the active drawing differs; never switches documents.")] string expected_document_fingerprint = null)
        {
            var request = new JObject();
            if (output_path != null) request["output_path"] = output_path;
            if (pixel_size.HasValue) request["pixel_size"] = pixel_size.Value;
            if (image_format != null) request["image_format"] = image_format;
            if (overwrite_existing.HasValue) request["overwrite_existing"] = overwrite_existing.Value;
            if (allow_repo_output.HasValue) request["allow_repo_output"] = allow_repo_output.Value;
            if (expected_document_fingerprint != null) request["expected_document_fingerprint"] = expected_document_fingerprint;

            return ToolGateway.LoggedCall("capture_view_image", request, request);
        }
    }
}
