using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace Bimwright.Dwg.Plugin.Cad
{
    internal static class CadEntityProperties
    {
        internal static object Describe(Entity entity, Transaction tx, bool includeGeometry)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            var result = new Dictionary<string, object>
            {
                ["handle"] = entity.Handle.ToString(),
                ["type"] = GetEntityType(entity),
                ["layer"] = entity.Layer,
                ["color_index"] = (int)entity.ColorIndex
            };

            if (!includeGeometry)
            {
                return result;
            }

            AddExtents(result, entity);

            switch (entity)
            {
                case Line line:
                    DescribeLine(result, line);
                    break;
                case Circle circle:
                    DescribeCircle(result, circle);
                    break;
                case Arc arc:
                    DescribeArc(result, arc);
                    break;
                case Polyline polyline:
                    DescribePolyline(result, polyline);
                    break;
                case DBText text:
                    DescribeDbText(result, text);
                    break;
                case MText mText:
                    DescribeMText(result, mText);
                    break;
                case MLeader mLeader:
                    DescribeMLeader(result, mLeader);
                    break;
                case Table table:
                    DescribeTable(result, table);
                    break;
                case BlockReference blockReference:
                    DescribeBlockReference(result, blockReference, tx);
                    break;
                case Hatch hatch:
                    DescribeHatch(result, hatch);
                    break;
                case Ellipse ellipse:
                    DescribeEllipse(result, ellipse);
                    break;
            }

            return result;
        }

        private static void DescribeLine(IDictionary<string, object> result, Line line)
        {
            result["start"] = Point(line.StartPoint);
            result["end"] = Point(line.EndPoint);
            result["length"] = line.StartPoint.DistanceTo(line.EndPoint);
        }

        private static void DescribeCircle(IDictionary<string, object> result, Circle circle)
        {
            result["center"] = Point(circle.Center);
            result["radius"] = circle.Radius;
            result["diameter"] = circle.Radius * 2d;
            result["circumference"] = Math.PI * circle.Radius * 2d;
            result["area"] = Math.PI * circle.Radius * circle.Radius;
        }

        private static void DescribeArc(IDictionary<string, object> result, Arc arc)
        {
            result["center"] = Point(arc.Center);
            result["radius"] = arc.Radius;
            result["start_angle"] = arc.StartAngle;
            result["end_angle"] = arc.EndAngle;
            result["included_angle"] = IncludedAngle(arc.StartAngle, arc.EndAngle);
            if (TryGetCurveLength(arc, out var length))
            {
                result["length"] = length;
            }
        }

        private static void DescribePolyline(IDictionary<string, object> result, Polyline polyline)
        {
            result["vertex_count"] = polyline.NumberOfVertices;
            result["is_closed"] = polyline.Closed;

            if (TryGetCurveLength(polyline, out var length))
            {
                result["length"] = length;
            }

            if (TryGetArea(polyline, out var area))
            {
                result["area"] = area;
            }

            var vertices = new List<object>();
            for (var i = 0; i < polyline.NumberOfVertices; i++)
            {
                var point = polyline.GetPoint3dAt(i);
                vertices.Add(new
                {
                    index = i,
                    x = point.X,
                    y = point.Y,
                    z = point.Z,
                    bulge = polyline.GetBulgeAt(i)
                });
            }

            result["vertices"] = vertices.ToArray();
        }

        private static void DescribeDbText(IDictionary<string, object> result, DBText text)
        {
            result["text"] = text.TextString;
            result["position"] = Point(text.Position);
            result["height"] = text.Height;
            result["rotation"] = text.Rotation;
            result["horizontal_mode"] = text.HorizontalMode.ToString();
            result["vertical_mode"] = text.VerticalMode.ToString();
        }

        private static void DescribeMText(IDictionary<string, object> result, MText mText)
        {
            result["text"] = mText.Contents;
            result["location"] = Point(mText.Location);
            result["text_height"] = mText.TextHeight;
            result["width"] = mText.Width;
            result["rotation"] = mText.Rotation;
            result["attachment"] = mText.Attachment.ToString();
        }

        private static void DescribeMLeader(IDictionary<string, object> result, MLeader mLeader)
        {
            result["text"] = mLeader.MText != null ? mLeader.MText.Contents : string.Empty;
            result["text_height"] = mLeader.TextHeight;
            result["leader_count"] = mLeader.LeaderCount;
            result["leader_line_count"] = mLeader.LeaderLineCount;
            try
            {
                result["first_vertex"] = Point(mLeader.GetFirstVertex(0));
                result["last_vertex"] = Point(mLeader.GetLastVertex(0));
            }
            catch
            {
                result["first_vertex"] = null;
                result["last_vertex"] = null;
            }
        }

        private static void DescribeTable(IDictionary<string, object> result, Table table)
        {
            result["position"] = Point(table.Position);
            result["rows"] = table.Rows.Count;
            result["columns"] = table.Columns.Count;
        }

        private static void DescribeBlockReference(
            IDictionary<string, object> result,
            BlockReference blockReference,
            Transaction tx)
        {
            result["position"] = Point(blockReference.Position);
            result["rotation"] = blockReference.Rotation;
            result["scale"] = Scale(blockReference.ScaleFactors);
            result["attribute_count"] = blockReference.AttributeCollection.Count;

            var name = TryGetBlockName(blockReference, tx);
            if (!string.IsNullOrEmpty(name))
            {
                result["name"] = name;
            }
        }

        private static void DescribeHatch(IDictionary<string, object> result, Hatch hatch)
        {
            result["pattern_name"] = hatch.PatternName;
            result["pattern_scale"] = hatch.PatternScale;
            result["pattern_angle"] = hatch.PatternAngle;
            result["is_associative"] = hatch.Associative;
            result["loop_count"] = hatch.NumberOfLoops;

            if (TryGetArea(hatch, out var area))
            {
                result["area"] = area;
            }
        }

        private static void DescribeEllipse(IDictionary<string, object> result, Ellipse ellipse)
        {
            result["center"] = Point(ellipse.Center);
            result["major_axis"] = Vector(ellipse.MajorAxis);
            result["radius_ratio"] = ellipse.RadiusRatio;
            result["start_angle"] = ellipse.StartAngle;
            result["end_angle"] = ellipse.EndAngle;

            if (TryGetCurveLength(ellipse, out var length))
            {
                result["length"] = length;
            }
        }

        private static void AddExtents(IDictionary<string, object> result, Entity entity)
        {
            try
            {
                var extents = entity.GeometricExtents;
                result["extents"] = new
                {
                    min = Point(extents.MinPoint),
                    max = Point(extents.MaxPoint)
                };
            }
            catch
            {
                result["extents"] = null;
            }
        }

        private static string GetEntityType(Entity entity)
        {
            if (entity is Line) return "Line";
            if (entity is Circle) return "Circle";
            if (entity is Arc) return "Arc";
            if (entity is Polyline) return "Polyline";
            if (entity is DBText) return entity.GetType().Name;
            if (entity is MText) return "MText";
            if (entity is MLeader) return "MLeader";
            if (entity is Table) return "Table";
            if (entity is BlockReference) return "BlockReference";
            if (entity is Hatch) return "Hatch";
            if (entity is Ellipse) return "Ellipse";
            return entity.GetType().Name;
        }

        private static string TryGetBlockName(BlockReference blockReference, Transaction tx)
        {
            if (tx == null || blockReference.BlockTableRecord.IsNull)
            {
                return null;
            }

            try
            {
                var blockTableRecord = (BlockTableRecord)tx.GetObject(blockReference.BlockTableRecord, OpenMode.ForRead);
                return blockTableRecord.Name;
            }
            catch
            {
                return null;
            }
        }

        private static bool TryGetCurveLength(Curve curve, out double length)
        {
            length = 0d;
            try
            {
                length = curve.GetDistanceAtParameter(curve.EndParam) -
                    curve.GetDistanceAtParameter(curve.StartParam);
                return true;
            }
            catch
            {
                length = 0d;
                return false;
            }
        }

        private static bool TryGetArea(Entity entity, out double area)
        {
            area = 0d;
            try
            {
                switch (entity)
                {
                    case Polyline polyline:
                        area = polyline.Area;
                        return true;
                    case Hatch hatch:
                        area = hatch.Area;
                        return true;
                    default:
                        return false;
                }
            }
            catch
            {
                area = 0d;
                return false;
            }
        }

        private static double IncludedAngle(double startAngle, double endAngle)
        {
            var angle = endAngle - startAngle;
            while (angle < 0d)
            {
                angle += Math.PI * 2d;
            }

            return angle;
        }

        private static object Point(Point3d point)
        {
            return new
            {
                x = point.X,
                y = point.Y,
                z = point.Z
            };
        }

        private static object Vector(Vector3d vector)
        {
            return new
            {
                x = vector.X,
                y = vector.Y,
                z = vector.Z,
                length = vector.Length
            };
        }

        private static object Scale(Scale3d scale)
        {
            return new
            {
                x = scale.X,
                y = scale.Y,
                z = scale.Z
            };
        }
    }
}
