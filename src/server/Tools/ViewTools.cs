using System.ComponentModel;
using System.Threading.Tasks;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
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
            + "Returns inline MCP image content (up to 8 MiB) plus JSON with image path, actual dimensions, SHA-256, timestamp, document fingerprint, layout, viewport and camera. "
            + "Use capture_id with dwg_inspect_view_region when region_navigation.supported is true. "
            + "Camera coordinates describe the active viewport; do not assume one pixel-to-world mapping for tiled views or paper layouts. "
            + "Persist the response alongside the image when building a reading atlas.")]
        public static async Task<CallToolResult> CaptureViewImage(
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

            return ViewImageResponse.FromJson(await ToolGateway.LoggedCall("capture_view_image", request, request));
        }

        [McpServerTool(Name = "dwg_inspect_view_region", ReadOnly = true), Description(
            "Look closer at a rectangle in a previous capture: zoom in AutoCAD, regenerate, verify the camera and capture a NEW image. "
            + "Changes the visible view and saves a PNG, but does not edit geometry or save the DWG. "
            + "Requires a current source capture in the same unchanged drawing: single model-space viewport, top-down, zero twist, orthographic, no clipping. "
            + "Returns inline image and JSON with a new capture_id and parent_capture_id. Use the new ID for the next zoom; use dwg_restore_view to go back. "
            + "History is limited to 64 captures / 30 minutes in the current AutoCAD process. Stale or unsupported sources are refused before zoom.")]
        public static async Task<CallToolResult> InspectViewRegion(
            [Description("capture_id returned by the current supported capture.")] string source_capture_id,
            [Description("Rectangle on that exact image: {left,top,right,bottom}, normalized 0..1, origin top-left. Ordered bounds, at least 4 image pixels per axis. Entire region is fitted with 10% surrounding context, preserving aspect ratio.")] ViewImageRegion region,
            [Description("Requested longer image dimension, default 1600; clamped to 64..8192.")] int? pixel_size = null)
        {
            var request = new JObject
            {
                ["source_capture_id"] = source_capture_id,
                ["region"] = region == null ? null : new JObject
                {
                    ["left"] = region.Left, ["top"] = region.Top,
                    ["right"] = region.Right, ["bottom"] = region.Bottom
                }
            };
            if (pixel_size.HasValue) request["pixel_size"] = pixel_size.Value;
            return ViewImageResponse.FromJson(await ToolGateway.LoggedCall("inspect_view_region", request, request));
        }

        [McpServerTool(Name = "dwg_restore_view", ReadOnly = true), Description(
            "Return to the camera of a recent supported capture and return a fresh inline image plus JSON. "
            + "Changes the visible view and writes a PNG, not drawing geometry. Requires the same unchanged drawing, layout and single top-down model viewport. "
            + "Use an ancestor capture_id to return to the overview. Returns a new capture_id; use that new ID for subsequent region navigation. "
            + "History expires after 30 minutes or 64 captures or host restart.")]
        public static async Task<CallToolResult> RestoreView(
            [Description("ID of the supported capture whose camera should be restored.")] string source_capture_id,
            [Description("Requested longer image dimension, default 1600; clamped to 64..8192.")] int? pixel_size = null)
        {
            var request = new JObject { ["source_capture_id"] = source_capture_id };
            if (pixel_size.HasValue) request["pixel_size"] = pixel_size.Value;
            return ViewImageResponse.FromJson(await ToolGateway.LoggedCall("restore_view", request, request));
        }
    }

    // An explicit JSON DTO is necessary: JObject produces a recursive array schema
    // in the MCP SDK's System.Text.Json schema generator.
    public sealed class ViewImageRegion
    {
        [JsonPropertyName("left")] public required double Left { get; set; }
        [JsonPropertyName("top")] public required double Top { get; set; }
        [JsonPropertyName("right")] public required double Right { get; set; }
        [JsonPropertyName("bottom")] public required double Bottom { get; set; }
    }
}
