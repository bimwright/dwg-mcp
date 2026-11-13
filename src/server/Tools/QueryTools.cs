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
            "Profile top-level entities in all spaces (default), model only or one named layout. " +
            "Reports exact managed runtime type, layer and per-space counts with source/coverage; incomplete reads report observed counts and null exact totals. " +
            "Block inserts count once; nested block contents, uninserted definitions and xref contents are not expanded. " +
            "The default final MCP output cap is 16384 UTF-8 bytes, independent of the global response budget. " +
            "The all-space summary also lists unused block definitions. Follow next_calls for paged tables including unused_blocks. Each page rescans; pass expected_snapshot when offset is positive. " +
            "Opt in to text_stats for counts of top-level DBText and MText, a fixed 20-bin nominal text-height histogram in drawing units, and a bounded text preview. " +
            "A changed document/revision refuses continuation. No geometry, selection or drawing changes.")]
        public static async Task<CallToolResult> GetDrawingProfile(
            [Description("summary (default), types, layers, spaces, or unused_blocks. Summary starts all tables at offset zero. unused_blocks requires space=all and lists named non-layout definitions with no active direct or nested references.")] string section = "summary",
            [Description("Ordinal group offset for the selected table, default 0; positive offsets require expected_snapshot.")] int offset = 0,
            [Description("Maximum final MCP result bytes, default 16384; supported range 4096..65536. A smaller global budget still applies.")] int max_output_bytes = DrawingProfileOptions.DefaultMaxOutputBytes,
            [Description("Snapshot token from a previous profile result. Required for positive offsets; mismatches require restarting at offset zero.")] string expected_snapshot = null,
            [Description("all (default), model or layout:<exact layout name>. Model snapshots ignore viewport navigation; layout/all snapshots track it.")] string space = "all",
            [Description("Include text_stats for the selected space. Off by default. Preview reads at most the first 8 text entities and caps each sample at 120 UTF-16 code units.")] bool include_text_stats = false)
        {
            DrawingProfileOptions options;
            try { options = DrawingProfileOptions.Create(section, offset, max_output_bytes, expected_snapshot, space, include_text_stats); }
            catch (ArgumentException ex)
            {
                return DrawingProfileResponse.FromJson(JsonConvert.SerializeObject(new { ok = false, error = ex.Message }), DrawingProfileOptions.DefaultMaxOutputBytes);
            }
            var request = new JObject { ["section"] = section, ["offset"] = offset, ["max_output_bytes"] = max_output_bytes };
            if (expected_snapshot != null) request["expected_snapshot"] = expected_snapshot;
            if (options.Space != "all") request["space"] = options.Space;
            if (options.IncludeTextStats) request["include_text_stats"] = true;
            return DrawingProfileResponse.FromJson(await ToolGateway.LoggedCall("get_drawing_profile", request, request), max_output_bytes,
                options.Space, options.IncludeTextStats);
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
            "such as color index, locked/frozen/off state. Opt in to entity_count for top-level model and layout entities, including zero for empty layers. " +
            "If counts_complete=false, entity_count is null and observed_entity_count reports the entities that could be counted.")]
        public static async Task<string> ListLayers(
            [Description("Include entity counts for each layer across model space and paper-space layouts. Off by default. Block and xref contents are not expanded.")] bool include_counts = false)
        {
            var request = new JObject();
            if (include_counts) request["include_counts"] = true;
            var json = await ToolGateway.LoggedCall("list_layers", request, request);
            if (!include_counts) return json;

            var response = JObject.Parse(json);
            if (response.Value<bool?>("ok") != true
                || (response["result"] as JObject)?["counts_complete"]?.Type == JTokenType.Boolean)
                return json;

            response["ok"] = false;
            response["result"] = null;
            response["error"] = "LAYER_COUNTS_UNSUPPORTED: the plugin did not return the requested layer counts. Update gateway and plugin together.";
            return response.ToString(Formatting.None);
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
            [Description("Optional exact managed type name or AcDb, TEXT, INSERT, or LWPOLYLINE alias, case-insensitive. Exact uppercase POLYLINE is refused; use Polyline/LWPOLYLINE, Polyline2d or Polyline3d explicitly.")] string entity_type = null,
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
            var typeError = ValidateEntityType(entity_type);
            if (typeError != null) return ToolInputError(typeError);
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

        [McpServerTool(Name = "dwg_cluster_bands", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description(
            "Group top-level entity extents along X or Y in ascending order. Extents that overlap or are separated by at most gap belong to the same band. " +
            "Returns each band's full WCS bbox and entity count. Optional text samples include up to five DBText/MText previews per band. " +
            "Read-only. Follow output.next_calls with the same clustering options. Changes to the drawing or options invalidate the cursor. " +
            "An incomplete source scan returns coverage without bands or continuation. Call directly, outside batches.")]
        public static Task<string> ClusterBands(
            [Description("WCS axis: x or y.")] string axis,
            [Description("Maximum finite, nonnegative gap between extents in drawing units.")] double gap,
            [Description("Optional JSON object {min:[x,y,z],max:[x,y,z]} in WCS drawing units. Uses inclusive extents intersection.")] string bbox = null,
            [Description("model (default) or layout:<exact layout name>.")] string space = null,
            [Description("Optional JSON array of 1..16 exact managed entity types or query aliases, e.g. [\"Line\",\"TEXT\"].")] string types = null,
            [Description("Include up to five text samples per band, each at most 120 UTF-16 units.")] bool sample_texts = false,
            [Description("Opaque cursor from output.next_calls.")] string cursor = null,
            [Description("Maximum bands per page, clamped to 1..128.")] int limit = BandQueryOptions.MaxBandsPerPage,
            [Description("Final MCP output budget in bytes, 4096..65536, subject to the global budget.")] int? max_output_bytes = null)
        {
            var request = new JObject { ["axis"] = axis, ["gap"] = gap, ["sample_texts"] = sample_texts, ["limit"] = limit };
            try
            {
                AddSpatialOptions(request, bbox, null, space);
                if (types != null) request["types"] = JArray.Parse(types);
                if (cursor != null) request["cursor"] = cursor;
                if (max_output_bytes.HasValue) request["max_output_bytes"] = max_output_bytes.Value;
                BandQueryOptions.Create(request);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is JsonException || ex is OverflowException)
            { return ToolInputError("INVALID_ARGUMENT: " + ex.Message); }
            return ToolGateway.LoggedCall("cluster_bands", request, request);
        }

        [McpServerTool(Name = "dwg_count_entities", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description(
            "Count AutoCAD entities matching type/layer/color; legacy calls read model space. " +
            "Optional bbox/space use the same inclusive WCS extents filter as paged query, report INSUNITS and unknown extents separately. " +
            "Incomplete spatial counts return null count/exact_match_count plus observed matched_count; no estimated count is invented. " +
            "An exact zero returns hint.available_types with up to 20 managed types observed in the whole queried space before applying filters. " +
            "Spatial counts require matching plugin support and direct calls outside batches.")]
        public static Task<string> CountEntities(
            [Description("Optional exact managed type name or AcDb, TEXT, INSERT, or LWPOLYLINE alias, case-insensitive. Exact uppercase POLYLINE is refused; use Polyline/LWPOLYLINE, Polyline2d or Polyline3d explicitly.")] string entity_type = null,
            [Description("Optional layer name filter. Case-insensitive.")] string layer = null,
            [Description("Optional ACI color index filter. Valid range: 1-256.")] int? color_index = null,
            [Description("JSON object {min:[x,y,z],max:[x,y,z]} in WCS drawing units.")] string bbox = null,
            [Description("intersects (default with bbox) or contains; inclusive AABB test, requires bbox.")] string bbox_mode = null,
            [Description("model or layout:<exact human layout name>; no fallback to another space.")] string space = null)
        {
            var typeError = ValidateEntityType(entity_type);
            if (typeError != null) return ToolInputError(typeError);
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
            "For bbox filtering, use dwg_query_entities with bbox, the same filters, and fields=[\"handle\"] for handles only. " +
            "This does not change AutoCAD pickfirst selection.")]
        public static Task<string> SelectByLayer(
            [Description("Required layer name filter. Case-insensitive.")] string layer,
            [Description("Optional exact managed type name or AcDb, TEXT, INSERT, or LWPOLYLINE alias, case-insensitive. Exact uppercase POLYLINE is refused; use Polyline/LWPOLYLINE, Polyline2d or Polyline3d explicitly.")] string entity_type = null,
            [Description("Optional ACI color index filter. Valid range: 1-256.")] int? color_index = null,
            [Description("Optional maximum number of returned handles. The plugin clamps this to 1-5000.")] int? limit = null)
        {
            var typeError = ValidateEntityType(entity_type);
            if (typeError != null) return ToolInputError(typeError);
            var request = BuildEntityQueryRequest(entity_type, layer, color_index, limit);
            return ToolGateway.LoggedCall("select_by_layer", request, request);
        }

        [McpServerTool(Name = "dwg_select_by_type", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description(
            "Return handles for model-space entities of a required type, with optional layer/color filters. " +
            "For bbox filtering, use dwg_query_entities with bbox, the same filters, and fields=[\"handle\"] for handles only. " +
            "An empty result returns hint.available_types with up to 20 managed types observed in model space before applying filters. " +
            "This does not change AutoCAD pickfirst selection.")]
        public static Task<string> SelectByType(
            [Description("Required exact managed type name or AcDb, TEXT, INSERT, or LWPOLYLINE alias, case-insensitive. Exact uppercase POLYLINE is refused; use Polyline/LWPOLYLINE, Polyline2d or Polyline3d explicitly.")] string entity_type,
            [Description("Optional layer name filter. Case-insensitive.")] string layer = null,
            [Description("Optional ACI color index filter. Valid range: 1-256.")] int? color_index = null,
            [Description("Optional maximum number of returned handles. The plugin clamps this to 1-5000.")] int? limit = null)
        {
            var typeError = ValidateEntityType(entity_type);
            if (typeError != null) return ToolInputError(typeError);
            var request = BuildEntityQueryRequest(entity_type, layer, color_index, limit);
            return ToolGateway.LoggedCall("select_by_type", request, request);
        }

        [McpServerTool(Name = "dwg_get_selected_texts", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description(
            "Read and cluster text. Existing two-argument calls use AutoCAD pickfirst selection and legacy rewrite IDs. " +
            "Supply handles or bbox to read without selecting anything; choose exactly one explicit source. " +
            "Explicit-source mode reads top-level DBText/MText/MLeader, does not expand blocks/xrefs, clusters the full declared source before paging whole clusters, and returns read-only cluster_key values. " +
            "It leaves legacy rewrite state unchanged. For per-entity writes use includeEntities=true then dwg_update_texts by handle; cluster rewrite still requires manual selection. " +
            "Cursor, content omissions and next_calls follow the bounded query pattern (default16384 final MCP UTF-8 bytes). " +
            "Source limits5000 text records/8MiB UTF-8; incomplete sources produce no groups or continuation. " +
            "Model snapshots ignore viewport navigation; entity changes refuse continuation. Explicit-source calls must be direct outside batches/baked tools. " +
            "Each cluster has id, text, entity_count, in_block (bool) and " +
            "rewrite_mode only on the legacy selection path. Explicit-source groups never supply writable numeric IDs.")]
        public static Task<string> GetSelectedTexts(
            [Description("Optional grouping strength: weak, normal, or strong. Use weak when an image/layout reference indicates nearby labels should stay separate.")] string groupingStrength = "normal",
            [Description("When true, include each child text entity's handle/text/position so callers can recover from over-grouped clusters.")] bool includeEntities = false,
            [Description("Explicit source JSON array of0..5000 hexadecimal handle values; deduplicated. Mutually exclusive with bbox.")] string handles = null,
            [Description("Explicit source JSON object {min:[x,y,z],max:[x,y,z]} in WCS drawing units; mutually exclusive with handles.")] string bbox = null,
            [Description("intersects or contains inclusive3D extents test; requires bbox.")] string bbox_mode = null,
            [Description("model(default for explicit source) or layout:<exact name>. Top-level text only.")] string space = null,
            [Description("Opaque whole-cluster continuation from output.next_calls. Echo same source/grouping/includeEntities.")] string cursor = null,
            [Description("Explicit-source final MCP UTF-8 cap4096..65536(default16384), subject to lower global cap.")] int? max_output_bytes = null)
        {
            if (handles != null || bbox != null || bbox_mode != null || space != null || cursor != null || max_output_bytes.HasValue)
            {
                var explicitRequest = new JObject { ["grouping_strength"] = groupingStrength, ["include_entities"] = includeEntities };
                try
                {
                    if (handles != null) explicitRequest["handles"] = JArray.Parse(handles);
                    AddSpatialOptions(explicitRequest, bbox, bbox_mode, space);
                    if (cursor != null) explicitRequest["cursor"] = cursor;
                    if (max_output_bytes.HasValue) explicitRequest["max_output_bytes"] = max_output_bytes.Value;
                    TextReadOptions.Create(explicitRequest);
                }
                catch (Exception ex) when (ex is ArgumentException || ex is JsonException || ex is OverflowException)
                { return ToolInputError("INVALID_ARGUMENT: " + ex.Message); }
                return ToolGateway.LoggedCall("get_selected_texts", explicitRequest, explicitRequest);
            }
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

        private static string ValidateEntityType(string entityType)
        {
            try { EntityQueryOptions.NormalizeType(entityType); return null; }
            catch (ArgumentException ex) { return ex.Message; }
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
                request["entity_type"] = entityType.TrimStart().StartsWith("AcDb", StringComparison.OrdinalIgnoreCase)
                    ? EntityQueryOptions.NormalizeType(entityType) : entityType;
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
