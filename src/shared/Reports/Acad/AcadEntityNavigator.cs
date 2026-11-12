using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Bimwright.Dwg.Plugin.Reports.Model;
using Bimwright.Dwg.Plugin.Reports.Presentation;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace Bimwright.Dwg.Plugin.Reports.Acad
{
    /// <summary>
    /// A report link click in AutoCAD: activate the link's drawing, switch to the layout that owns most of its
    /// entities, zoom to them and make them the pickfirst selection. Main thread, application context only.
    /// Changes the view, the current layout and the selection; never the drawing database.
    /// </summary>
    internal static class AcadEntityNavigator
    {
        /// <summary>Margin around the zoomed entities, as a share of their size.</summary>
        private const double Margin = 0.1;

        /// <summary>Null with <paramref name="activationRequested"/> set: the drawing was made active; call again on the next idle.</summary>
        internal static ReportNavigationResult Navigate(ReportNavigationRequest request, bool activationDone, out bool activationRequested)
        {
            activationRequested = false;
            var documents = Application.DocumentManager;
            var doc = Find(request?.Document);
            if (doc == null) return Result(ReportNavigationOutcome.NotMatched);
            if (!ReferenceEquals(documents.MdiActiveDocument, doc))
            {
                if (activationDone) return Result(ReportNavigationOutcome.Failed);
                documents.MdiActiveDocument = doc;
                activationRequested = true;
                return null;
            }

            using (doc.LockDocument())
            {
                var db = doc.Database;
                var byOwner = new Dictionary<ObjectId, List<ObjectId>>();
                var missing = 0;
                using (var tx = db.TransactionManager.StartOpenCloseTransaction())
                {
                    var seen = new HashSet<ObjectId>();
                    foreach (var target in request.Entities ?? (IReadOnlyList<ReportEntity>)new ReportEntity[0])
                    {
                        var id = Resolve(db, target?.Handle);
                        if (id.IsNull) { missing++; continue; }
                        if (!seen.Add(id)) continue;
                        if (!(tx.GetObject(id, OpenMode.ForRead, false) is Entity entity)) { missing++; continue; }
                        if (!byOwner.TryGetValue(entity.OwnerId, out var list)) byOwner[entity.OwnerId] = list = new List<ObjectId>();
                        list.Add(id);
                    }

                    // Only model space and paper-space layouts can be shown; an entity inside a block definition cannot.
                    var space = byOwner
                        .Where(pair => tx.GetObject(pair.Key, OpenMode.ForRead, false) is BlockTableRecord record && record.IsLayout)
                        .OrderByDescending(pair => pair.Value.Count)
                        .FirstOrDefault();
                    if (space.Value == null)
                        return Result(byOwner.Count == 0 ? ReportNavigationOutcome.EntitiesGone : ReportNavigationOutcome.NoExtents, missing: missing);

                    var owner = (BlockTableRecord)tx.GetObject(space.Key, OpenMode.ForRead, false);
                    var layoutName = ((Layout)tx.GetObject(owner.LayoutId, OpenMode.ForRead, false)).LayoutName;
                    Extents3d? box = null;
                    foreach (var id in space.Value)
                    {
                        try
                        {
                            var extents = ((Entity)tx.GetObject(id, OpenMode.ForRead, false)).GeometricExtents;
                            if (box.HasValue) { var b = box.Value; b.AddExtents(extents); box = b; }
                            else box = extents;
                        }
                        catch (Autodesk.AutoCAD.Runtime.Exception) { } // Some entities (empty text, rays) have no extents.
                    }
                    tx.Commit();

                    var layouts = LayoutManager.Current;
                    if (!string.Equals(layouts.CurrentLayout, layoutName, StringComparison.OrdinalIgnoreCase)) layouts.CurrentLayout = layoutName;
                    if (box.HasValue) Zoom(doc, box.Value);
                    doc.Editor.SetImpliedSelection(space.Value.ToArray());
                    return Result(box.HasValue ? ReportNavigationOutcome.Selected : ReportNavigationOutcome.NoExtents,
                        space.Value.Count, missing, layoutName);
                }
            }
        }

        /// <summary>The open drawing a link belongs to: the same open document, else the same saved file reopened.</summary>
        internal static Document Find(ReportDocument target)
        {
            if (target == null) return null;
            var open = AcadReportResolver.Find(target.Token);
            if (open != null) return open;
            foreach (Document document in Application.DocumentManager)
                if (ReportDocumentMatch.Matches(target, AcadReportsRuntime.Identify(document))) return document;
            return null;
        }

        private static ObjectId Resolve(Database db, string handle)
        {
            if (string.IsNullOrEmpty(handle) || !long.TryParse(handle, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value))
                return ObjectId.Null;
            return db.TryGetObjectId(new Handle(value), out var id) && !id.IsNull && id.IsValid && !id.IsErased ? id : ObjectId.Null;
        }

        private static void Zoom(Document doc, Extents3d box)
        {
            var size = box.MaxPoint - box.MinPoint;
            // A point or a horizontal line has no height: keep a visible window around it.
            var pad = Math.Max(Math.Max(size.X, size.Y) * Margin, 1.0);
            var min = new Point3d(box.MinPoint.X - pad, box.MinPoint.Y - pad, box.MinPoint.Z);
            var max = new Point3d(box.MaxPoint.X + pad, box.MaxPoint.Y + pad, box.MaxPoint.Z);
            Bimwright.Dwg.Plugin.View.ViewZoomService.ZoomWindow(doc, min, max);
        }

        private static ReportNavigationResult Result(ReportNavigationOutcome outcome, int selected = 0, int missing = 0, string layout = null)
            => new ReportNavigationResult { Outcome = outcome, SelectedCount = selected, MissingCount = missing, LayoutName = layout };
    }
}
