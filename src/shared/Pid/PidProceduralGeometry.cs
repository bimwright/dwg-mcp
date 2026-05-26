using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace Bimwright.Dwg.Plugin.Pid
{
    public static class PidProceduralGeometry
    {
        public static List<Entity> DrawPump(Point3d center, double scale, double rotationRad)
        {
            var entities = new List<Entity>();

            // 1. Circle
            var circle = new Circle(Point3d.Origin, Vector3d.ZAxis, 5.0);
            entities.Add(circle);

            // 2. Inscribed triangle
            var triangle = new Polyline();
            triangle.AddVertexAt(0, new Point2d(0, 5.0), 0, 0, 0);
            triangle.AddVertexAt(1, new Point2d(-4.33, -2.5), 0, 0, 0);
            triangle.AddVertexAt(2, new Point2d(4.33, -2.5), 0, 0, 0);
            triangle.Closed = true;
            entities.Add(triangle);

            ApplyTransform(entities, center, scale, rotationRad);
            return entities;
        }

        public static List<Entity> DrawTank(Point3d center, double scale, double rotationRad, string symbol)
        {
            var entities = new List<Entity>();

            bool isHorizontal = symbol != null && symbol.Contains("HORIZONTAL", StringComparison.OrdinalIgnoreCase);
            double w = isHorizontal ? 20.0 : 10.0;
            double h = isHorizontal ? 10.0 : 20.0;

            var tank = new Polyline();
            tank.AddVertexAt(0, new Point2d(-w / 2, -h / 2), 0, 0, 0);
            tank.AddVertexAt(1, new Point2d(w / 2, -h / 2), 0, 0, 0);
            tank.AddVertexAt(2, new Point2d(w / 2, h / 2), 0, 0, 0);
            tank.AddVertexAt(3, new Point2d(-w / 2, h / 2), 0, 0, 0);
            tank.Closed = true;
            entities.Add(tank);

            ApplyTransform(entities, center, scale, rotationRad);
            return entities;
        }

        public static List<Entity> DrawValve(Point3d center, double scale, double rotationRad)
        {
            var entities = new List<Entity>();

            // Left triangle
            var leftTri = new Polyline();
            leftTri.AddVertexAt(0, new Point2d(-5.0, 3.0), 0, 0, 0);
            leftTri.AddVertexAt(1, new Point2d(0, 0), 0, 0, 0);
            leftTri.AddVertexAt(2, new Point2d(-5.0, -3.0), 0, 0, 0);
            leftTri.Closed = true;
            entities.Add(leftTri);

            // Right triangle
            var rightTri = new Polyline();
            rightTri.AddVertexAt(0, new Point2d(5.0, 3.0), 0, 0, 0);
            rightTri.AddVertexAt(1, new Point2d(0, 0), 0, 0, 0);
            rightTri.AddVertexAt(2, new Point2d(5.0, -3.0), 0, 0, 0);
            rightTri.Closed = true;
            entities.Add(rightTri);

            ApplyTransform(entities, center, scale, rotationRad);
            return entities;
        }

        public static List<Entity> DrawGenericEquipment(Point3d center, double scale, double rotationRad, string symbol)
        {
            var entities = new List<Entity>();

            // Rectangle
            var rect = new Polyline();
            rect.AddVertexAt(0, new Point2d(-7.5, -7.5), 0, 0, 0);
            rect.AddVertexAt(1, new Point2d(7.5, -7.5), 0, 0, 0);
            rect.AddVertexAt(2, new Point2d(7.5, 7.5), 0, 0, 0);
            rect.AddVertexAt(3, new Point2d(-7.5, 7.5), 0, 0, 0);
            rect.Closed = true;
            entities.Add(rect);

            // Text
            var text = new DBText();
            text.Height = 2.0;
            text.TextString = symbol ?? "EQUIPMENT";
            // Simple horizontal centering approximation
            double textOffset = Math.Min(6.0, text.TextString.Length * 0.7);
            text.Position = new Point3d(-textOffset, -1.0, 0);
            entities.Add(text);

            ApplyTransform(entities, center, scale, rotationRad);
            return entities;
        }

        public static Polyline DrawFlowArrow(Point3d position, Vector3d direction, double scale)
        {
            double len = 4.0 * scale;
            double width = 2.0 * scale;

            Vector3d dirNormalized = direction.GetNormal();
            Vector3d normal = new Vector3d(-dirNormalized.Y, dirNormalized.X, 0);

            Point3d tip = position + dirNormalized * (len / 2.0);
            Point3d back = position - dirNormalized * (len / 2.0);
            Point3d left = back + normal * (width / 2.0);
            Point3d right = back - normal * (width / 2.0);

            var arrow = new Polyline();
            arrow.AddVertexAt(0, new Point2d(tip.X, tip.Y), 0, 0, 0);
            arrow.AddVertexAt(1, new Point2d(left.X, left.Y), 0, 0, 0);
            arrow.AddVertexAt(2, new Point2d(right.X, right.Y), 0, 0, 0);
            arrow.Closed = true;

            return arrow;
        }

        private static void ApplyTransform(List<Entity> entities, Point3d center, double scale, double rotationRad)
        {
            var transform = Matrix3d.Displacement(center - Point3d.Origin);
            if (rotationRad != 0.0)
            {
                transform = transform * Matrix3d.Rotation(rotationRad, Vector3d.ZAxis, Point3d.Origin);
            }
            if (scale != 1.0)
            {
                transform = transform * Matrix3d.Scaling(scale, Point3d.Origin);
            }

            foreach (var ent in entities)
            {
                ent.TransformBy(transform);
            }
        }
    }
}
