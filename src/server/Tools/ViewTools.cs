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
    }
}
