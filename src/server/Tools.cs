using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server
{
    [McpServerToolType]
    public class Tools
    {
        private static readonly PluginClient Client = PluginClient.FromDiscoveryFile();

        private static async Task<string> LoggedCall(string toolName, object inputParams, object pluginParams)
        {
            var requestId = Guid.NewGuid().ToString("N");
            var sw = Stopwatch.StartNew();
            string paramsJson = SafeSerialize(inputParams);
            ServerLogger.LogStart(requestId, toolName, paramsJson);
            try
            {
                var resp = await Client.SendAsync(toolName, pluginParams, requestId);
                sw.Stop();
                ServerLogger.LogFinish(requestId, toolName, resp.Ok, sw.ElapsedMilliseconds, resp.Error);
                return JsonConvert.SerializeObject(resp);
            }
            catch (Exception ex)
            {
                sw.Stop();
                ServerLogger.LogFinish(requestId, toolName, false, sw.ElapsedMilliseconds, ex.Message);
                throw;
            }
        }

        private static string SafeSerialize(object o)
        {
            try { return JsonConvert.SerializeObject(o); }
            catch { return "<unserializable>"; }
        }

        [McpServerTool, Description(
            "Read and cluster text entities currently selected in AutoCAD. " +
            "Returns pre-clustered groups with combined text in reading order. " +
            "Each cluster has id, text, entity_count, in_block (bool) and " +
            "rewrite_mode (one of: update, collapse, rewrite_in_block) — the " +
            "mode that translate_and_rewrite will apply if given a translation " +
            "for this cluster. Use this BEFORE translate_and_rewrite. The user " +
            "must select entities in AutoCAD BEFORE calling this tool.")]
        public static Task<string> GetSelectedTexts(
            [Description("Optional grouping strength: weak, normal, or strong. Use weak when an image/layout reference indicates nearby labels should stay separate.")] string groupingStrength = "normal",
            [Description("When true, include each child text entity's handle/text/position so callers can recover from over-grouped clusters.")] bool includeEntities = false)
        {
            var request = new { grouping_strength = groupingStrength, include_entities = includeEntities };
            return LoggedCall("get_selected_texts", request, request);
        }

        [McpServerTool, Description(
            "Write new text to AutoCAD entities identified by handle. " +
            "Input is a JSON array of {handle, new_text} objects. " +
            "Returns per-item {handle, ok, error} — failures do not abort siblings. " +
            "Handles are hex strings as returned by get_selected_texts. " +
            "All items are written in a single transaction so one Ctrl+Z undoes the batch. " +
            "If applyUnicodeStyle=true, the same call also reassigns successfully updated " +
            "entities to the Unicode text style.")]
        public static Task<string> UpdateTexts(
            [Description("JSON array: [{\"handle\":\"2A4F\",\"new_text\":\"...\"}]")] string items,
            [Description("When true, apply the Unicode text style to successfully updated entities in the same tool call.")] bool applyUnicodeStyle = false)
        {
            var parsed = JsonConvert.DeserializeObject<UpdateTextItem[]>(items) ?? Array.Empty<UpdateTextItem>();
            var request = new UpdateTextsRequest { Items = parsed, ApplyUnicodeStyle = applyUnicodeStyle };
            return LoggedCall("update_texts", request, request);
        }

        [McpServerTool, Description(
            "PREFERRED translation tool. Writes translated text back to AutoCAD. " +
            "Input is a JSON array of {id, new_text, render_mode?, width_policy?} where id matches a cluster " +
            "from get_selected_texts. The tool handles everything automatically: " +
            "anchor selection, fragment deletion, MText conversion (when safe), " +
            "Unicode font style, and height scaling. Optional render_mode='mtext' " +
            "lets the caller force a safe single DBText or top-level cluster to end up as MText. Simply provide the " +
            "translated text for each cluster. If a cluster needs no translation " +
            "(pure numbers, elevation markers), omit it — the tool will still " +
            "apply the Unicode style to it. Per-cluster response includes an " +
            "'action' field: update | collapse | rewrite_in_block | style_only. " +
            "Workflow: get_selected_texts → translate each cluster → translate_and_rewrite. " +
            "Use collapse_and_rewrite (low-level) only for expert control or " +
            "replaying call-log scenarios.")]
        public static Task<string> TranslateAndRewrite(
            [Description("JSON array: [{\"id\":0,\"new_text\":\"translated text\",\"render_mode\":\"mtext\",\"width_policy\":\"preserve\"}]")] string translations,
            [Description("Optional per-request final text-height multiplier. Default 0.80; clamped to [0.5, 0.9]. Values outside the range snap to the nearest bound; 0 or NaN fall back to default.")] double finalScale = 0.80)
        {
            var parsed = JsonConvert.DeserializeObject<TranslationItem[]>(translations) ?? Array.Empty<TranslationItem>();
            var request = new TranslateRequest { Translations = parsed, FinalScale = finalScale };
            return LoggedCall("translate_and_rewrite", request, request);
        }

        [McpServerTool, Description(
            "Execute a C# snippet against the AutoCAD .NET API as an escape hatch. " +
            "WARNING: send_code runs arbitrary code with full access to the AutoCAD process " +
            "and local filesystem. Only use with trusted agents. " +
            "Globals available: Document doc, Database db, Editor ed. " +
            "Use System.Console.WriteLine for output. 30s timeout. " +
            "Prefer the specialized tools (get_selected_texts / update_texts / " +
            "collapse_and_rewrite / apply_unicode_style) for text operations.")]
        public static Task<string> SendCode(
            [Description("C# code to execute")] string code)
            => LoggedCall("send_code", new { code }, new { code });

        [McpServerTool, Description(
            "Ensure the 'Bimwright_Unicode' text style exists (using Open Sans Condensed Light font, " +
            "auto-downloading OpenSans-CondensedLight.ttf from GitHub to " +
            "%LOCALAPPDATA%\\Bimwright\\Fonts\\ if not already installed) and reassign " +
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
            return LoggedCall("apply_unicode_style", logInput, pluginParams);
        }

        [McpServerTool, Description(
            "LOW-LEVEL rewrite primitive. PREFER translate_and_rewrite for the " +
            "standard translation workflow — this tool exists for expert control, " +
            "regression replay from mcp-calls.jsonl, and future non-translation " +
            "orchestrators. Accepts explicit per-cluster instructions " +
            "{anchor_handle, new_text, delete_handles, convert_to_mtext, mtext_width}. " +
            "For each cluster: anchor_handle is the fragment to keep (typically " +
            "topmost-leftmost), new_text is the full rewritten sentence " +
            "(may contain \\\\P line breaks for multi-line), delete_handles are all " +
            "other fragments in the cluster (will be erased), convert_to_mtext=true " +
            "upgrades DBText→MText for natural word wrap (only safe in model space, " +
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
            return LoggedCall("collapse_and_rewrite", parsed, parsed);
        }
    }
}
