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
    internal static class CadBandQuery
    {
        private const int HostPageBytes = 128 * 1024;

        internal static JObject Read(Document document, BandQueryOptions options)
        {
            var revision = DrawingRevision.For(document);
            var snapshot = revision.Snapshot(options.Query.Space);
            var after = options.Query.Cursor == null ? 0UL
                : EntityQueryCursor.Read(options.Query.Cursor, snapshot, options.Fingerprint);
            var database = document.Database;
            var rows = new JArray();
            var checkpoints = new JArray();
            var coverage = new Coverage();
            int bandCount = 0;
            JObject result;
            using (var transaction = database.TransactionManager.StartTransaction())
            {
                var space = CadEntityPageService.ResolveSpace(database, transaction, options.Query.Space, out var scope);
                var members = Scan(space, transaction, options, coverage);
                if (options.Query.Bbox != null)
                    scope["bbox"] = Box(options.Query.Bbox);
                result = new JObject
                {
                    ["band_page_version"] = 1, ["snapshot"] = snapshot, ["document_id"] = DocumentTargetGuard.Id(document),
                    ["axis"] = options.Axis, ["gap"] = options.Gap, ["scope"] = scope,
                    ["units"] = "WCS_drawing_units_no_conversion", ["query_arguments"] = options.ToArguments(),
                    ["observed_entity_count"] = members.Count, ["bands"] = rows, ["_checkpoints"] = checkpoints
                };
                if (coverage.Complete)
                {
                    var sorted = members.OrderBy(member => Axis(member.Bounds.MinPoint, options.Axis))
                        .ThenBy(member => Axis(member.Bounds.MaxPoint, options.Axis));
                    var bytes = Encoding.UTF8.GetByteCount(result.ToString(Formatting.None));
                    foreach (var band in Cluster(sorted, options))
                    {
                        bandCount++;
                        if ((ulong)bandCount <= after || rows.Count >= options.Query.Limit
                            || rows.Count >= BandQueryOptions.MaxBandsPerPage || result["_blocked"] != null) continue;
                        var id = bandCount.ToString(CultureInfo.InvariantCulture);
                        var row = new JObject { ["band_id"] = id, ["bbox"] = Box(band.Bounds), ["entity_count"] = band.Count };
                        if (options.SampleTexts)
                            row["text_samples"] = ReadSamples(band.TextIds, transaction, coverage);
                        var cursor = EntityQueryCursor.Issue(snapshot, options.Fingerprint, bandCount.ToString("X", CultureInfo.InvariantCulture));
                        bytes += Encoding.UTF8.GetByteCount(row.ToString(Formatting.None)) + cursor.Length + 4;
                        if (bytes > HostPageBytes - 2048)
                        {
                            result["_blocked"] = new JObject { ["band_id"] = id, ["reason"] = "row_exceeds_host_candidate_budget" };
                            continue;
                        }
                        rows.Add(row);
                        checkpoints.Add(cursor);
                    }
                }
                transaction.Commit();
            }

            if (!string.Equals(snapshot, revision.Snapshot(options.Query.Space), StringComparison.Ordinal))
                throw new InvalidOperationException("QUERY_CHANGED: the drawing lifetime or revision changed while clustering; restart without a cursor.");
            if (coverage.Complete && after > (ulong)bandCount)
                throw new InvalidOperationException("CURSOR_INVALID: band checkpoint is outside the current result; restart without a cursor.");

            var remaining = coverage.Complete ? bandCount - (long)after : (long?)null;
            result["band_count"] = coverage.Complete ? new JValue(bandCount) : JValue.CreateNull();
            result["entity_count"] = coverage.Complete ? result["observed_entity_count"].DeepClone() : JValue.CreateNull();
            result["matches_after_cursor"] = remaining;
            result["coverage"] = coverage.ToJson();
            result["_continuation_allowed"] = coverage.Complete;
            result["_host_more"] = remaining > rows.Count;
            return result;
        }

        private static List<Member> Scan(BlockTableRecord space, Transaction transaction, BandQueryOptions options, Coverage coverage)
        {
            var members = new List<Member>();
            try
            {
                foreach (ObjectId id in space)
                {
                    coverage.Seen++;
                    try
                    {
                        if (id.IsErased) { coverage.Erased++; continue; }
                        if (transaction.GetObject(id, OpenMode.ForRead) is not Entity entity)
                        { coverage.ReadFailed++; continue; }
                        if (options.Types.Length != 0
                            && !options.Types.Any(type => EntityQueryOptions.TypeMatches(entity.GetType(), type))) continue;
                        if (!CadEntityProjection.TryReadExtents(entity, out var bounds))
                        { coverage.ExtentsUnavailable++; continue; }
                        if (options.Query.Bbox != null && !CadEntityPageService.MatchesBounds(bounds, options.Query)) continue;
                        var textId = options.SampleTexts && (entity is DBText || entity is MText) ? id : ObjectId.Null;
                        members.Add(new Member(bounds, textId));
                    }
                    catch { coverage.ReadFailed++; }
                }
            }
            catch { coverage.EnumerationFailed = true; }
            return members;
        }

        private static IEnumerable<Band> Cluster(IEnumerable<Member> sorted, BandQueryOptions options)
        {
            Band band = null;
            foreach (var member in sorted)
            {
                if (band == null || Axis(member.Bounds.MinPoint, options.Axis) - Axis(band.Bounds.MaxPoint, options.Axis) > options.Gap)
                {
                    if (band != null) yield return band;
                    band = new Band(member.Bounds);
                }
                band.Add(member);
            }
            if (band != null) yield return band;
        }

        private static JArray ReadSamples(List<ObjectId> ids, Transaction transaction, Coverage coverage)
        {
            var samples = new JArray();
            if (ids == null) return samples;
            foreach (var id in ids)
            {
                try
                {
                    var entity = transaction.GetObject(id, OpenMode.ForRead);
                    var text = entity is DBText single ? single.TextString : ((MText)entity).Contents;
                    if (text == null) { coverage.SampleReadFailed++; continue; }
                    var length = Math.Min(text.Length, BandQueryOptions.SampleMaxLength);
                    if (length < text.Length && char.IsSurrogatePair(text[length - 1], text[length])) length--;
                    samples.Add(new JObject
                    {
                        ["handle"] = entity.Handle.ToString(), ["text"] = text.Substring(0, length), ["truncated"] = length < text.Length
                    });
                }
                catch { coverage.SampleReadFailed++; }
            }
            return samples;
        }

        private static double Axis(Point3d point, string axis) => axis == "x" ? point.X : point.Y;
        private static JObject Box(Extents3d bounds)
            => new JObject { ["min"] = Point(bounds.MinPoint), ["max"] = Point(bounds.MaxPoint) };
        private static JObject Box(double[] bounds)
            => new JObject { ["min"] = new JArray(bounds.Take(3)), ["max"] = new JArray(bounds.Skip(3)) };
        private static JArray Point(Point3d point) => new JArray(point.X, point.Y, point.Z);

        private readonly struct Member
        {
            internal Member(Extents3d bounds, ObjectId textId) { Bounds = bounds; TextId = textId; }
            internal Extents3d Bounds { get; }
            internal ObjectId TextId { get; }
        }

        private sealed class Band
        {
            internal Band(Extents3d bounds) { Bounds = bounds; }
            internal Extents3d Bounds;
            internal int Count;
            internal List<ObjectId> TextIds;

            internal void Add(Member member)
            {
                Bounds.AddExtents(member.Bounds);
                Count++;
                if (member.TextId.IsNull) return;
                if (TextIds == null) TextIds = new List<ObjectId>();
                if (TextIds.Count < BandQueryOptions.MaxSamples) TextIds.Add(member.TextId);
            }
        }

        private sealed class Coverage
        {
            internal int Seen, Erased, ReadFailed, ExtentsUnavailable, SampleReadFailed;
            internal bool EnumerationFailed;
            internal bool Complete => ReadFailed == 0 && ExtentsUnavailable == 0 && !EnumerationFailed;
            internal JObject ToJson() => new JObject
            {
                ["scan_complete"] = Complete, ["entity_ids_seen"] = Seen, ["erased_excluded"] = Erased,
                ["entity_read_failed"] = ReadFailed, ["extents_unavailable"] = ExtentsUnavailable,
                ["enumeration_failed"] = EnumerationFailed, ["text_sample_read_failed"] = SampleReadFailed
            };
        }
    }
}
