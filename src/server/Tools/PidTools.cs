using System.ComponentModel;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server.Tools
{
    [McpServerToolType]
    public class PidTools
    {
        [McpServerTool(Name = "dwg_pid_setup_layers"), Description(
            "Setup standard P&ID layers. Optional include_wwtp_layers creates additional WWTP-specific layers.")]
        public static Task<string> SetupLayers(
            [Description("Whether to include wastewater treatment plant specific layers.")] bool? include_wwtp_layers = null)
        {
            var request = new JObject();
            if (include_wwtp_layers.HasValue) request["include_wwtp_layers"] = include_wwtp_layers.Value;

            return ToolGateway.LoggedCall("pid_setup_layers", request, request);
        }

        [McpServerTool(Name = "dwg_pid_list_categories"), Description(
            "List all P&ID standard fallback categories.")]
        public static Task<string> ListCategories()
        {
            var request = new JObject();
            return ToolGateway.LoggedCall("pid_list_categories", request, request);
        }

        [McpServerTool(Name = "dwg_pid_list_symbols"), Description(
            "List P&ID symbols for a specific category.")]
        public static Task<string> ListSymbols(
            [Description("The category to list symbols for.")] string category)
        {
            var request = new JObject
            {
                ["category"] = category
            };

            return ToolGateway.LoggedCall("pid_list_symbols", request, request);
        }

        [McpServerTool(Name = "dwg_pid_draw_pipe"), Description(
            "Draw process/utility piping between start and end coordinates. start and end are JSON point objects " +
            "with numeric x, y, and optional z fields.")]
        public static Task<string> DrawPipe(
            [Description("JSON point object, e.g. {\"x\":0,\"y\":0,\"z\":0}.")] string start,
            [Description("JSON point object, e.g. {\"x\":10,\"y\":10,\"z\":0}.")] string end,
            [Description("Optional target layer name (defaults to PID-PROCESS-PIPING).")] string layer = null)
        {
            if (!TryParseJsonObject(start, "start", out var startObject, out var startError))
            {
                return ToolInputError(startError);
            }
            if (!TryParseJsonObject(end, "end", out var endObject, out var endError))
            {
                return ToolInputError(endError);
            }

            var request = new JObject
            {
                ["start"] = startObject,
                ["end"] = endObject
            };
            if (layer != null) request["layer"] = layer;

            return ToolGateway.LoggedCall("pid_draw_pipe", request, request);
        }

        [McpServerTool(Name = "dwg_pid_insert_symbol"), Description(
            "Procedural P&ID symbol insertion at specified position, scale, and rotation. position is a JSON point object.")]
        public static Task<string> InsertSymbol(
            [Description("The symbol's category.")] string category,
            [Description("The symbol name to insert.")] string symbol,
            [Description("JSON point object, e.g. {\"x\":0,\"y\":0,\"z\":0}.")] string position,
            [Description("Optional scale factor.")] double? scale = null,
            [Description("Optional rotation angle in degrees.")] double? rotation = null,
            [Description("Optional text contents associated with the symbol.")] string text_content = null)
        {
            if (!TryParseJsonObject(position, "position", out var positionObject, out var positionError))
            {
                return ToolInputError(positionError);
            }

            var request = new JObject
            {
                ["category"] = category,
                ["symbol"] = symbol,
                ["position"] = positionObject
            };
            if (scale.HasValue) request["scale"] = scale.Value;
            if (rotation.HasValue) request["rotation"] = rotation.Value;
            if (text_content != null) request["text_content"] = text_content;

            return ToolGateway.LoggedCall("pid_insert_symbol", request, request);
        }

        [McpServerTool(Name = "dwg_pid_add_flow_arrow"), Description(
            "Draw a flow arrow polyline pointing in the specified direction vector. position and direction are JSON point/vector objects.")]
        public static Task<string> AddFlowArrow(
            [Description("JSON point object, e.g. {\"x\":0,\"y\":0,\"z\":0}.")] string position,
            [Description("JSON vector object, e.g. {\"x\":1,\"y\":0,\"z\":0}.")] string direction,
            [Description("Optional target layer.")] string layer = null)
        {
            if (!TryParseJsonObject(position, "position", out var positionObject, out var positionError))
            {
                return ToolInputError(positionError);
            }
            if (!TryParseJsonObject(direction, "direction", out var directionObject, out var directionError))
            {
                return ToolInputError(directionError);
            }

            var request = new JObject
            {
                ["position"] = positionObject,
                ["direction"] = directionObject
            };
            if (layer != null) request["layer"] = layer;

            return ToolGateway.LoggedCall("pid_add_flow_arrow", request, request);
        }

        [McpServerTool(Name = "dwg_pid_add_equipment_tag"), Description(
            "Add equipment tag text at specified position. position is a JSON point object.")]
        public static Task<string> AddEquipmentTag(
            [Description("JSON point object, e.g. {\"x\":0,\"y\":0,\"z\":0}.")] string position,
            [Description("Text label for the equipment.")] string tag_text,
            [Description("Optional target layer (defaults to PID-ANNOTATION).")] string layer = null)
        {
            if (!TryParseJsonObject(position, "position", out var positionObject, out var positionError))
            {
                return ToolInputError(positionError);
            }

            var request = new JObject
            {
                ["position"] = positionObject,
                ["tag_text"] = tag_text
            };
            if (layer != null) request["layer"] = layer;

            return ToolGateway.LoggedCall("pid_add_equipment_tag", request, request);
        }

        [McpServerTool(Name = "dwg_pid_add_line_number"), Description(
            "Add pipe line number text at specified position. position is a JSON point object.")]
        public static Task<string> AddLineNumber(
            [Description("JSON point object, e.g. {\"x\":0,\"y\":0,\"z\":0}.")] string position,
            [Description("Text label for the pipe line number.")] string line_text,
            [Description("Optional target layer (defaults to PID-ANNOTATION).")] string layer = null)
        {
            if (!TryParseJsonObject(position, "position", out var positionObject, out var positionError))
            {
                return ToolInputError(positionError);
            }

            var request = new JObject
            {
                ["position"] = positionObject,
                ["line_text"] = line_text
            };
            if (layer != null) request["layer"] = layer;

            return ToolGateway.LoggedCall("pid_add_line_number", request, request);
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
