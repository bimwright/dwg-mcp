using System.ComponentModel;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server.Tools
{
    [McpServerToolType]
    public class DimensionTools
    {
        [McpServerTool(Name = "dwg_create_linear_dimension"), Description(
            "Create a linear dimension. start, end, and dimension_line_point are JSON point objects.")]
        public static Task<string> CreateLinearDimension(
            [Description("JSON point object, e.g. {\"x\":0,\"y\":0,\"z\":0}.")] string start,
            [Description("JSON point object, e.g. {\"x\":1000,\"y\":0,\"z\":0}.")] string end,
            [Description("JSON point object for the dimension line location.")] string dimension_line_point,
            [Description("Optional target layer name.")] string layer = null,
            [Description("Optional dimension style name.")] string style_name = null)
        {
            if (!TryParseJsonObject(start, "start", out var startObject, out var startError))
            {
                return ToolInputError(startError);
            }
            if (!TryParseJsonObject(end, "end", out var endObject, out var endError))
            {
                return ToolInputError(endError);
            }
            if (!TryParseJsonObject(dimension_line_point, "dimension_line_point", out var dimensionLinePointObject, out var dimensionLinePointError))
            {
                return ToolInputError(dimensionLinePointError);
            }

            var request = BuildTwoPointDimensionRequest(
                startObject,
                endObject,
                dimensionLinePointObject,
                layer,
                style_name);

            return ToolGateway.LoggedCall("create_linear_dimension", request, request);
        }

        [McpServerTool(Name = "dwg_create_aligned_dimension"), Description(
            "Create an aligned dimension. start, end, and dimension_line_point are JSON point objects.")]
        public static Task<string> CreateAlignedDimension(
            [Description("JSON point object, e.g. {\"x\":0,\"y\":0,\"z\":0}.")] string start,
            [Description("JSON point object, e.g. {\"x\":1000,\"y\":500,\"z\":0}.")] string end,
            [Description("JSON point object for the dimension line location.")] string dimension_line_point,
            [Description("Optional target layer name.")] string layer = null,
            [Description("Optional dimension style name.")] string style_name = null)
        {
            if (!TryParseJsonObject(start, "start", out var startObject, out var startError))
            {
                return ToolInputError(startError);
            }
            if (!TryParseJsonObject(end, "end", out var endObject, out var endError))
            {
                return ToolInputError(endError);
            }
            if (!TryParseJsonObject(dimension_line_point, "dimension_line_point", out var dimensionLinePointObject, out var dimensionLinePointError))
            {
                return ToolInputError(dimensionLinePointError);
            }

            var request = BuildTwoPointDimensionRequest(
                startObject,
                endObject,
                dimensionLinePointObject,
                layer,
                style_name);

            return ToolGateway.LoggedCall("create_aligned_dimension", request, request);
        }

        [McpServerTool(Name = "dwg_create_radial_dimension"), Description(
            "Create a radial dimension for an existing entity. dimension_line_point is a JSON point object.")]
        public static Task<string> CreateRadialDimension(
            [Description("AutoCAD handle of the circle or arc entity to dimension.")] string entity_handle,
            [Description("JSON point object for the dimension line location.")] string dimension_line_point,
            [Description("Optional target layer name.")] string layer = null,
            [Description("Optional dimension style name.")] string style_name = null)
        {
            if (!TryParseJsonObject(dimension_line_point, "dimension_line_point", out var dimensionLinePointObject, out var dimensionLinePointError))
            {
                return ToolInputError(dimensionLinePointError);
            }

            var request = BuildEntityDimensionRequest(
                entity_handle,
                dimensionLinePointObject,
                layer,
                style_name);

            return ToolGateway.LoggedCall("create_radial_dimension", request, request);
        }

        [McpServerTool(Name = "dwg_create_diameter_dimension"), Description(
            "Create a diameter dimension for an existing entity. dimension_line_point is a JSON point object.")]
        public static Task<string> CreateDiameterDimension(
            [Description("AutoCAD handle of the circle or arc entity to dimension.")] string entity_handle,
            [Description("JSON point object for the dimension line location.")] string dimension_line_point,
            [Description("Optional target layer name.")] string layer = null,
            [Description("Optional dimension style name.")] string style_name = null)
        {
            if (!TryParseJsonObject(dimension_line_point, "dimension_line_point", out var dimensionLinePointObject, out var dimensionLinePointError))
            {
                return ToolInputError(dimensionLinePointError);
            }

            var request = BuildEntityDimensionRequest(
                entity_handle,
                dimensionLinePointObject,
                layer,
                style_name);

            return ToolGateway.LoggedCall("create_diameter_dimension", request, request);
        }

        private static JObject BuildTwoPointDimensionRequest(
            JObject start,
            JObject end,
            JObject dimensionLinePoint,
            string layer,
            string styleName)
        {
            var request = new JObject
            {
                ["start"] = start,
                ["end"] = end,
                ["dimension_line_point"] = dimensionLinePoint
            };
            if (layer != null) request["layer"] = layer;
            if (styleName != null) request["style_name"] = styleName;
            return request;
        }

        private static JObject BuildEntityDimensionRequest(
            string entityHandle,
            JObject dimensionLinePoint,
            string layer,
            string styleName)
        {
            var request = new JObject
            {
                ["entity_handle"] = entityHandle,
                ["dimension_line_point"] = dimensionLinePoint
            };
            if (layer != null) request["layer"] = layer;
            if (styleName != null) request["style_name"] = styleName;
            return request;
        }

        private static bool TryParseJsonObject(string json, string fieldName, out JObject obj, out string error)
        {
            obj = null;
            error = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = fieldName + " must be a JSON object";
                return false;
            }

            try
            {
                obj = JObject.Parse(json);
                return true;
            }
            catch (JsonException ex)
            {
                error = fieldName + " must be a JSON object: " + ex.Message;
                return false;
            }
        }

        private static Task<string> ToolInputError(string error)
        {
            return Task.FromResult(JsonConvert.SerializeObject(new { ok = false, error }));
        }
    }
}
