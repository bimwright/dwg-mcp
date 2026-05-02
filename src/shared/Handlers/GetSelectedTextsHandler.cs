using System;
using System.Collections.Generic;
using System.Linq;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.Rewriting;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    /// <summary>
    /// Reads text from the pickfirst selection, clusters it spatially, stores
    /// clusters in ClusterStateStore, and returns a lightweight summary so the
    /// LLM can reason about groups without receiving a large payload.
    /// </summary>
    public class GetSelectedTextsHandler : IAcadCommand
    {
        public string Name => "get_selected_texts";

        public CommandResult Execute(Document doc, JToken parameters)
        {
            var ed = doc.Editor;
            var pickfirst = ed.SelectImplied();
            if (pickfirst.Status != PromptStatus.OK || pickfirst.Value == null || pickfirst.Value.Count == 0)
                return CommandResult.Fail("no selection");

            var records = new List<EntityRecord>();
            using (var tx = doc.Database.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject so in pickfirst.Value)
                {
                    var obj = tx.GetObject(so.ObjectId, OpenMode.ForRead);
                    CollectEntityRecords(tx, obj, Matrix3d.Identity, null, records, depth: 0);
                }
                tx.Commit();
            }

            var options = ClusterOptions.FromWire(parameters?["grouping_strength"]?.Value<string>());
            bool includeEntities = parameters?["include_entities"]?.Value<bool>() ?? false;

            var clusters = SpatialClusterer.Cluster(records, options);
            ClusterStateStore.Replace(clusters);

            var lightweight = clusters.Select(c => new
            {
                id = c.Id,
                text = c.CombinedText,
                entity_count = c.AllHandles.Count,
                in_block = c.InBlock,
                rewrite_mode = RewriteActionNames.ToWire(
                    RewriteRequestBuilder.DetermineAction(c, hasTranslation: true)),
                entities = includeEntities ? c.Entities.Select(e => new
                {
                    handle = e.Handle,
                    kind = e.Kind,
                    text = e.Text,
                    x = e.X,
                    y = e.Y,
                    layer = e.Layer
                }).ToList() : null
            }).ToList();

            return CommandResult.Success(new { clusters = lightweight });
        }

        private const int MaxBlockDepth = 1;

        private static void CollectEntityRecords(
            Transaction tx,
            DBObject obj,
            Matrix3d xform,
            string blockHandle,
            List<EntityRecord> records,
            int depth)
        {
            switch (obj)
            {
                // IMPORTANT: AttributeReference and AttributeDefinition both
                // inherit from DBText, so they MUST be matched before DBText.
                case AttributeReference attRef:
                    records.Add(BuildEntityRecord(
                        attRef, attRef.TextString,
                        attRef.Position, attRef.Height,
                        xform, blockHandle));
                    break;

                case AttributeDefinition attDef:
                    if (attDef.Constant)
                    {
                        records.Add(BuildEntityRecord(
                            attDef, attDef.TextString,
                            attDef.Position, attDef.Height,
                            xform, blockHandle));
                    }
                    break;

                case DBText dbText:
                    records.Add(BuildEntityRecord(
                        dbText, dbText.TextString,
                        dbText.Position, dbText.Height,
                        xform, blockHandle));
                    break;

                case MText mText:
                    records.Add(BuildEntityRecord(
                        mText, mText.Contents,
                        mText.Location, mText.TextHeight,
                        xform, blockHandle));
                    break;

                case MLeader mLeader:
                    var mlText = mLeader.MText != null ? mLeader.MText.Contents : string.Empty;
                    Point3d mlPos;
                    try { mlPos = mLeader.GetFirstVertex(0); }
                    catch { mlPos = Point3d.Origin; }
                    records.Add(BuildEntityRecord(
                        mLeader, mlText,
                        mlPos, mLeader.TextHeight,
                        xform, blockHandle));
                    break;

                case BlockReference br:
                    // BlockReference wrappers are NOT collected as EntityRecords (no text).
                    // Recurse into per-instance attribute references — already world-space.
                    string brHandleStr = br.Handle.ToString();
                    foreach (ObjectId aId in br.AttributeCollection)
                    {
                        var att = (AttributeReference)tx.GetObject(aId, OpenMode.ForRead);
                        CollectEntityRecords(tx, att, xform, brHandleStr, records, depth);
                    }

                    // Recurse into block definition (one level deep).
                    if (depth < MaxBlockDepth)
                    {
                        var blockDef = (BlockTableRecord)tx.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                        var childXform = br.BlockTransform.PostMultiplyBy(xform);
                        foreach (ObjectId childId in blockDef)
                        {
                            var child = tx.GetObject(childId, OpenMode.ForRead);
                            if (child is BlockReference) continue; // skip nested, MVP
                            CollectEntityRecords(tx, child, childXform, brHandleStr, records, depth + 1);
                        }
                    }
                    break;

                // Other entity types: silently skip
            }
        }

        private static EntityRecord BuildEntityRecord(
            Entity ent, string text,
            Point3d localPos, double localHeight,
            Matrix3d xform, string blockHandle)
        {
            var worldPos = localPos.TransformBy(xform);
            double worldH = localHeight * YScaleOf(xform);

            double boundsMinX = worldPos.X, boundsMaxX = worldPos.X;
            double boundsMinY = worldPos.Y, boundsMaxY = worldPos.Y;
            try
            {
                var ext = ent.GeometricExtents;
                var corners = new[]
                {
                    new Point3d(ext.MinPoint.X, ext.MinPoint.Y, ext.MinPoint.Z),
                    new Point3d(ext.MaxPoint.X, ext.MinPoint.Y, ext.MinPoint.Z),
                    new Point3d(ext.MinPoint.X, ext.MaxPoint.Y, ext.MinPoint.Z),
                    new Point3d(ext.MaxPoint.X, ext.MaxPoint.Y, ext.MinPoint.Z),
                    new Point3d(ext.MinPoint.X, ext.MinPoint.Y, ext.MaxPoint.Z),
                    new Point3d(ext.MaxPoint.X, ext.MinPoint.Y, ext.MaxPoint.Z),
                    new Point3d(ext.MinPoint.X, ext.MaxPoint.Y, ext.MaxPoint.Z),
                    new Point3d(ext.MaxPoint.X, ext.MaxPoint.Y, ext.MaxPoint.Z),
                };
                double minX = double.MaxValue, minY = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;
                foreach (var c in corners)
                {
                    var t = c.TransformBy(xform);
                    if (t.X < minX) minX = t.X;
                    if (t.Y < minY) minY = t.Y;
                    if (t.X > maxX) maxX = t.X;
                    if (t.Y > maxY) maxY = t.Y;
                }
                boundsMinX = minX; boundsMaxX = maxX;
                boundsMinY = minY; boundsMaxY = maxY;
            }
            catch { /* use position fallback */ }

            return new EntityRecord
            {
                Handle = ent.Handle.ToString(),
                Kind = GetEntityKind(ent),
                Text = text,
                X = worldPos.X,
                Y = worldPos.Y,
                Height = worldH > 0 ? worldH : localHeight,
                Layer = ent.Layer,
                BlockHandle = blockHandle,
                BoundsMinX = boundsMinX,
                BoundsMaxX = boundsMaxX,
                BoundsMinY = boundsMinY,
                BoundsMaxY = boundsMaxY,
                BlockScale = YScaleOf(xform)
            };
        }

        private static string GetEntityKind(Entity ent)
        {
            switch (ent)
            {
                case AttributeReference _:
                    return "AttributeReference";
                case AttributeDefinition _:
                    return "AttributeDefinition";
                case DBText _:
                    return "DBText";
                case MText _:
                    return "MText";
                case MLeader _:
                    return "MLeader";
                default:
                    return ent.GetType().Name;
            }
        }

        private static double YScaleOf(Matrix3d m) => m.CoordinateSystem3d.Yaxis.Length;
    }
}
