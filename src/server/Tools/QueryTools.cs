using System.ComponentModel;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server.Tools
{
    [McpServerToolType]
    public class QueryTools
    {
        [McpServerTool(Name = "dwg_get_drawing_info", ReadOnly = true, Idempotent = true), Description(
            "Return small JSON-safe metadata for the current AutoCAD drawing: " +
            "drawing name when available, current layer, current space/layout, " +
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
            [Description("When true, include lightweight geometry such as extents, points, lengths, and text positions where available.")] bool includeGeometry = false)
        {
            if (string.IsNullOrWhiteSpace(handles))
            {
                return ToolInputError("handles must be a JSON array");
            }

            JArray parsed;
            try
            {
                parsed = JArray.Parse(handles);
            }
            catch (JsonException ex)
            {
                return ToolInputError($"handles must be a JSON array: {ex.Message}");
            }

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

        [McpServerTool(Name = "dwg_query_entities", ReadOnly = true, Idempotent = true), Description(
            "Query model-space AutoCAD entities with optional filters for entity type, layer, and ACI color index. " +
            "Returns count and entity property records; limit is clamped by the plugin.")]
        public static Task<string> QueryEntities(
            [Description("Optional entity type filter such as Line, Circle, Polyline, DBText, MText, BlockReference, Hatch, Arc, or Ellipse. Case-insensitive.")] string entity_type = null,
            [Description("Optional layer name filter. Case-insensitive.")] string layer = null,
            [Description("Optional ACI color index filter. Valid range: 1-256.")] int? color_index = null,
            [Description("Optional maximum number of returned entities. The plugin clamps this to 1-5000.")] int? limit = null,
            [Description("When true, include lightweight geometry such as extents, points, lengths, and text positions where available.")] bool include_geometry = false)
        {
            var request = BuildEntityQueryRequest(entity_type, layer, color_index, limit);
            request["include_geometry"] = include_geometry;
            return ToolGateway.LoggedCall("query_entities", request, request);
        }

        [McpServerTool(Name = "dwg_count_entities", ReadOnly = true, Idempotent = true), Description(
            "Count model-space AutoCAD entities matching optional entity type, layer, and ACI color index filters.")]
        public static Task<string> CountEntities(
            [Description("Optional entity type filter such as Line, Circle, Polyline, DBText, MText, BlockReference, Hatch, Arc, or Ellipse. Case-insensitive.")] string entity_type = null,
            [Description("Optional layer name filter. Case-insensitive.")] string layer = null,
            [Description("Optional ACI color index filter. Valid range: 1-256.")] int? color_index = null)
        {
            var request = BuildEntityQueryRequest(entity_type, layer, color_index, limit: null);
            return ToolGateway.LoggedCall("count_entities", request, request);
        }

        [McpServerTool(Name = "dwg_select_by_layer", ReadOnly = true, Idempotent = true), Description(
            "Return handles for model-space entities on a required layer, with optional type/color filters. " +
            "This does not change AutoCAD pickfirst selection.")]
        public static Task<string> SelectByLayer(
            [Description("Required layer name filter. Case-insensitive.")] string layer,
            [Description("Optional entity type filter. Case-insensitive.")] string entity_type = null,
            [Description("Optional ACI color index filter. Valid range: 1-256.")] int? color_index = null,
            [Description("Optional maximum number of returned handles. The plugin clamps this to 1-5000.")] int? limit = null)
        {
            var request = BuildEntityQueryRequest(entity_type, layer, color_index, limit);
            return ToolGateway.LoggedCall("select_by_layer", request, request);
        }

        [McpServerTool(Name = "dwg_select_by_type", ReadOnly = true, Idempotent = true), Description(
            "Return handles for model-space entities of a required type, with optional layer/color filters. " +
            "This does not change AutoCAD pickfirst selection.")]
        public static Task<string> SelectByType(
            [Description("Required entity type filter such as Line, Circle, Polyline, DBText, MText, BlockReference, Hatch, Arc, or Ellipse. Case-insensitive.")] string entity_type,
            [Description("Optional layer name filter. Case-insensitive.")] string layer = null,
            [Description("Optional ACI color index filter. Valid range: 1-256.")] int? color_index = null,
            [Description("Optional maximum number of returned handles. The plugin clamps this to 1-5000.")] int? limit = null)
        {
            var request = BuildEntityQueryRequest(entity_type, layer, color_index, limit);
            return ToolGateway.LoggedCall("select_by_type", request, request);
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

        private static JObject BuildEntityQueryRequest(
            string entityType,
            string layer,
            int? colorIndex,
            int? limit)
        {
            var request = new JObject();
            if (!string.IsNullOrWhiteSpace(entityType))
            {
                request["entity_type"] = entityType;
            }

            if (!string.IsNullOrWhiteSpace(layer))
            {
                request["layer"] = layer;
            }

            if (colorIndex.HasValue)
            {
                request["color_index"] = colorIndex.Value;
            }

            if (limit.HasValue)
            {
                request["limit"] = limit.Value;
            }

            return request;
        }

        private static Task<string> ToolInputError(string error)
        {
            return Task.FromResult(JsonConvert.SerializeObject(new { ok = false, error }));
        }
    }
}
