using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace Bimwright.Dwg.Plugin.Rewriting
{
    /// <summary>
    /// AutoCAD-side executor for <see cref="RewriteRequest"/>. Each call opens
    /// its own transaction so callers processing multiple requests see
    /// per-request isolation.
    /// </summary>
    internal static class RewriteExecutor
    {
        public static RewriteResult Execute(Database db, RewriteRequest req, ObjectId unicodeStyleId)
        {
            try
            {
                string targetLayer = req.ApplyUnicodeStyle
                    ? McpLayerService.EnsureWhiteLayer(db)
                    : null;

                using (var tx = db.TransactionManager.StartTransaction())
                {
                    if (!TryGetObjectId(db, req.AnchorHandle, out var anchorId))
                    {
                        tx.Abort();
                        return Fail(req, "anchor handle not found");
                    }

                    var deleteIds = new List<ObjectId>();
                    foreach (var h in req.DeleteHandles)
                    {
                        if (!TryGetObjectId(db, h, out var did))
                        {
                            tx.Abort();
                            return Fail(req, $"delete handle not found: {h}");
                        }

                        deleteIds.Add(did);
                    }

                    double effectiveHeight = req.MedianHeight
                        ?? ComputeMedianHeight(tx, anchorId, deleteIds);
                    if (effectiveHeight <= 0)
                    {
                        effectiveHeight = 2.5;
                    }

                    string newHandle;
                    int deletedCount;

                    switch (req.Action)
                    {
                        case RewriteAction.Collapse:
                            if (!DoCollapse(tx, db, anchorId, deleteIds, req, effectiveHeight, unicodeStyleId, targetLayer,
                                out newHandle, out string collapseError))
                            {
                                tx.Abort();
                                return Fail(req, collapseError);
                            }
                            deletedCount = deleteIds.Count;
                            break;

                        case RewriteAction.RewriteInBlock:
                            if (!DoRewriteInBlock(tx, anchorId, deleteIds, req, unicodeStyleId, targetLayer,
                                out newHandle, out string rewriteError))
                            {
                                tx.Abort();
                                return Fail(req, rewriteError);
                            }
                            deletedCount = deleteIds.Count;
                            break;

                        case RewriteAction.Update:
                            if (!DoUpdate(tx, anchorId, req, unicodeStyleId, targetLayer,
                                out newHandle, out string updateError))
                            {
                                tx.Abort();
                                return Fail(req, updateError);
                            }
                            deletedCount = 0;
                            break;

                        case RewriteAction.StyleOnly:
                            DoStyleOnly(tx, anchorId, deleteIds, unicodeStyleId, targetLayer, req.FinalScale);
                            newHandle = req.AnchorHandle;
                            deletedCount = 0;
                            break;

                        default:
                            tx.Abort();
                            return Fail(req, $"unknown action: {req.Action}");
                    }

                    tx.Commit();
                    return new RewriteResult
                    {
                        Ok = true,
                        Action = req.Action,
                        AnchorHandle = req.AnchorHandle,
                        NewHandle = newHandle,
                        DeletedCount = deletedCount,
                        Error = null,
                        LayoutHint = req.LayoutHint,
                        FinalWidth = req.MtextWidth,
                        FinalTextHeight = DetermineReportedFinalHeight(req, effectiveHeight)
                    };
                }
            }
            catch (Exception ex)
            {
                return Fail(req, ex.Message);
            }
        }

        private static bool DoCollapse(
            Transaction tx,
            Database db,
            ObjectId anchorId,
            List<ObjectId> deleteIds,
            RewriteRequest req,
            double effectiveHeight,
            ObjectId unicodeStyleId,
            string targetLayer,
            out string newHandle,
            out string error)
        {
            newHandle = null;
            error = null;

            var anchorEnt = (Entity)tx.GetObject(anchorId, OpenMode.ForWrite);
            if (!TryExtractLocation(anchorEnt, out var pos, out var rotation, out var anchorStyleId, out var layer, out var needsBaselineToTopOffset))
            {
                error = "anchor is not a text entity";
                return false;
            }

            double textHeight = req.ExplicitTextHeight
                ?? (req.ApplyUnicodeStyle
                    ? UnicodeStyleService.ComputeTargetHeight(tx, anchorStyleId, unicodeStyleId, effectiveHeight)
                    : effectiveHeight);
            textHeight = FinalTextScalePolicy.Apply(textHeight, req.FinalScale);

            if (needsBaselineToTopOffset)
            {
                pos = new Point3d(pos.X, pos.Y + textHeight, pos.Z);
            }

            var newMText = new MText
            {
                Location = pos,
                Rotation = rotation,
                TextHeight = textHeight,
                Contents = req.NewText ?? string.Empty,
                TextStyleId = req.ApplyUnicodeStyle ? unicodeStyleId : anchorStyleId,
                Layer = targetLayer ?? layer,
                Width = req.MtextWidth > 0 ? req.MtextWidth : 0,
                Attachment = AttachmentPoint.TopLeft
            };

            var btr = (BlockTableRecord)tx.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            btr.AppendEntity(newMText);
            tx.AddNewlyCreatedDBObject(newMText, true);
            newHandle = newMText.Handle.ToString();

            anchorEnt.Erase();
            foreach (var did in deleteIds)
            {
                tx.GetObject(did, OpenMode.ForWrite).Erase();
            }

            return true;
        }

        private static bool DoRewriteInBlock(
            Transaction tx,
            ObjectId anchorId,
            List<ObjectId> deleteIds,
            RewriteRequest req,
            ObjectId unicodeStyleId,
            string targetLayer,
            out string newHandle,
            out string error)
        {
            newHandle = null;
            error = null;

            var anchorEnt = (Entity)tx.GetObject(anchorId, OpenMode.ForWrite);

            // Single entity: keep existing behavior (just update text + style)
            if (deleteIds.Count == 0)
            {
                if (!SetText(anchorEnt, req.NewText ?? string.Empty))
                {
                    error = "anchor is not a text entity";
                    return false;
                }

                if (req.ApplyUnicodeStyle)
                {
                    UnicodeStyleService.ApplyToObject(tx, anchorEnt, unicodeStyleId);
                    McpLayerService.ApplyToObject(anchorEnt, targetLayer);
                }

                ApplyFinalScale(anchorEnt, req.FinalScale);

                newHandle = req.AnchorHandle;
                return true;
            }

            // Multi-fragment: create MText in block definition, erase old DBTexts
            if (!TryExtractLocation(anchorEnt, out var anchorPos, out var rotation,
                out var anchorStyleId, out var layer, out _))
            {
                error = "anchor is not a text entity";
                return false;
            }

            // Compute bounding box from all entities
            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;
            var allIds = new List<ObjectId> { anchorId };
            allIds.AddRange(deleteIds);

            foreach (var oid in allIds)
            {
                var ent = (Entity)tx.GetObject(oid, OpenMode.ForRead);
                try
                {
                    var ext = ent.GeometricExtents;
                    if (ext.MinPoint.X < minX) minX = ext.MinPoint.X;
                    if (ext.MaxPoint.X > maxX) maxX = ext.MaxPoint.X;
                    if (ext.MinPoint.Y < minY) minY = ext.MinPoint.Y;
                    if (ext.MaxPoint.Y > maxY) maxY = ext.MaxPoint.Y;
                }
                catch { /* use anchor position fallback */ }
            }

            if (minX == double.MaxValue)
            {
                // Fallback: no bounds available, use anchor position
                minX = anchorPos.X;
                maxY = anchorPos.Y;
                maxX = minX + (req.MtextWidth > 0 ? req.MtextWidth : 500);
            }

            double effectiveHeight = req.MedianHeight
                ?? ComputeMedianHeight(tx, anchorId, deleteIds);
            if (effectiveHeight <= 0) effectiveHeight = 2.5;

            double textHeight = req.ExplicitTextHeight
                ?? (req.ApplyUnicodeStyle
                    ? UnicodeStyleService.ComputeTargetHeight(tx, anchorStyleId, unicodeStyleId, effectiveHeight)
                    : effectiveHeight);
            textHeight = FinalTextScalePolicy.Apply(textHeight, req.FinalScale);

            double mtextWidth = req.MtextWidth > 0 ? req.MtextWidth : (maxX - minX);

            // Scale down for in-block: textHeight/width were computed in world space,
            // but MText created in block definition renders at (value × block scale).
            // Divide by the block reference's scale factor so world-rendered size matches intent.
            double scale = req.BlockScale > 0 ? req.BlockScale : 1.0;
            double blockDefTextHeight = textHeight / scale;
            double blockDefWidth = mtextWidth / scale;

            // Find the owner BlockTableRecord
            var ownerBtrId = anchorEnt.OwnerId;
            var ownerBtr = (BlockTableRecord)tx.GetObject(ownerBtrId, OpenMode.ForWrite);

            var newMText = new MText
            {
                Location = new Point3d(minX, maxY, 0),
                Rotation = rotation,
                TextHeight = blockDefTextHeight,
                Contents = req.NewText ?? string.Empty,
                TextStyleId = req.ApplyUnicodeStyle ? unicodeStyleId : anchorStyleId,
                Layer = targetLayer ?? layer,
                Width = blockDefWidth,
                Attachment = AttachmentPoint.TopLeft
            };

            ownerBtr.AppendEntity(newMText);
            tx.AddNewlyCreatedDBObject(newMText, true);
            newHandle = newMText.Handle.ToString();

            // Erase all old entities
            anchorEnt.Erase();
            foreach (var did in deleteIds)
            {
                tx.GetObject(did, OpenMode.ForWrite).Erase();
            }

            return true;
        }

        private static bool DoUpdate(
            Transaction tx,
            ObjectId anchorId,
            RewriteRequest req,
            ObjectId unicodeStyleId,
            string targetLayer,
            out string newHandle,
            out string error)
        {
            newHandle = null;
            error = null;

            var anchorEnt = (Entity)tx.GetObject(anchorId, OpenMode.ForWrite);
            if (!SetText(anchorEnt, req.NewText ?? string.Empty))
            {
                error = "anchor is not a text entity";
                return false;
            }

            if (req.ApplyUnicodeStyle)
            {
                UnicodeStyleService.ApplyToObject(tx, anchorEnt, unicodeStyleId);
                McpLayerService.ApplyToObject(anchorEnt, targetLayer);
            }

            ApplyFinalScale(anchorEnt, req.FinalScale);

            newHandle = req.AnchorHandle;
            return true;
        }

        private static void DoStyleOnly(
            Transaction tx,
            ObjectId anchorId,
            List<ObjectId> deleteIds,
            ObjectId unicodeStyleId,
            string targetLayer,
            double finalScale)
        {
            var anchorEnt = (Entity)tx.GetObject(anchorId, OpenMode.ForWrite);
            UnicodeStyleService.ApplyToObject(tx, anchorEnt, unicodeStyleId);
            McpLayerService.ApplyToObject(anchorEnt, targetLayer);
            ApplyFinalScale(anchorEnt, finalScale);

            foreach (var did in deleteIds)
            {
                var delEnt = (Entity)tx.GetObject(did, OpenMode.ForWrite);
                UnicodeStyleService.ApplyToObject(tx, delEnt, unicodeStyleId);
                McpLayerService.ApplyToObject(delEnt, targetLayer);
                ApplyFinalScale(delEnt, finalScale);
            }
        }

        private static double DetermineReportedFinalHeight(RewriteRequest req, double effectiveHeight)
        {
            double baseHeight = req.ExplicitTextHeight ?? effectiveHeight;
            return FinalTextScalePolicy.Apply(baseHeight, req.FinalScale);
        }

        private static void ApplyFinalScale(Entity ent, double scale)
        {
            switch (ent)
            {
                case AttributeReference att when att.Height > 0:
                    att.Height = FinalTextScalePolicy.Apply(att.Height, scale);
                    break;
                case DBText dbt when dbt.Height > 0:
                    dbt.Height = FinalTextScalePolicy.Apply(dbt.Height, scale);
                    break;
                case MText mt when mt.TextHeight > 0:
                    mt.TextHeight = FinalTextScalePolicy.Apply(mt.TextHeight, scale);
                    break;
                case MLeader ml:
                    double leaderHeight = ml.TextHeight > 0
                        ? ml.TextHeight
                        : (ml.MText?.TextHeight ?? 0);
                    double scaledLeaderHeight = FinalTextScalePolicy.Apply(leaderHeight, scale);
                    if (scaledLeaderHeight > 0)
                    {
                        ml.TextHeight = scaledLeaderHeight;
                    }

                    if (ml.MText != null && scaledLeaderHeight > 0)
                    {
                        var inner = ml.MText;
                        inner.TextHeight = scaledLeaderHeight;
                        ml.MText = inner;
                    }
                    break;
            }
        }

        private static bool TryExtractLocation(
            Entity ent,
            out Point3d pos,
            out double rotation,
            out ObjectId styleId,
            out string layer,
            out bool needsBaselineToTopOffset)
        {
            switch (ent)
            {
                case AttributeReference att:
                    pos = att.Position;
                    rotation = att.Rotation;
                    styleId = att.TextStyleId;
                    layer = att.Layer;
                    needsBaselineToTopOffset = true;
                    return true;
                case DBText dbt:
                    pos = dbt.Position;
                    rotation = dbt.Rotation;
                    styleId = dbt.TextStyleId;
                    layer = dbt.Layer;
                    needsBaselineToTopOffset = true;
                    return true;
                case MText mt:
                    pos = mt.Location;
                    rotation = mt.Rotation;
                    styleId = mt.TextStyleId;
                    layer = mt.Layer;
                    needsBaselineToTopOffset = false;
                    return true;
                case MLeader ml:
                    try
                    {
                        pos = ml.GetFirstVertex(0);
                    }
                    catch
                    {
                        pos = Point3d.Origin;
                    }
                    rotation = ml.MText != null ? ml.MText.Rotation : 0;
                    styleId = ml.TextStyleId;
                    layer = ml.Layer;
                    needsBaselineToTopOffset = false;
                    return true;
                default:
                    pos = Point3d.Origin;
                    rotation = 0;
                    styleId = ObjectId.Null;
                    layer = null;
                    needsBaselineToTopOffset = false;
                    return false;
            }
        }

        private static bool SetText(Entity ent, string text)
        {
            switch (ent)
            {
                case AttributeReference att:
                    att.TextString = text;
                    return true;
                case DBText dbt:
                    dbt.TextString = text;
                    return true;
                case MText mt:
                    mt.Contents = text;
                    return true;
                case MLeader ml:
                    if (ml.MText == null)
                    {
                        return false;
                    }
                    var inner = ml.MText;
                    inner.Contents = text;
                    ml.MText = inner;
                    return true;
                default:
                    return false;
            }
        }

        private static double ComputeMedianHeight(Transaction tx, ObjectId anchorId, List<ObjectId> deleteIds)
        {
            var heights = new List<double>();
            CollectHeight(tx, anchorId, heights);
            foreach (var did in deleteIds)
            {
                CollectHeight(tx, did, heights);
            }
            return Median(heights);
        }

        private static void CollectHeight(Transaction tx, ObjectId id, List<double> heights)
        {
            try
            {
                var obj = tx.GetObject(id, OpenMode.ForRead);
                switch (obj)
                {
                    case DBText t when t.Height > 0:
                        heights.Add(t.Height);
                        break;
                    case MText m when m.TextHeight > 0:
                        heights.Add(m.TextHeight);
                        break;
                    case MLeader ml when ml.TextHeight > 0:
                        heights.Add(ml.TextHeight);
                        break;
                    case AttributeReference a when a.Height > 0:
                        heights.Add(a.Height);
                        break;
                }
            }
            catch
            {
            }
        }

        private static double Median(List<double> values)
        {
            if (values == null || values.Count == 0)
            {
                return 0;
            }

            var sorted = values.OrderBy(v => v).ToList();
            int mid = sorted.Count / 2;
            return sorted.Count % 2 == 1
                ? sorted[mid]
                : (sorted[mid - 1] + sorted[mid]) / 2.0;
        }

        private static bool TryGetObjectId(Database db, string handleStr, out ObjectId id)
        {
            id = ObjectId.Null;
            if (string.IsNullOrWhiteSpace(handleStr))
            {
                return false;
            }

            if (!long.TryParse(handleStr, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long h))
            {
                return false;
            }

            try
            {
                id = db.GetObjectId(false, new Handle(h), 0);
                return !id.IsNull;
            }
            catch
            {
                return false;
            }
        }

        private static RewriteResult Fail(RewriteRequest req, string error)
        {
            return new RewriteResult
            {
                Ok = false,
                Action = req.Action,
                AnchorHandle = req.AnchorHandle,
                Error = error
            };
        }
    }
}
