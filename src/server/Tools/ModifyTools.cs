using System;
using System.ComponentModel;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server.Tools
{
    [McpServerToolType]
    public class ModifyTools
    {
        [McpServerTool(Name = "dwg_update_texts"), Description(
            "Write new text to AutoCAD entities identified by handle. " +
            "Input is a JSON array of {handle, new_text} objects. " +
            "Returns per-item {handle, ok, error} - failures do not abort siblings. " +
            "Handles are hex strings as returned by dwg_get_selected_texts. " +
            "All items are written in a single transaction so one Ctrl+Z undoes the batch. " +
            "If applyUnicodeStyle=true, the same call also reassigns successfully updated " +
            "entities to the Unicode text style.")]
        public static Task<string> UpdateTexts(
            [Description("JSON array: [{\"handle\":\"2A4F\",\"new_text\":\"...\"}]")] string items,
            [Description("When true, apply the Unicode text style to successfully updated entities in the same tool call.")] bool applyUnicodeStyle = false)
        {
            var parsed = JsonConvert.DeserializeObject<UpdateTextItem[]>(items) ?? Array.Empty<UpdateTextItem>();
            var request = new UpdateTextsRequest { Items = parsed, ApplyUnicodeStyle = applyUnicodeStyle };
            return ToolGateway.LoggedCall("update_texts", request, request);
        }

        [McpServerTool(Name = "dwg_create_layer"), Description(
            "Ensure an AutoCAD layer exists. If the layer already exists, its existing " +
            "color and state are left unchanged and the response reports created=false.")]
        public static Task<string> CreateLayer(
            [Description("Layer name to ensure.")] string name,
            [Description("Optional ACI color index used only when creating a missing layer. Valid range: 1-256. Default: 7.")] int? color_index = null)
        {
            var request = new JObject
            {
                ["name"] = name
            };
            if (color_index.HasValue)
            {
                request["color_index"] = color_index.Value;
            }

            return ToolGateway.LoggedCall("create_layer", request, request);
        }

        [McpServerTool(Name = "dwg_create_line"), Description(
            "Create a Line in the current AutoCAD space. start and end are JSON point " +
            "objects with numeric x, y, and optional z fields. Optional layer is ensured " +
            "before assignment; optional color_index sets the entity ACI color.")]
        public static Task<string> CreateLine(
            [Description("JSON point object, e.g. {\"x\":0,\"y\":0,\"z\":0}.")] string start,
            [Description("JSON point object, e.g. {\"x\":1000,\"y\":0,\"z\":0}.")] string end,
            [Description("Optional target layer name. If supplied, the layer is ensured using color_index or default 7.")] string layer = null,
            [Description("Optional ACI color index for the new entity, and for creating a supplied missing layer. Valid range: 1-256.")] int? color_index = null)
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
            if (layer != null)
            {
                request["layer"] = layer;
            }
            if (color_index.HasValue)
            {
                request["color_index"] = color_index.Value;
            }

            return ToolGateway.LoggedCall("create_line", request, request);
        }

        [McpServerTool(Name = "dwg_create_circle"), Description(
            "Create a Circle in the current AutoCAD space. center is a JSON point object " +
            "with numeric x, y, and optional z fields. radius must be positive and finite.")]
        public static Task<string> CreateCircle(
            [Description("JSON point object, e.g. {\"x\":0,\"y\":0,\"z\":0}.")] string center,
            [Description("Circle radius. Must be positive and finite.")] double radius,
            [Description("Optional target layer name. If supplied, the layer is ensured using color_index or default 7.")] string layer = null,
            [Description("Optional ACI color index for the new entity, and for creating a supplied missing layer. Valid range: 1-256.")] int? color_index = null)
        {
            if (!TryParseJsonObject(center, "center", out var centerObject, out var centerError))
            {
                return ToolInputError(centerError);
            }

            var request = new JObject
            {
                ["center"] = centerObject,
                ["radius"] = radius
            };
            if (layer != null)
            {
                request["layer"] = layer;
            }
            if (color_index.HasValue)
            {
                request["color_index"] = color_index.Value;
            }

            return ToolGateway.LoggedCall("create_circle", request, request);
        }

        [McpServerTool(Name = "dwg_create_point"), Description(
            "Create a Point in the current AutoCAD space. point is a JSON point object " +
            "with numeric x, y, and optional z fields. Optional layer is ensured before " +
            "assignment; optional color_index sets the entity ACI color.")]
        public static Task<string> CreatePoint(
            [Description("JSON point object, e.g. {\"x\":0,\"y\":0,\"z\":0}.")] string point,
            [Description("Optional target layer name. If supplied, the layer is ensured using color_index or default 7.")] string layer = null,
            [Description("Optional ACI color index for the new entity, and for creating a supplied missing layer. Valid range: 1-256.")] int? color_index = null)
        {
            if (!TryParseJsonObject(point, "point", out var pointObject, out var pointError))
            {
                return ToolInputError(pointError);
            }

            var request = new JObject
            {
                ["point"] = pointObject
            };
            if (layer != null)
            {
                request["layer"] = layer;
            }
            if (color_index.HasValue)
            {
                request["color_index"] = color_index.Value;
            }

            return ToolGateway.LoggedCall("create_point", request, request);
        }

        [McpServerTool(Name = "dwg_create_polyline"), Description(
            "Create a 2D Polyline in the current AutoCAD space. points is a JSON array " +
            "of point objects with numeric x, y, and optional z fields. At least two " +
            "points are required.")]
        public static Task<string> CreatePolyline(
            [Description("JSON point array, e.g. [{\"x\":0,\"y\":0},{\"x\":1000,\"y\":0}].")] string points,
            [Description("When true, close the polyline.")] bool closed = false,
            [Description("Optional target layer name. If supplied, the layer is ensured using color_index or default 7.")] string layer = null,
            [Description("Optional ACI color index for the new entity, and for creating a supplied missing layer. Valid range: 1-256.")] int? color_index = null)
        {
            if (!TryParseJsonArray(points, "points", out var pointsArray, out var pointsError))
            {
                return ToolInputError(pointsError);
            }

            var request = new JObject
            {
                ["points"] = pointsArray,
                ["closed"] = closed
            };
            if (layer != null)
            {
                request["layer"] = layer;
            }
            if (color_index.HasValue)
            {
                request["color_index"] = color_index.Value;
            }

            return ToolGateway.LoggedCall("create_polyline", request, request);
        }

        [McpServerTool(Name = "dwg_create_rectangle"), Description(
            "Create a Rectangle as a closed Polyline in the current AutoCAD space. " +
            "corner1 and corner2 are JSON point objects with numeric x, y, and optional z fields.")]
        public static Task<string> CreateRectangle(
            [Description("JSON point object, e.g. {\"x\":0,\"y\":0,\"z\":0}.")] string corner1,
            [Description("JSON point object, e.g. {\"x\":1000,\"y\":500,\"z\":0}.")] string corner2,
            [Description("Optional target layer name. If supplied, the layer is ensured using color_index or default 7.")] string layer = null,
            [Description("Optional ACI color index for the new entity, and for creating a supplied missing layer. Valid range: 1-256.")] int? color_index = null)
        {
            if (!TryParseJsonObject(corner1, "corner1", out var corner1Object, out var corner1Error))
            {
                return ToolInputError(corner1Error);
            }

            if (!TryParseJsonObject(corner2, "corner2", out var corner2Object, out var corner2Error))
            {
                return ToolInputError(corner2Error);
            }

            var request = new JObject
            {
                ["corner1"] = corner1Object,
                ["corner2"] = corner2Object
            };
            if (layer != null)
            {
                request["layer"] = layer;
            }
            if (color_index.HasValue)
            {
                request["color_index"] = color_index.Value;
            }

            return ToolGateway.LoggedCall("create_rectangle", request, request);
        }

        [McpServerTool(Name = "dwg_create_arc"), Description(
            "Create an Arc in the current AutoCAD space. center is a JSON point object. " +
            "radius must be positive and finite. start_angle and end_angle are degrees.")]
        public static Task<string> CreateArc(
            [Description("JSON point object, e.g. {\"x\":0,\"y\":0,\"z\":0}.")] string center,
            [Description("Arc radius. Must be positive and finite.")] double radius,
            [Description("Start angle in degrees.")] double start_angle,
            [Description("End angle in degrees.")] double end_angle,
            [Description("Optional target layer name. If supplied, the layer is ensured using color_index or default 7.")] string layer = null,
            [Description("Optional ACI color index for the new entity, and for creating a supplied missing layer. Valid range: 1-256.")] int? color_index = null)
        {
            if (!TryParseJsonObject(center, "center", out var centerObject, out var centerError))
            {
                return ToolInputError(centerError);
            }

            var request = new JObject
            {
                ["center"] = centerObject,
                ["radius"] = radius,
                ["start_angle"] = start_angle,
                ["end_angle"] = end_angle
            };
            if (layer != null)
            {
                request["layer"] = layer;
            }
            if (color_index.HasValue)
            {
                request["color_index"] = color_index.Value;
            }

            return ToolGateway.LoggedCall("create_arc", request, request);
        }

        [McpServerTool(Name = "dwg_create_ellipse"), Description(
            "Create an Ellipse in the current AutoCAD space. center is a JSON point object. " +
            "major_radius and minor_radius must be positive and finite. rotation is degrees.")]
        public static Task<string> CreateEllipse(
            [Description("JSON point object, e.g. {\"x\":0,\"y\":0,\"z\":0}.")] string center,
            [Description("Major radius. Must be positive and finite.")] double major_radius,
            [Description("Minor radius. Must be positive, finite, and less than or equal to major_radius.")] double minor_radius,
            [Description("Major-axis rotation in degrees.")] double rotation,
            [Description("Optional target layer name. If supplied, the layer is ensured using color_index or default 7.")] string layer = null,
            [Description("Optional ACI color index for the new entity, and for creating a supplied missing layer. Valid range: 1-256.")] int? color_index = null)
        {
            if (!TryParseJsonObject(center, "center", out var centerObject, out var centerError))
            {
                return ToolInputError(centerError);
            }

            var request = new JObject
            {
                ["center"] = centerObject,
                ["major_radius"] = major_radius,
                ["minor_radius"] = minor_radius,
                ["rotation"] = rotation
            };
            if (layer != null)
            {
                request["layer"] = layer;
            }
            if (color_index.HasValue)
            {
                request["color_index"] = color_index.Value;
            }

            return ToolGateway.LoggedCall("create_ellipse", request, request);
        }

        [McpServerTool(Name = "dwg_change_layer"), Description(
            "Move entities identified by handle to an existing layer. If create_layer=true, " +
            "the layer is ensured first using color_index or default 7. Returns one result " +
            "record per handle; bad handles do not abort siblings.")]
        public static Task<string> ChangeLayer(
            [Description("JSON array of AutoCAD handles, e.g. [\"7F5AD\",\"2A4F\"].")] string handles,
            [Description("Target layer name. Must exist unless create_layer=true.")] string layer,
            [Description("When true, ensure the target layer before moving entities.")] bool create_layer = false,
            [Description("Optional ACI color index used only when create_layer=true creates a missing layer. Valid range: 1-256. Default: 7.")] int? color_index = null)
        {
            if (string.IsNullOrWhiteSpace(handles))
            {
                return ToolInputError("handles must be a JSON array");
            }

            JArray parsedHandles;
            try
            {
                parsedHandles = JArray.Parse(handles);
            }
            catch (JsonException ex)
            {
                return ToolInputError("handles must be a JSON array: " + ex.Message);
            }

            var request = new JObject
            {
                ["handles"] = parsedHandles,
                ["layer"] = layer,
                ["create_layer"] = create_layer
            };
            if (color_index.HasValue)
            {
                request["color_index"] = color_index.Value;
            }

            return ToolGateway.LoggedCall("change_layer", request, request);
        }

        [McpServerTool(Name = "dwg_change_color"), Description(
            "Apply an AutoCAD ACI color index to entities identified by handle. handles is a JSON array " +
            "of AutoCAD handle strings. colorIndex is validated by AutoCAD-side handling as ACI range 1-256. " +
            "Returns per-item {handle, ok, error}; bad handles do not abort siblings.")]
        public static Task<string> ChangeColor(
            [Description("JSON array of AutoCAD handles, e.g. [\"7F5AD\",\"2A4F\"].")] string handles,
            [Description("ACI color index to apply. Valid range: 1-256.")] int colorIndex)
        {
            if (!TryParseJsonArray(handles, "handles", out var handlesArray, out var handlesError))
            {
                return ToolInputError(handlesError);
            }

            var request = new JObject
            {
                ["handles"] = handlesArray,
                ["color_index"] = colorIndex
            };

            return ToolGateway.LoggedCall("change_color", request, request);
        }

        [McpServerTool(Name = "dwg_move_entities"), Description(
            "Move entities identified by handle by a displacement vector. handles is a JSON array " +
            "of AutoCAD handle strings. vector is a JSON point object with numeric x, y, and optional z fields. " +
            "Returns per-item {handle, ok, error}; bad handles do not abort siblings.")]
        public static Task<string> MoveEntities(
            [Description("JSON array of AutoCAD handles, e.g. [\"7F5AD\",\"2A4F\"].")] string handles,
            [Description("JSON vector object, e.g. {\"x\":100,\"y\":0,\"z\":0}.")] string vector)
        {
            if (!TryParseJsonArray(handles, "handles", out var handlesArray, out var handlesError))
            {
                return ToolInputError(handlesError);
            }

            if (!TryParseJsonObject(vector, "vector", out var vectorObject, out var vectorError))
            {
                return ToolInputError(vectorError);
            }

            var request = new JObject
            {
                ["handles"] = handlesArray,
                ["vector"] = vectorObject
            };

            return ToolGateway.LoggedCall("move_entities", request, request);
        }

        [McpServerTool(Name = "dwg_rotate_entities"), Description(
            "Rotate entities identified by handle around basePoint on the Z axis. handles is a JSON array " +
            "of AutoCAD handle strings. basePoint is a JSON point object, and angleDegrees is in degrees. " +
            "Returns per-item {handle, ok, error}; bad handles do not abort siblings.")]
        public static Task<string> RotateEntities(
            [Description("JSON array of AutoCAD handles, e.g. [\"7F5AD\",\"2A4F\"].")] string handles,
            [Description("JSON base point object, e.g. {\"x\":0,\"y\":0,\"z\":0}.")] string basePoint,
            [Description("Rotation angle in degrees.")] double angleDegrees)
        {
            if (!TryParseJsonArray(handles, "handles", out var handlesArray, out var handlesError))
            {
                return ToolInputError(handlesError);
            }

            if (!TryParseJsonObject(basePoint, "basePoint", out var basePointObject, out var basePointError))
            {
                return ToolInputError(basePointError);
            }

            var request = new JObject
            {
                ["handles"] = handlesArray,
                ["basePoint"] = basePointObject,
                ["angleDegrees"] = angleDegrees
            };

            return ToolGateway.LoggedCall("rotate_entities", request, request);
        }

        [McpServerTool(Name = "dwg_scale_entities"), Description(
            "Scale entities identified by handle around basePoint. handles is a JSON array " +
            "of AutoCAD handle strings. basePoint is a JSON point object. scale must be finite, positive, " +
            "and less than or equal to 1000. Returns per-item {handle, ok, error}; bad handles do not abort siblings.")]
        public static Task<string> ScaleEntities(
            [Description("JSON array of AutoCAD handles, e.g. [\"7F5AD\",\"2A4F\"].")] string handles,
            [Description("JSON base point object, e.g. {\"x\":0,\"y\":0,\"z\":0}.")] string basePoint,
            [Description("Scale factor. Must be finite, positive, and <= 1000.")] double scale)
        {
            if (!TryParseJsonArray(handles, "handles", out var handlesArray, out var handlesError))
            {
                return ToolInputError(handlesError);
            }

            if (!TryParseJsonObject(basePoint, "basePoint", out var basePointObject, out var basePointError))
            {
                return ToolInputError(basePointError);
            }

            var request = new JObject
            {
                ["handles"] = handlesArray,
                ["basePoint"] = basePointObject,
                ["scale"] = scale
            };

            return ToolGateway.LoggedCall("scale_entities", request, request);
        }

        [McpServerTool(Name = "dwg_copy_entities"), Description(
            "Copy entities identified by handle by a displacement vector. handles is a JSON array " +
            "of AutoCAD handle strings. vector is a JSON point object with numeric x, y, and optional z fields. " +
            "Returns per-item {handle, ok, new_handle, error}; bad handles do not abort siblings.")]
        public static Task<string> CopyEntities(
            [Description("JSON array of AutoCAD handles, e.g. [\"7F5AD\",\"2A4F\"].")] string handles,
            [Description("JSON vector object, e.g. {\"x\":100,\"y\":0,\"z\":0}.")] string vector)
        {
            if (!TryParseJsonArray(handles, "handles", out var handlesArray, out var handlesError))
            {
                return ToolInputError(handlesError);
            }

            if (!TryParseJsonObject(vector, "vector", out var vectorObject, out var vectorError))
            {
                return ToolInputError(vectorError);
            }

            var request = new JObject
            {
                ["handles"] = handlesArray,
                ["vector"] = vectorObject
            };

            return ToolGateway.LoggedCall("copy_entities", request, request);
        }

        [McpServerTool(Name = "dwg_erase_entities"), Description(
            "Erase entities identified by handle. handles is a JSON array of AutoCAD handle strings. " +
            "Returns per-item {handle, ok, error}; bad handles do not abort siblings.")]
        public static Task<string> EraseEntities(
            [Description("JSON array of AutoCAD handles, e.g. [\"7F5AD\",\"2A4F\"].")] string handles)
        {
            if (!TryParseJsonArray(handles, "handles", out var handlesArray, out var handlesError))
            {
                return ToolInputError(handlesError);
            }

            var request = new JObject
            {
                ["handles"] = handlesArray
            };

            return ToolGateway.LoggedCall("erase_entities", request, request);
        }

        [McpServerTool(Name = "dwg_offset_entities"), Description(
            "Offset curve entities identified by handle by distance. handles is a JSON array of AutoCAD handle strings. " +
            "Only Curve entities are supported. Returns per-item {handle, ok, created_handles, error}; bad handles " +
            "and unsupported entities do not abort siblings.")]
        public static Task<string> OffsetEntities(
            [Description("JSON array of AutoCAD handles, e.g. [\"7F5AD\",\"2A4F\"].")] string handles,
            [Description("Offset distance. Must be finite and non-zero.")] double distance)
        {
            if (!TryParseJsonArray(handles, "handles", out var handlesArray, out var handlesError))
            {
                return ToolInputError(handlesError);
            }

            var request = new JObject
            {
                ["handles"] = handlesArray,
                ["distance"] = distance
            };

            return ToolGateway.LoggedCall("offset_entities", request, request);
        }

        [McpServerTool(Name = "dwg_translate_and_rewrite"), Description(
            "PREFERRED translation tool. Writes translated text back to AutoCAD. " +
            "Input is a JSON array of {id, new_text, render_mode?, width_policy?} where id matches a cluster " +
            "from dwg_get_selected_texts. The tool handles everything automatically: " +
            "anchor selection, fragment deletion, MText conversion (when safe), " +
            "Unicode font style, and height scaling. Optional render_mode='mtext' " +
            "lets the caller force a safe single DBText or top-level cluster to end up as MText. Simply provide the " +
            "translated text for each cluster. If a cluster needs no translation " +
            "(pure numbers, elevation markers), omit it - the tool will still " +
            "apply the Unicode style to it. Per-cluster response includes an " +
            "'action' field: update | collapse | rewrite_in_block | style_only. " +
            "Workflow: dwg_get_selected_texts -> translate each cluster -> dwg_translate_and_rewrite. " +
            "Use dwg_collapse_and_rewrite (low-level) only for expert control or " +
            "replaying call-log scenarios.")]
        public static Task<string> TranslateAndRewrite(
            [Description("JSON array: [{\"id\":0,\"new_text\":\"translated text\",\"render_mode\":\"mtext\",\"width_policy\":\"preserve\"}]")] string translations,
            [Description("Optional per-request final text-height multiplier. Default 0.80; clamped to [0.5, 0.9]. Values outside the range snap to the nearest bound; 0 or NaN fall back to default.")] double finalScale = 0.80)
        {
            var parsed = JsonConvert.DeserializeObject<TranslationItem[]>(translations) ?? Array.Empty<TranslationItem>();
            var request = new TranslateRequest { Translations = parsed, FinalScale = finalScale };
            return ToolGateway.LoggedCall("translate_and_rewrite", request, request);
        }

        [McpServerTool(Name = "dwg_apply_unicode_style"), Description(
            "Ensure the 'Bimwright_Unicode' text style exists (using Open Sans Condensed Light font, " +
            "using the bundled font or a checksum-validated fallback download) and reassign " +
            "target entities to it. Height normalization is smart and idempotent: SHX " +
            "sources are reduced more than TrueType sources, while entities already on " +
            "the Unicode style keep their current height instead of shrinking again. " +
            "Targets: if 'handles' is a non-empty JSON array, those " +
            "entities are used; otherwise falls back to the current pickfirst selection. " +
            "MUST be called after translating text to Vietnamese or any non-ASCII language on " +
            "drawings that use SHX fonts lacking the required glyphs (otherwise text " +
            "renders as '?').")]
        public static Task<string> ApplyUnicodeStyle(
            [Description("Optional JSON array of target handles, e.g. [\"7F5AD\",\"2A4F\"]. Omit or pass \"\" to use the current pickfirst selection.")] string handles = "")
        {
            object pluginParams;
            object logInput;
            if (string.IsNullOrWhiteSpace(handles))
            {
                pluginParams = new { };
                logInput = new { source = "pickfirst" };
            }
            else
            {
                var parsed = JsonConvert.DeserializeObject<string[]>(handles);
                pluginParams = new { handles = parsed };
                logInput = new { handles = parsed };
            }
            return ToolGateway.LoggedCall("apply_unicode_style", logInput, pluginParams);
        }

        [McpServerTool(Name = "dwg_collapse_and_rewrite"), Description(
            "LOW-LEVEL rewrite primitive. PREFER translate_and_rewrite for the " +
            "standard translation workflow - this tool exists for expert control, " +
            "regression replay from mcp-calls.jsonl, and future non-translation " +
            "orchestrators. Accepts explicit per-cluster instructions " +
            "{anchor_handle, new_text, delete_handles, convert_to_mtext, mtext_width}. " +
            "For each cluster: anchor_handle is the fragment to keep (typically " +
            "topmost-leftmost), new_text is the full rewritten sentence " +
            "(may contain \\\\P line breaks for multi-line), delete_handles are all " +
            "other fragments in the cluster (will be erased), convert_to_mtext=true " +
            "upgrades DBText->MText for natural word wrap (only safe in model space, " +
            "not inside blocks), mtext_width is the cluster bounding box X-span " +
            "(only used if convert_to_mtext). Each cluster runs in its own " +
            "transaction (one failure does not roll back siblings). Response " +
            "per cluster includes an 'action' field: update | collapse | " +
            "rewrite_in_block. If applyUnicodeStyle=true, the same call reassigns " +
            "surviving entities to the Unicode text style and scales height.")]
        public static Task<string> CollapseAndRewrite(
            [Description("JSON: {\"clusters\":[{\"anchor_handle\":\"...\",\"new_text\":\"...\",\"delete_handles\":[\"...\"],\"convert_to_mtext\":true,\"mtext_width\":0}]}")] string clusters,
            [Description("When true, apply the Unicode text style to surviving entities in the same tool call.")] bool applyUnicodeStyle = false,
            [Description("Optional per-request final text-height multiplier. Default 0.80; clamped to [0.5, 0.9].")] double finalScale = 0.80)
        {
            var parsed = JsonConvert.DeserializeObject<JObject>(clusters)
                ?? throw new JsonSerializationException("clusters JSON parsed to null");
            parsed["apply_unicode_style"] = applyUnicodeStyle;
            parsed["final_scale"] = finalScale;
            return ToolGateway.LoggedCall("collapse_and_rewrite", parsed, parsed);
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
