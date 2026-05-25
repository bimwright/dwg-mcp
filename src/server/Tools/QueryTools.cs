using System;
using System.ComponentModel;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json;

namespace Bimwright.Dwg.Server.Tools
{
    [McpServerToolType]
    public class QueryTools
    {
        [McpServerTool(Name = "dwg_get_drawing_info", ReadOnly = true, Idempotent = true), Description(
            "Return small JSON-safe metadata for the current AutoCAD drawing: " +
            "drawing name/path when available, current layer, current space/layout, " +
            "and database unit scalars.")]
        public static Task<string> GetDrawingInfo()
        {
            var request = new { };
            return ToolGateway.LoggedCall("get_drawing_info", request, request);
        }

        [McpServerTool(Name = "dwg_get_entity_properties", ReadOnly = true, Idempotent = true), Description(
            "Return properties for AutoCAD entities identified by handle. " +
            "Input handles is a JSON array of hex handles, e.g. [\"7F5AD\"]. " +
            "Returns one result record per handle; bad handles do not abort siblings.")]
        public static Task<string> GetEntityProperties(
            [Description("JSON array of AutoCAD handles, e.g. [\"7F5AD\",\"2A4F\"].")] string handles,
            [Description("When true, include lightweight geometry such as extents, points, lengths, and text positions where available.")] bool includeGeometry = true)
        {
            var parsed = JsonConvert.DeserializeObject<string[]>(handles) ?? Array.Empty<string>();
            var request = new { handles = parsed, include_geometry = includeGeometry };
            return ToolGateway.LoggedCall("get_entity_properties", request, request);
        }

        [McpServerTool(Name = "dwg_list_layers", ReadOnly = true, Idempotent = true), Description(
            "List layers in the current AutoCAD drawing with small scalar properties " +
            "such as color index, locked/frozen/off state.")]
        public static Task<string> ListLayers()
        {
            var request = new { };
            return ToolGateway.LoggedCall("list_layers", request, request);
        }

        [McpServerTool(Name = "dwg_get_selected_texts", ReadOnly = true, Idempotent = true), Description(
            "Read and cluster text entities currently selected in AutoCAD. " +
            "Returns pre-clustered groups with combined text in reading order. " +
            "Each cluster has id, text, entity_count, in_block (bool) and " +
            "rewrite_mode (one of: update, collapse, rewrite_in_block) - the " +
            "mode that dwg_translate_and_rewrite will apply if given a translation " +
            "for this cluster. Use this BEFORE dwg_translate_and_rewrite. The user " +
            "must select entities in AutoCAD BEFORE calling this tool.")]
        public static Task<string> GetSelectedTexts(
            [Description("Optional grouping strength: weak, normal, or strong. Use weak when an image/layout reference indicates nearby labels should stay separate.")] string groupingStrength = "normal",
            [Description("When true, include each child text entity's handle/text/position so callers can recover from over-grouped clusters.")] bool includeEntities = false)
        {
            var request = new { grouping_strength = groupingStrength, include_entities = includeEntities };
            return ToolGateway.LoggedCall("get_selected_texts", request, request);
        }
    }
}
