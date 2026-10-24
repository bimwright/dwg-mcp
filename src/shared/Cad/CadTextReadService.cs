using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Bimwright.Dwg.Plugin.Handlers;
using Bimwright.Dwg.Plugin.View;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Cad
{
    // Explicit-source reads never consume pickfirst selection or publish rewrite IDs.
    internal static class CadTextReadService
    {
        internal const int MaxTextRecords = TextReadOptions.MaxTextRecords;
        internal const int MaxTextUtf8Bytes = TextReadOptions.MaxTextUtf8Bytes;
        private const int MaxRows = 128;
        private const int MaxRawBytes = 128 * 1024;

        internal static JObject Read(Document document, TextReadOptions options)
        {
            if (document == null) throw new InvalidOperationException(DrawingSessionContract.NoActiveDocument);
            var revision = DrawingRevision.For(document);
            var snapshot = revision.Snapshot(options.Query.Space);
            var after = options.Cursor == null ? 0UL : EntityQueryCursor.Read(options.Cursor, snapshot, options.Fingerprint);
            // Opaque cluster_key values cannot collide with legacy numeric IDs, so an
            // explicit read leaves a prior manual-selection rewrite state intact.
            return ReadSource(document, options, revision, snapshot, after);
        }

        private static JObject ReadSource(Document document, TextReadOptions options, DrawingRevision revision, string snapshot, ulong after)
        {
            var records = new SortedDictionary<ulong, EntityRecord>();
            var stats = new ReadStats();
            var requested = options.Handles == null ? null : new HashSet<ulong>(options.Handles.Select(Handle));
            var found = new HashSet<ulong>();
            JObject scope;
            using (var transaction = document.Database.TransactionManager.StartTransaction())
            {
                var space = CadEntityPageService.ResolveSpace(document.Database, transaction, options.Query.Space, out scope);
                scope["bbox"] = options.Query.Bbox == null ? JValue.CreateNull() : (JToken)new JObject
                {
                    ["min"] = new JArray(options.Query.Bbox.Take(3)), ["max"] = new JArray(options.Query.Bbox.Skip(3))
                };
                scope["bbox_mode"] = options.Query.Bbox == null ? null : options.Query.BboxMode;
                if (requested == null || requested.Count > 0)
                {
                    try
                    {
                        foreach (ObjectId id in space)
                        {
                            stats.EntityIdsSeen++;
                            ulong numericHandle = 0;
                            Entity entity;
                            try
                            {
                                if (id.IsErased) { stats.ErasedExcluded++; continue; }
                                if (requested != null)
                                {
                                    try { numericHandle = Handle(id.Handle.ToString()); }
                                    catch { stats.HandleReadFailed++; continue; }
                                    if (!requested.Contains(numericHandle)) continue;
                                    found.Add(numericHandle);
                                }
                                entity = transaction.GetObject(id, OpenMode.ForRead) as Entity;
                                if (entity == null) throw new InvalidOperationException("Entity unavailable.");
                                stats.EntitiesRead++;
                            }
                            catch { stats.EntityReadFailed++; continue; }
                            try
                            {
                                if (!Supported(entity, stats)) continue;
                            }
                            catch { stats.TextReadFailed++; continue; }
                            if (requested == null)
                            {
                                try { numericHandle = Handle(entity.Handle.ToString()); }
                                catch { stats.HandleReadFailed++; continue; }
                            }
                            if (!CadEntityProjection.TryReadExtents(entity, out var extents))
                            { stats.ExtentsUnavailable++; continue; }
                            if (options.Query.Bbox != null && !CadEntityPageService.MatchesBounds(extents, options.Query)) continue;
                            EntityRecord record;
                            try
                            {
                                record = Extract(entity, extents);
                                record.Handle = numericHandle.ToString("X", CultureInfo.InvariantCulture);
                            }
                            catch { stats.TextReadFailed++; continue; }
                            if (records.ContainsKey(numericHandle)) { stats.HandleReadFailed++; continue; }
                            stats.TextUtf8Bytes += Encoding.UTF8.GetByteCount(record.Text);
                            if (records.Count >= MaxTextRecords || stats.TextUtf8Bytes > MaxTextUtf8Bytes)
                                throw new SourceTooLargeException();
                            records.Add(numericHandle, record);
                        }
                    }
                    catch (SourceTooLargeException) { throw; }
                    catch { stats.EnumerationFailed++; }
                }
                stats.MissingRequestedHandles = requested == null ? 0 : requested.Count - found.Count;
                transaction.Commit();
            }
            CheckSnapshot(revision, options, snapshot);
            var result = Header(document, options, snapshot, scope, stats, requested?.Count, records.Count);
            var rows = (JArray)result["clusters"];
            var checkpoints = (JArray)result["_checkpoints"];
            if (!stats.Complete) return result;

            // Stable handle order resolves ties in the unchanged clustering algorithm.
            var clusters = SpatialClusterer.Cluster(records.Values.ToList(), ClusterOptions.FromWire(options.GroupingStrength));
            CheckSnapshot(revision, options, snapshot);
            if (after > (ulong)clusters.Count)
                throw new InvalidOperationException("CURSOR_INVALID: cluster ordinal exceeds this complete source; restart without a cursor.");
            result["matched_count"] = clusters.Count;
            result["exact_match_count"] = clusters.Count;
            result["matches_after_cursor"] = clusters.Count - (long)after;
            result["_continuation_allowed"] = true;
            foreach (var cluster in clusters.Skip((int)after).Take(MaxRows))
            {
                var ordinal = cluster.Id;
                var key = options.ClusterKey(snapshot, ordinal);
                var row = new JObject { ["cluster_key"] = key, ["text"] = cluster.CombinedText,
                    ["entity_count"] = cluster.AllHandles.Count, ["in_block"] = false };
                if (options.IncludeEntities)
                    row["entities"] = new JArray(cluster.Entities.Select(entity => new JObject
                    {
                        ["handle"] = entity.Handle, ["kind"] = entity.Kind, ["text"] = entity.Text,
                        ["x"] = entity.X, ["y"] = entity.Y, ["height"] = entity.Height, ["layer"] = entity.Layer
                    }));
                rows.Add(row);
                checkpoints.Add(EntityQueryCursor.Issue(snapshot, options.Fingerprint, ordinal.ToString("X", CultureInfo.InvariantCulture)));
                result["_host_more"] = clusters.Count - (long)after > rows.Count;
                if (Encoding.UTF8.GetByteCount(result.ToString(Formatting.None)) > MaxRawBytes - 1024)
                {
                    rows.RemoveAt(rows.Count - 1);
                    checkpoints.RemoveAt(checkpoints.Count - 1);
                    result["_blocked"] = new JObject { ["cluster_key"] = key, ["reason"] = "row_exceeds_host_candidate_budget; narrow source or omit entities" };
                    break;
                }
            }
            result["_host_more"] = clusters.Count - (long)after > rows.Count;
            CheckSnapshot(revision, options, snapshot);
            return result;
        }

        private static bool Supported(Entity entity, ReadStats stats)
        {
            if (entity is AttributeDefinition definition && !definition.Constant)
            { stats.NonconstantAttributeDefinitions++; stats.UnsupportedEntities++; return false; }
            if (entity is DBText || entity is MText || entity is MLeader) return true;
            stats.UnsupportedEntities++;
            if (entity is BlockReference) stats.BlockReferencesExcluded++;
            return false;
        }

        private static EntityRecord Extract(Entity entity, Extents3d extents)
        {
            string text;
            Point3d position;
            double height;
            if (entity is DBText dbText)
            { text = dbText.TextString; position = dbText.Position; height = dbText.Height; }
            else if (entity is MText mText)
            { text = mText.Contents; position = mText.Location; height = mText.TextHeight; }
            else if (entity is MLeader leader)
            {
                var contents = leader.MText;
                try
                {
                    if (contents == null) throw new InvalidOperationException("MLeader text content is unavailable.");
                    text = contents.Contents;
                    position = leader.GetFirstVertex(0);
                    height = leader.TextHeight;
                }
                finally { contents?.Dispose(); }
            }
            else throw new InvalidOperationException("Text type is unsupported.");
            if (text == null || !Finite(position.X) || !Finite(position.Y) || !Finite(position.Z) || !Finite(height) || height <= 0)
                throw new InvalidOperationException("Text content, WCS anchor or positive finite height is unavailable.");
            var record = GetSelectedTextsHandler.BuildEntityRecord(entity, text, position, height, Matrix3d.Identity, null, extents);
            if (string.IsNullOrEmpty(record.Layer)) throw new InvalidOperationException("Text layer is unavailable.");
            return record;
        }

        private static JObject Header(Document document, TextReadOptions options, string snapshot, JObject scope, ReadStats stats,
            int? requestedHandles, int records)
        {
            var result = new JObject
            {
                ["text_page_version"] = 1, ["snapshot"] = snapshot, ["document_id"] = DocumentTargetGuard.Id(document),
                ["query_hash"] = options.Fingerprint, ["query_arguments"] = options.ToArguments(),
                ["read_only"] = true, ["writeback_ready"] = false, ["legacy_cluster_state_preserved"] = true,
                ["scope"] = scope,
                ["source"] = new JObject { ["kind"] = options.Handles == null ? "bbox" : "handles",
                    ["api"] = "AutoCAD managed DatabaseServices", ["extraction"] = "top_level_DBText_MText_MLeader",
                    ["clustering"] = "SpatialClusterer_WCS_XY", ["sampling"] = false, ["estimated"] = false,
                    ["requested_handle_count"] = requestedHandles.HasValue ? new JValue(requestedHandles.Value) : JValue.CreateNull(),
                    ["text_records"] = records, ["text_utf8_bytes"] = stats.TextUtf8Bytes,
                    ["max_text_records"] = MaxTextRecords, ["max_text_utf8_bytes"] = MaxTextUtf8Bytes },
                ["coverage"] = stats.Coverage(),
                ["matched_count"] = JValue.CreateNull(), ["exact_match_count"] = JValue.CreateNull(),
                ["matches_after_cursor"] = stats.Complete ? new JValue(0) : JValue.CreateNull(),
                ["clusters"] = new JArray(), ["_checkpoints"] = new JArray(),
                ["_continuation_allowed"] = false, ["_host_more"] = false,
                ["guidance"] = "This is a read-only text result, not legacy rewrite state; existing legacy rewrite groups are unchanged. For per-entity writes, read with includeEntities=true and call dwg_update_texts by handle. Cluster rewrite still requires a manual-selection get_selected_texts call."
            };
            var units = new JObject { ["coordinates"] = "drawing_units_no_conversion" };
            try { var value = document.Database.Insunits; units["insunits"] = value.ToString(); units["insunits_code"] = (int)value; }
            catch { units["insunits"] = null; units["insunits_code"] = null; units["unavailable"] = true; }
            result["units"] = units;
            if (Encoding.UTF8.GetByteCount(result.ToString(Formatting.None)) > MaxRawBytes - 2048)
                throw new InvalidOperationException("TEXT_SOURCE_TOO_LARGE: source arguments or metadata exceed the host candidate budget; narrow the source.");
            return result;
        }

        private static ulong Handle(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 16
                || !ulong.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var numeric) || numeric == 0)
                throw new InvalidOperationException("Text handle is unavailable.");
            return numeric;
        }

        private static void CheckSnapshot(DrawingRevision revision, TextReadOptions options, string snapshot)
        {
            if (!string.Equals(snapshot, revision.Snapshot(options.Query.Space), StringComparison.Ordinal))
                throw new InvalidOperationException("QUERY_CHANGED: drawing changed while reading/clustering text; restart without a cursor.");
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        private sealed class SourceTooLargeException : InvalidOperationException
        {
            internal SourceTooLargeException() : base("TEXT_SOURCE_TOO_LARGE: source exceeds 5000 text records or 8388608 aggregate UTF-8 text bytes; narrow the source before clustering.") { }
        }

        private sealed class ReadStats
        {
            internal long EntityIdsSeen, EntitiesRead, ErasedExcluded, EntityReadFailed, HandleReadFailed, ExtentsUnavailable,
                TextReadFailed, UnsupportedEntities, BlockReferencesExcluded, NonconstantAttributeDefinitions, EnumerationFailed,
                MissingRequestedHandles, TextUtf8Bytes;
            internal bool Complete => EntityReadFailed == 0 && HandleReadFailed == 0 && ExtentsUnavailable == 0
                && TextReadFailed == 0 && EnumerationFailed == 0 && MissingRequestedHandles == 0;
            internal JObject Coverage() => new JObject
            {
                ["scan_complete"] = Complete, ["entity_ids_seen"] = EntityIdsSeen, ["entities_read"] = EntitiesRead,
                ["erased_excluded"] = ErasedExcluded, ["entity_read_failed"] = EntityReadFailed,
                ["handle_read_failed"] = HandleReadFailed, ["extents_unavailable"] = ExtentsUnavailable,
                ["text_read_failed"] = TextReadFailed, ["unsupported_entity_count"] = UnsupportedEntities,
                ["block_references_excluded"] = BlockReferencesExcluded,
                ["nonconstant_attribute_definitions_excluded"] = NonconstantAttributeDefinitions,
                ["space_enumeration_failed"] = EnumerationFailed, ["missing_requested_handles"] = MissingRequestedHandles
            };
        }
    }
}
