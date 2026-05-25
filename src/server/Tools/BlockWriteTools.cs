using System.ComponentModel;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server.Tools
{
    [McpServerToolType]
    public class BlockWriteTools
    {
        [McpServerTool(Name = "dwg_insert_block"), Description(
            "Insert a block reference. insertion_point is a JSON point object. Optional attributes is a JSON object.")]
        public static Task<string> InsertBlock(
            [Description("Block definition name to insert.")] string block_name,
            [Description("JSON point object, e.g. {\"x\":0,\"y\":0,\"z\":0}.")] string insertion_point,
            [Description("Optional path to a DWG file to load before insertion.")] string block_path = null,
            [Description("Optional uniform scale factor.")] double? scale = null,
            [Description("Optional block rotation in degrees.")] double? rotation = null,
            [Description("Optional JSON object of attribute tag/value pairs.")] string attributes = null)
        {
            if (!TryParseJsonObject(insertion_point, "insertion_point", out var insertionPointObject, out var insertionPointError))
            {
                return ToolInputError(insertionPointError);
            }

            var request = new JObject
            {
                ["block_name"] = block_name,
                ["insertion_point"] = insertionPointObject
            };
            if (block_path != null) request["block_path"] = block_path;
            if (scale.HasValue) request["scale"] = scale.Value;
            if (rotation.HasValue) request["rotation"] = rotation.Value;
            if (!string.IsNullOrWhiteSpace(attributes))
            {
                if (!TryParseJsonObject(attributes, "attributes", out var attributesObject, out var attributesError))
                {
                    return ToolInputError(attributesError);
                }

                request["attributes"] = attributesObject;
            }

            return ToolGateway.LoggedCall("insert_block", request, request);
        }

        [McpServerTool(Name = "dwg_set_block_attributes"), Description(
            "Set attribute values on a block reference identified by handle. attributes is a JSON object.")]
        public static Task<string> SetBlockAttributes(
            [Description("AutoCAD handle of the block reference.")] string handle,
            [Description("JSON object of attribute tag/value pairs.")] string attributes)
        {
            if (!TryParseJsonObject(attributes, "attributes", out var attributesObject, out var attributesError))
            {
                return ToolInputError(attributesError);
            }

            var request = new JObject
            {
                ["handle"] = handle,
                ["attributes"] = attributesObject
            };

            return ToolGateway.LoggedCall("set_block_attributes", request, request);
        }

        [McpServerTool(Name = "dwg_explode_block"), Description(
            "Explode a block reference identified by handle.")]
        public static Task<string> ExplodeBlock(
            [Description("AutoCAD handle of the block reference.")] string handle)
        {
            var request = new JObject
            {
                ["handle"] = handle
            };

            return ToolGateway.LoggedCall("explode_block", request, request);
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
