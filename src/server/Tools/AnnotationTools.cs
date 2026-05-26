using System.ComponentModel;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server.Tools
{
    [McpServerToolType]
    public class AnnotationTools
    {
        [McpServerTool(Name = "dwg_create_text"), Description(
            "Create single-line text in the current AutoCAD space. position is a JSON point object " +
            "with numeric x, y, and optional z fields.")]
        public static Task<string> CreateText(
            [Description("Text content to create.")] string text,
            [Description("JSON point object, e.g. {\"x\":0,\"y\":0,\"z\":0}.")] string position,
            [Description("Optional text height.")] double? height = null,
            [Description("Optional text rotation in degrees.")] double? rotation = null,
            [Description("Optional target layer name.")] string layer = null,
            [Description("Optional ACI color index for the new entity. Valid range: 1-256.")] int? color_index = null)
        {
            if (!TryParseJsonObject(position, "position", out var positionObject, out var positionError))
            {
                return ToolInputError(positionError);
            }

            var request = new JObject
            {
                ["text"] = text,
                ["position"] = positionObject
            };
            if (height.HasValue) request["height"] = height.Value;
            if (rotation.HasValue) request["rotation"] = rotation.Value;
            if (layer != null) request["layer"] = layer;
            if (color_index.HasValue) request["color_index"] = color_index.Value;

            return ToolGateway.LoggedCall("create_text", request, request);
        }

        [McpServerTool(Name = "dwg_create_mtext"), Description(
            "Create multi-line text in the current AutoCAD space. location is a JSON point object " +
            "with numeric x, y, and optional z fields.")]
        public static Task<string> CreateMText(
            [Description("MText content to create.")] string text,
            [Description("JSON point object, e.g. {\"x\":0,\"y\":0,\"z\":0}.")] string location,
            [Description("Optional MText width.")] double? width = null,
            [Description("Optional text height.")] double? height = null,
            [Description("Optional text rotation in degrees.")] double? rotation = null,
            [Description("Optional target layer name.")] string layer = null,
            [Description("Optional ACI color index for the new entity. Valid range: 1-256.")] int? color_index = null)
        {
            if (!TryParseJsonObject(location, "location", out var locationObject, out var locationError))
            {
                return ToolInputError(locationError);
            }

            var request = new JObject
            {
                ["text"] = text,
                ["location"] = locationObject
            };
            if (width.HasValue) request["width"] = width.Value;
            if (height.HasValue) request["height"] = height.Value;
            if (rotation.HasValue) request["rotation"] = rotation.Value;
            if (layer != null) request["layer"] = layer;
            if (color_index.HasValue) request["color_index"] = color_index.Value;

            return ToolGateway.LoggedCall("create_mtext", request, request);
        }

        [McpServerTool(Name = "dwg_create_leader"), Description(
            "Create a leader annotation. points is a JSON array of point objects with numeric x, y, " +
            "and optional z fields.")]
        public static Task<string> CreateLeader(
            [Description("JSON point array, e.g. [{\"x\":0,\"y\":0},{\"x\":10,\"y\":5}].")] string points,
            [Description("Optional leader text.")] string text = null,
            [Description("Optional target layer name.")] string layer = null,
            [Description("Optional ACI color index for the new entity. Valid range: 1-256.")] int? color_index = null)
        {
            if (!TryParseJsonArray(points, "points", out var pointsArray, out var pointsError))
            {
                return ToolInputError(pointsError);
            }

            var request = new JObject
            {
                ["points"] = pointsArray
            };
            if (text != null) request["text"] = text;
            if (layer != null) request["layer"] = layer;
            if (color_index.HasValue) request["color_index"] = color_index.Value;

            return ToolGateway.LoggedCall("create_leader", request, request);
        }

        [McpServerTool(Name = "dwg_create_table"), Description(
            "Create a table in the current AutoCAD space. insertion_point is a JSON point object, " +
            "and cells is a JSON array of row arrays.")]
        public static Task<string> CreateTable(
            [Description("JSON point object, e.g. {\"x\":0,\"y\":0,\"z\":0}.")] string insertion_point,
            [Description("Number of table rows.")] int rows,
            [Description("Number of table columns.")] int columns,
            [Description("JSON array of row arrays, e.g. [[\"A\",\"B\"],[\"C\",\"D\"]].")] string cells,
            [Description("Optional target layer name.")] string layer = null)
        {
            if (!TryParseJsonObject(insertion_point, "insertion_point", out var insertionPointObject, out var insertionPointError))
            {
                return ToolInputError(insertionPointError);
            }

            if (!TryParseJsonArray(cells, "cells", out var cellsArray, out var cellsError))
            {
                return ToolInputError(cellsError);
            }

            var request = new JObject
            {
                ["insertion_point"] = insertionPointObject,
                ["rows"] = rows,
                ["columns"] = columns,
                ["cells"] = cellsArray
            };
            if (layer != null) request["layer"] = layer;

            return ToolGateway.LoggedCall("create_table", request, request);
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

        private static bool TryParseJsonArray(string json, string fieldName, out JArray array, out string error)
        {
            array = null;
            error = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = fieldName + " must be a JSON array";
                return false;
            }

            try
            {
                array = JArray.Parse(json);
                return true;
            }
            catch (JsonException ex)
            {
                error = fieldName + " must be a JSON array: " + ex.Message;
                return false;
            }
        }

        private static Task<string> ToolInputError(string error)
        {
            return Task.FromResult(JsonConvert.SerializeObject(new { ok = false, error }));
        }
    }
}
