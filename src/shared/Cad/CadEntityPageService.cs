using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Bimwright.Dwg.Plugin.View;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Cad
{
    // Rescan one top-level space; only the smallest page of handles is retained.
    // The document lock is supplied by DocumentInvoker, as for the legacy query.
    internal static class CadEntityPageService
    {
        private const int MaxRows = 128;
        private const int MaxRawBytes = 128 * 1024;

        internal static JObject Query(Document document, EntityQueryOptions options, bool countOnly = false)
        {
            if (document == null) throw new InvalidOperationException(DrawingSessionContract.NoActiveDocument);
            var revision = DrawingRevision.For(document);
            var snapshot = Snapshot(revision);
            var after = options.Cursor == null ? 0UL : EntityQueryCursor.Read(options.Cursor, snapshot, options.Fingerprint);
            var database = document.Database;
            var stats = new ScanStats();
            var candidates = new SortedList<ulong, Candidate>();
            var rows = new JArray();
            var checkpoints = new JArray();
            var result = new JObject();
            using (var transaction = database.TransactionManager.StartTransaction())
            {
                var record = ResolveSpace(database, transaction, options.Space, out var scope);
                scope["bbox_mode"] = options.Bbox == null ? null : options.BboxMode;
                scope["bbox"] = options.Bbox == null ? JValue.CreateNull() : (JToken)new JObject
                {
                    ["min"] = new JArray(options.Bbox.Take(3)), ["max"] = new JArray(options.Bbox.Skip(3))
                };
                try
                {
                    foreach (ObjectId id in record)
                    {
                        stats.EntityIdsSeen++;
                        Entity entity;
                        try
                        {
                            if (id.IsErased) { stats.ErasedExcluded++; continue; }
                            entity = transaction.GetObject(id, OpenMode.ForRead) as Entity;
                            if (entity == null) throw new InvalidOperationException("Entity unavailable.");
                            stats.EntitiesRead++;
                        }
                        catch { stats.EntityReadFailed++; continue; }
                        try
                        {
                            if (!Matches(entity, options)) continue;
                        }
                        catch { stats.FilterReadFailed++; continue; }
                        Extents3d? bounds = null;
                        if (options.Bbox != null)
                        {
                            if (!CadEntityProjection.TryReadExtents(entity, out var readBounds))
                            { stats.ExtentsUnavailable++; continue; }
                            if (!MatchesBounds(readBounds, options)) continue;
                            bounds = readBounds;
                        }
                        stats.Matched++;
                        if (countOnly) continue;
                        string handle;
                        ulong numericHandle;
                        try
                        {
                            handle = entity.Handle.ToString();
                            if (!ulong.TryParse(handle, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out numericHandle)
                                || numericHandle == 0) throw new InvalidOperationException("Handle unavailable.");
                        }
                        catch { stats.HandleReadFailed++; continue; }
                        if (numericHandle <= after) continue;
                        stats.MatchesAfterCursor++;
                        var capacity = Math.Min(options.Limit, MaxRows);
                        if (candidates.Count >= capacity && numericHandle > candidates.Keys[candidates.Count - 1]) continue;
                        if (candidates.ContainsKey(numericHandle)) { stats.HandleReadFailed++; continue; }
                        candidates.Add(numericHandle, new Candidate(id, handle, bounds));
                        if (candidates.Count > capacity) candidates.RemoveAt(candidates.Count - 1);
                    }
                }
                catch { stats.EnumerationFailed++; }

                result = Header(database, options, snapshot, DocumentTargetGuard.Id(document), scope, stats, countOnly);
                if (Encoding.UTF8.GetByteCount(result.ToString(Formatting.None)) > MaxRawBytes - 2048)
                    throw new InvalidOperationException("QUERY_OUTPUT_TOO_LARGE: query metadata exceeds the host candidate budget; shorten filter or layout names.");
                if (!countOnly)
                {
                    result["entities"] = rows;
                    result["_checkpoints"] = checkpoints;
                    foreach (var candidate in candidates.Values)
                    {
                        JObject row;
                        try
                        {
                            var entity = transaction.GetObject(candidate.Id, OpenMode.ForRead) as Entity;
                            if (entity == null) throw new InvalidOperationException("Entity unavailable.");
                            row = CadEntityProjection.Project(entity, options.Fields, candidate.Bounds, options.Bbox != null,
                                out var extentsUnavailable, out var fieldReadFailed, candidate.Handle);
                            stats.ProjectionExtentsUnavailable += extentsUnavailable;
                            stats.ProjectionFieldReadFailed += fieldReadFailed;
                        }
                        catch { stats.EntityReadFailed++; break; }
                        rows.Add(row);
                        checkpoints.Add(EntityQueryCursor.Issue(snapshot, options.Fingerprint, candidate.Handle));
                        Update(result, stats, rows.Count);
                        if (Encoding.UTF8.GetByteCount(result.ToString(Formatting.None)) > MaxRawBytes - 1024)
                        {
                            rows.RemoveAt(rows.Count - 1);
                            checkpoints.RemoveAt(checkpoints.Count - 1);
                            result["_blocked"] = new JObject { ["handle"] = candidate.Handle, ["reason"] = "row_exceeds_host_candidate_budget; narrow fields" };
                            break;
                        }
                    }
                    Update(result, stats, rows.Count);
                }
                transaction.Commit();
            }
            if (!string.Equals(snapshot, Snapshot(revision), StringComparison.Ordinal))
                throw new InvalidOperationException("QUERY_CHANGED: the drawing lifetime or revision changed during enumeration; restart without a cursor.");
            return result;
        }

        private static JObject Header(Database database, EntityQueryOptions options, string snapshot, string documentId,
            JObject scope, ScanStats stats, bool countOnly)
        {
            var result = new JObject
            {
                ["scope"] = scope,
                ["units"] = Units(database),
                ["source"] = new JObject { ["api"] = "AutoCAD managed DatabaseServices", ["method"] = "ForRead top-level BlockTableRecord enumeration",
                    ["counting"] = "full_enumeration", ["sampling"] = false, ["estimated"] = false,
                    ["bbox_basis"] = "WCS_3D_axis_aligned_GeometricExtents_inclusive",
                    ["type_filter_basis"] = "managed_Name_or_FullName_with_TEXT_INSERT_LWPOLYLINE_aliases" }
            };
            if (options.EntityType != null && stats.Matched == 0)
                result["filter_hint"] = "Use an exact managed Name/FullName or TEXT, INSERT, LWPOLYLINE; inspect dwg_get_drawing_profile for observed types. No type family is inferred.";
            if (countOnly)
            {
                result["query_count_version"] = 1;
                result["count"] = stats.MembershipComplete ? new JValue(stats.Matched) : JValue.CreateNull();
                result["matched_count"] = stats.Matched;
                result["exact_match_count"] = stats.MembershipComplete ? new JValue(stats.Matched) : JValue.CreateNull();
                result["coverage"] = stats.Coverage();
                return result;
            }
            result["query_page_version"] = 1;
            result["snapshot"] = snapshot;
            result["document_id"] = documentId;
            result["query_hash"] = options.Fingerprint;
            result["query_arguments"] = Arguments(options);
            Update(result, stats, 0);
            return result;
        }

        private static void Update(JObject result, ScanStats stats, int delivered)
        {
            result["matched_count"] = stats.Matched;
            result["matches_after_cursor"] = stats.MatchesAfterCursor;
            result["exact_match_count"] = stats.MembershipComplete ? new JValue(stats.Matched) : JValue.CreateNull();
            result["coverage"] = stats.Coverage();
            result["_continuation_allowed"] = stats.ContinuationAllowed;
            result["_host_more"] = stats.MatchesAfterCursor > delivered;
        }

        private static JObject Arguments(EntityQueryOptions options)
        {
            var result = new JObject { ["paged"] = true, ["limit"] = options.Limit, ["space"] = options.Space,
                ["fields"] = new JArray(options.Fields).ToString(Formatting.None) };
            if (options.EntityType != null) result["entity_type"] = options.EntityType;
            if (options.Layer != null) result["layer"] = options.Layer;
            if (options.ColorIndex.HasValue) result["color_index"] = options.ColorIndex.Value;
            if (options.Bbox != null)
            {
                result["bbox"] = new JObject { ["min"] = new JArray(options.Bbox.Take(3)), ["max"] = new JArray(options.Bbox.Skip(3)) }.ToString(Formatting.None);
                result["bbox_mode"] = options.BboxMode;
            }
            return result;
        }

        private static BlockTableRecord ResolveSpace(Database database, Transaction transaction, string space, out JObject scope)
        {
            var blocks = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
            var modelId = blocks[BlockTableRecord.ModelSpace];
            BlockTableRecord selected = null;
            string name = null;
            if (space == "model") { selected = (BlockTableRecord)transaction.GetObject(modelId, OpenMode.ForRead); name = "Model"; }
            else
            {
                var requested = space.Substring("layout:".Length);
                try
                {
                    foreach (ObjectId id in blocks)
                    {
                        if (id.IsErased || id == modelId) continue;
                        var record = (BlockTableRecord)transaction.GetObject(id, OpenMode.ForRead);
                        if (!record.IsLayout) continue;
                        var layoutName = ((Layout)transaction.GetObject(record.LayoutId, OpenMode.ForRead)).LayoutName;
                        if (string.IsNullOrEmpty(layoutName)) throw new InvalidOperationException("Layout name unavailable.");
                        if (!string.Equals(layoutName, requested, StringComparison.OrdinalIgnoreCase)) continue;
                        if (selected != null) throw new InvalidOperationException("Layout name is ambiguous.");
                        selected = record; name = layoutName;
                    }
                }
                catch { throw new InvalidOperationException("QUERY_SPACE_UNAVAILABLE: named layout could not be resolved completely; no fallback space was queried."); }
                if (selected == null) throw new InvalidOperationException("QUERY_SPACE_NOT_FOUND: the exact named layout does not exist; no fallback space was queried.");
            }
            scope = new JObject { ["space"] = space, ["name"] = name, ["id"] = selected.Handle.ToString(),
                ["entities"] = "top_level", ["block_expansion"] = false, ["xref_traversal"] = false, ["erased"] = "excluded" };
            return selected;
        }

        private static bool Matches(Entity entity, EntityQueryOptions options)
        {
            if (options.EntityType != null && !EntityQueryOptions.TypeMatches(entity.GetType(), options.EntityType)) return false;
            if (options.Layer != null && !string.Equals(entity.Layer, options.Layer, StringComparison.OrdinalIgnoreCase)) return false;
            return !options.ColorIndex.HasValue || (int)entity.ColorIndex == options.ColorIndex.Value;
        }

        private static bool MatchesBounds(Extents3d extents, EntityQueryOptions options)
        {
            var min = extents.MinPoint; var max = extents.MaxPoint; var bbox = options.Bbox;
            if (options.BboxMode == "contains")
                return min.X >= bbox[0] && min.Y >= bbox[1] && min.Z >= bbox[2]
                    && max.X <= bbox[3] && max.Y <= bbox[4] && max.Z <= bbox[5];
            return max.X >= bbox[0] && max.Y >= bbox[1] && max.Z >= bbox[2]
                && min.X <= bbox[3] && min.Y <= bbox[4] && min.Z <= bbox[5];
        }

        private static JObject Units(Database database)
        {
            var result = new JObject { ["coordinates"] = "drawing_units_no_conversion" };
            try { var units = database.Insunits; result["insunits"] = units.ToString(); result["insunits_code"] = (int)units; }
            catch { result["insunits"] = null; result["insunits_code"] = null; result["unavailable"] = true; }
            return result;
        }

        private static string Snapshot(DrawingRevision revision)
            => revision.SessionId + ":" + revision.ProfileRevision.ToString(CultureInfo.InvariantCulture);

        private sealed class Candidate
        {
            internal Candidate(ObjectId id, string handle, Extents3d? bounds) { Id = id; Handle = handle; Bounds = bounds; }
            internal ObjectId Id { get; }
            internal string Handle { get; }
            internal Extents3d? Bounds { get; }
        }

        private sealed class ScanStats
        {
            internal long EntityIdsSeen, EntitiesRead, ErasedExcluded, EntityReadFailed, FilterReadFailed,
                ExtentsUnavailable, HandleReadFailed, EnumerationFailed, ProjectionExtentsUnavailable, ProjectionFieldReadFailed,
                Matched, MatchesAfterCursor;
            internal bool MembershipComplete => EntityReadFailed == 0 && FilterReadFailed == 0 && ExtentsUnavailable == 0 && EnumerationFailed == 0;
            internal bool ContinuationAllowed => MembershipComplete && HandleReadFailed == 0;
            internal JObject Coverage() => new JObject
            {
                ["scan_complete"] = ContinuationAllowed,
                ["entity_ids_seen"] = EntityIdsSeen, ["entities_read"] = EntitiesRead, ["erased_excluded"] = ErasedExcluded,
                ["entity_read_failed"] = EntityReadFailed, ["filter_read_failed"] = FilterReadFailed,
                ["extents_unavailable"] = ExtentsUnavailable,
                ["projection_extents_unavailable"] = ProjectionExtentsUnavailable,
                ["handle_read_failed"] = HandleReadFailed, ["enumeration_failed"] = EnumerationFailed,
                ["projection_field_read_failed"] = ProjectionFieldReadFailed
            };
        }
    }
}
