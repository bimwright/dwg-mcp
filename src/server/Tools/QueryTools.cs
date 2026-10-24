using System.Collections.Generic;
using System;
using System.ComponentModel;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using Bimwright.Dwg.Plugin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server.Tools
{
    [McpServerToolType]
    public class QueryTools
    {
        [McpServerTool(Name = "dwg_get_drawing_info", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description(
            "Return small JSON-safe metadata for the current AutoCAD drawing: " +
            "drawing name when available, current layer, current space/layout, " +
            "and database unit scalars.")]
        public static Task<string> GetDrawingInfo()
        {
            var request = new { };
            return ToolGateway.LoggedCall("get_drawing_info", request, request);
        }

        [McpServerTool(Name = "dwg_get_drawing_profile", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description(
            "Profile the active drawing with a full read-only enumeration of top-level model-space and all layout entities. " +
            "Reports exact managed runtime type, layer and per-space counts with source/coverage; incomplete reads report observed counts and null exact totals. " +
            "Block inserts count once; nested block contents, uninserted definitions and xref contents are not expanded. " +
            "The default final MCP output cap is 16384 UTF-8 bytes, independent of the global response budget. " +
            "Content identifies omitted groups and next_calls for types/layers/spaces. Each page rescans; pass expected_snapshot from the previous result when offset is positive. " +
            "A changed document/revision refuses continuation. No geometry, selection or drawing changes.")]
        public static async Task<CallToolResult> GetDrawingProfile(
            [Description("summary (default), types, layers or spaces. Summary starts all tables at offset zero.")] string section = "summary",
            [Description("Ordinal group offset for the selected table, default 0; positive offsets require expected_snapshot.")] int offset = 0,
            [Description("Maximum final MCP result bytes, default 16384; supported range 4096..65536. A smaller global budget still applies.")] int max_output_bytes = DrawingProfileOptions.DefaultMaxOutputBytes,
            [Description("Snapshot token from a previous profile result. Required for positive offsets; mismatches require restarting at offset zero.")] string expected_snapshot = null)
        {
            try { DrawingProfileOptions.Create(section, offset, max_output_bytes, expected_snapshot); }
            catch (ArgumentException ex)
            {
                return DrawingProfileResponse.FromJson(JsonConvert.SerializeObject(new { ok = false, error = ex.Message }), DrawingProfileOptions.DefaultMaxOutputBytes);
            }
            var request = new JObject { ["section"] = section, ["offset"] = offset, ["max_output_bytes"] = max_output_bytes };
            if (expected_snapshot != null) request["expected_snapshot"] = expected_snapshot;
            return DrawingProfileResponse.FromJson(await ToolGateway.LoggedCall("get_drawing_profile", request, request), max_output_bytes);
        }

        [McpServerTool(Name = "dwg_get_entity_properties", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description(
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

        [McpServerTool(Name = "dwg_list_layers", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description(
            "List layers in the current AutoCAD drawing with small scalar properties " +
            "such as color index, locked/frozen/off state.")]
        public static Task<string> ListLayers()
        {
            var request = new { };
            return ToolGateway.LoggedCall("list_layers", request, request);
        }

        [McpServerTool(Name = "dwg_query_entities", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description(
            "Query model-space AutoCAD entities with optional filters for entity type, layer, and ACI color index. " +
            "Existing calls retain their legacy fields, geometry option and order. Set paged=true or supply spatial/field options for bounded numeric-handle pages. " +
            "Paged defaults are handle/type/layer and 16384 final MCP UTF-8 bytes. Content reports omissions and next_calls; echo their filters/fields with the signed cursor. " +
            "BBox tests inclusive WCS 3D axis-aligned extents, not exact geometry clipping, in selected-space drawing units without INSUNITS conversion. " +
            "Unavailable bbox-filter extents/population read failures are counted and block continuation; changed drawing/filter/fields rejects the cursor. " +
            "Requires matching plugin support; unconfirmed legacy replies are refused. Call directly, outside batches, for paged reads. " +
            "Use fields=[\"handle\"] for paged spatial handles without changing selection.")]
        public static Task<string> QueryEntities(
            [Description("Optional entity type filter such as Line, Circle, Polyline, DBText, MText, BlockReference, Hatch, Arc, or Ellipse. Case-insensitive.")] string entity_type = null,
            [Description("Optional layer name filter. Case-insensitive.")] string layer = null,
            [Description("Optional ACI color index filter. Valid range: 1-256.")] int? color_index = null,
            [Description("Optional maximum number of returned entities. The plugin clamps this to 1-5000.")] int? limit = null,
            [Description("Legacy only: include full existing geometry output. Paged reads use fields instead.")] bool include_geometry = false,
            [Description("Opt in to bounded stable pages, even without spatial or field filters.")] bool paged = false,
            [Description("Optional JSON object {min:[x,y,z],max:[x,y,z]}, finite ordered WCS drawing-unit bounds.")] string bbox = null,
            [Description("intersects (default with bbox) or contains; closed AABB boundaries. Requires bbox.")] string bbox_mode = null,
            [Description("model (default) or layout:<exact human layout name>, case-insensitive. Top-level entities only; no block/xref expansion.")] string space = null,
            [Description("JSON array from handle,type,runtime_type,layer,color_index,text,position,extents. Default handle/type/layer; handle always included. Unsupported field values are null with unavailable_fields.")] string fields = null,
            [Description("Opaque cursor from output.next_calls. Echo the same matching/field options; changed snapshots refuse continuation.")] string cursor = null,
            [Description("Paged final MCP byte cap, default 16384, range4096..65536; lower global cap still applies.")] int? max_output_bytes = null)
        {
            var request = BuildEntityQueryRequest(entity_type, layer, color_index, limit);
            request["include_geometry"] = include_geometry;
            if (paged || bbox != null || bbox_mode != null || space != null || fields != null || cursor != null || max_output_bytes.HasValue)
            {
                try
                {
                    request["paged"] = true;
                    AddSpatialOptions(request, bbox, bbox_mode, space);
                    if (fields != null) request["fields"] = JArray.Parse(fields);
                    if (cursor != null) request["cursor"] = cursor;
                    if (max_output_bytes.HasValue) request["max_output_bytes"] = max_output_bytes.Value;
                    EntityQueryOptions.Create(request);
                }
                catch (Exception ex) when (ex is ArgumentException || ex is JsonException || ex is OverflowException)
                { return ToolInputError("INVALID_ARGUMENT: " + ex.Message); }
            }
            return ToolGateway.LoggedCall("query_entities", request, request);
        }

        [McpServerTool(Name = "dwg_count_entities", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description(
            "Count AutoCAD entities matching type/layer/color; legacy calls read model space. " +
            "Optional bbox/space use the same inclusive WCS extents filter as paged query, report INSUNITS and unknown extents separately. " +
            "Incomplete spatial counts return null count/exact_match_count plus observed matched_count; no estimated count is invented. " +
            "Spatial counts require matching plugin support and direct calls outside batches.")]
        public static Task<string> CountEntities(
            [Description("Optional entity type filter such as Line, Circle, Polyline, DBText, MText, BlockReference, Hatch, Arc, or Ellipse. Case-insensitive.")] string entity_type = null,
            [Description("Optional layer name filter. Case-insensitive.")] string layer = null,
            [Description("Optional ACI color index filter. Valid range: 1-256.")] int? color_index = null,
            [Description("JSON object {min:[x,y,z],max:[x,y,z]} in WCS drawing units.")] string bbox = null,
            [Description("intersects (default with bbox) or contains; inclusive AABB test, requires bbox.")] string bbox_mode = null,
            [Description("model or layout:<exact human layout name>; no fallback to another space.")] string space = null)
        {
            var request = BuildEntityQueryRequest(entity_type, layer, color_index, limit: null);
            if (bbox != null || bbox_mode != null || space != null)
            {
                try { AddSpatialOptions(request, bbox, bbox_mode, space); EntityQueryOptions.Create(request); }
                catch (Exception ex) when (ex is ArgumentException || ex is JsonException || ex is OverflowException)
                { return ToolInputError("INVALID_ARGUMENT: " + ex.Message); }
            }
            return ToolGateway.LoggedCall("count_entities", request, request);
        }

        [McpServerTool(Name = "dwg_select_by_layer", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description(
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

        [McpServerTool(Name = "dwg_select_by_type", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description(
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

        [McpServerTool(Name = "dwg_get_selected_texts", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description(
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

        [McpServerTool(Name = "dwg_inspect_lisp", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description(
            "Statically inspect AutoLISP for known suspicious patterns without executing it — " +
            "downloaded .lsp files can embed malware (process exec via startapp/shell, " +
            "COM automation like WScript.Shell/XMLHTTP, persistence via acad.lsp/acaddoc.lsp " +
            "or registry writes, file deletion, staged (load ...) payloads, obfuscation via " +
            "eval/read). Pure local analysis: does not execute anything and needs no AutoCAD " +
            "round-trip, so it works under --read-only too. Params: 'file' (absolute path) " +
            "and/or 'code' (inline source). Returns {ok, verdict: clean|caution|dangerous, " +
            "opaque, summary, findings[]}. Compiled .fas/.vlx cannot be inspected and always " +
            "verdict 'dangerous'. This is not a sandbox or an antivirus guarantee: clean means no known " +
            "pattern matched, not safe. execution_authorized and safety_assured are always false. " +
            "dwg_run_lisp is blocked in this build; do not bypass that refusal through other execution tools.")]
        public static Task<string> InspectLisp(
            [Description("Absolute path to a .lsp/.fas/.vlx file to inspect")] string file = null,
            [Description("Inline AutoLISP source to inspect")] string code = null)
        {
            if (string.IsNullOrWhiteSpace(file) && string.IsNullOrWhiteSpace(code))
            {
                return ToolInputError("at least one of file or code is required");
            }

            var merged = new LispSecurityScanner.Report();
            var scanned = new List<string>();

            if (!string.IsNullOrWhiteSpace(file))
            {
                var rep = LispSecurityScanner.ScanFile(file, out var err);
                if (rep == null)
                {
                    return ToolInputError(err);
                }
                LispSecurityScanner.AbsorbInto(merged, rep);
                scanned.Add("file: " + file);
            }
            if (!string.IsNullOrWhiteSpace(code))
            {
                LispSecurityScanner.AbsorbInto(merged, LispSecurityScanner.ScanText(code));
                scanned.Add("code: " + code.Length + " chars");
            }

            return Task.FromResult(LispSecurityScanner.ToJson(merged, string.Join("; ", scanned)).ToString(Newtonsoft.Json.Formatting.None));
        }

        private static void AddSpatialOptions(JObject request, string bbox, string mode, string space)
        {
            if (bbox != null) request["bbox"] = JObject.Parse(bbox);
            if (mode != null) request["bbox_mode"] = mode;
            if (space != null) request["space"] = space;
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
