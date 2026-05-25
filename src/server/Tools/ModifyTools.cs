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

        private static Task<string> ToolInputError(string error)
        {
            return Task.FromResult(JsonConvert.SerializeObject(new { ok = false, error }));
        }
    }
}
