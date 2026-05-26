using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace Bimwright.Dwg.Plugin.View
{
    internal static class ViewZoomService
    {
        internal static ViewInfo ZoomExtents(Document doc)
        {
            var ed = doc.Editor;
            var db = doc.Database;

            Point3d min = db.Extmin;
            Point3d max = db.Extmax;

            double width = max.X - min.X;
            double height = max.Y - min.Y;

            if (width <= 0) width = 100d;
            if (height <= 0) height = 100d;

            Point3d center3d = new Point3d((min.X + max.X) / 2.0, (min.Y + max.Y) / 2.0, (min.Z + max.Z) / 2.0);

            return SetView(ed, center3d, width, height);
        }

        internal static ViewInfo ZoomWindow(Document doc, Point3d corner1, Point3d corner2)
        {
            var ed = doc.Editor;

            double minX = Math.Min(corner1.X, corner2.X);
            double maxX = Math.Max(corner1.X, corner2.X);
            double minY = Math.Min(corner1.Y, corner2.Y);
            double maxY = Math.Max(corner1.Y, corner2.Y);
            double minZ = Math.Min(corner1.Z, corner2.Z);
            double maxZ = Math.Max(corner1.Z, corner2.Z);

            double width = maxX - minX;
            double height = maxY - minY;

            if (width <= 0) width = 1d;
            if (height <= 0) height = 1d;

            Point3d center3d = new Point3d((minX + maxX) / 2.0, (minY + maxY) / 2.0, (minZ + maxZ) / 2.0);

            return SetView(ed, center3d, width, height);
        }

        internal static ViewInfo ZoomToEntity(Document doc, Entity entity)
        {
            var ed = doc.Editor;
            Extents3d ext = entity.GeometricExtents;

            double width = ext.MaxPoint.X - ext.MinPoint.X;
            double height = ext.MaxPoint.Y - ext.MinPoint.Y;

            if (width <= 0) width = 1d;
            if (height <= 0) height = 1d;

            Point3d center3d = new Point3d(
                (ext.MinPoint.X + ext.MaxPoint.X) / 2.0,
                (ext.MinPoint.Y + ext.MaxPoint.Y) / 2.0,
                (ext.MinPoint.Z + ext.MaxPoint.Z) / 2.0);

            return SetView(ed, center3d, width, height);
        }

        private static ViewInfo SetView(Editor ed, Point3d center3d, double width, double height)
        {
            using (var view = ed.GetCurrentView())
            {
                Point3d target = view.Target;
                Vector3d viewDir = view.ViewDirection;
                double twist = view.ViewTwist;

                Vector3d zAxis = viewDir.GetNormal();
                Vector3d xAxis;
                if (zAxis.X == 0d && zAxis.Y == 0d)
                {
                    xAxis = Vector3d.XAxis;
                }
                else
                {
                    xAxis = Vector3d.ZAxis.CrossProduct(zAxis).GetNormal();
                }
                Vector3d yAxis = zAxis.CrossProduct(xAxis).GetNormal();

                xAxis = xAxis.RotateBy(twist, zAxis);
                yAxis = yAxis.RotateBy(twist, zAxis);

                Vector3d rel = center3d - target;
                double dcsX = rel.DotProduct(xAxis);
                double dcsY = rel.DotProduct(yAxis);

                Point2d center2d = new Point2d(dcsX, dcsY);

                view.CenterPoint = center2d;
                view.Height = height;
                view.Width = width;

                ed.SetCurrentView(view);

                return new ViewInfo(
                    center: new double[] { center2d.X, center2d.Y },
                    width: width,
                    height: height,
                    target: new double[] { view.Target.X, view.Target.Y, view.Target.Z }
                );
            }
        }
    }

    internal class ViewInfo
    {
        public ViewInfo(double[] center, double width, double height, double[] target)
        {
            Center = center;
            Width = width;
            Height = height;
            Target = target;
        }

        public double[] Center { get; }
        public double Width { get; }
        public double Height { get; }
        public double[] Target { get; }
    }
}
