// Geometry/API surface for compiling the actual legacy and paged query sources.
// Query* controls belong only to these simulated database fixtures.
using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.Geometry;

namespace Autodesk.AutoCAD.Colors
{
    public enum ColorMethod { ByAci }

    public sealed class Color
    {
        internal Color(short colorIndex) => ColorIndex = colorIndex;
        public short ColorIndex { get; }
        public static Color FromColorIndex(ColorMethod method, short index) => new Color(index);
    }
}

namespace Autodesk.AutoCAD.Geometry
{
    public partial struct Point3d
    {
        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public Point3d(double x, double y, double z) { X = x; Y = y; Z = z; }
        public double DistanceTo(Point3d other)
            => Math.Sqrt((X - other.X) * (X - other.X) + (Y - other.Y) * (Y - other.Y) + (Z - other.Z) * (Z - other.Z));
    }
    public readonly struct Vector3d
    {
        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
        public Vector3d(double x, double y, double z) { X = x; Y = y; Z = z; }
    }
    public readonly struct Scale3d
    {
        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public Scale3d(double x, double y, double z) { X = x; Y = y; Z = z; }
    }
}

namespace Autodesk.AutoCAD.DatabaseServices
{
    public enum UnitsValue { Undefined = 0, Inches = 1, Feet = 2, Millimeters = 4, Centimeters = 5, Meters = 6 }
    public readonly struct Extents3d
    {
        public Point3d MinPoint { get; }
        public Point3d MaxPoint { get; }
        public Extents3d(Point3d min, Point3d max) { MinPoint = min; MaxPoint = max; }
    }
    public partial class Database
    {
        public UnitsValue Insunits { get; set; }
    }
    public partial class Entity
    {
        private short colorIndex = 256;
        internal Point3d[] QueryVertices = Array.Empty<Point3d>();
        internal Exception QueryExtentsFailure;
        internal Func<Extents3d> QueryExtentsOverride;
        internal int QueryExtentsReads;
        public short ColorIndex { get => colorIndex; set { colorIndex = value; ProfileWrite(); } }
        public Extents3d GeometricExtents
        {
            get
            {
                QueryExtentsReads++;
                if (QueryExtentsFailure != null) throw QueryExtentsFailure;
                if (QueryExtentsOverride != null) return QueryExtentsOverride();
                if (QueryVertices.Length == 0) throw new InvalidOperationException("No simulated geometry vertices.");
                // Recompute a real AABB on every getter call. No precomputed bounds or cache.
                var first = QueryVertices[0];
                double minX = first.X, minY = first.Y, minZ = first.Z, maxX = first.X, maxY = first.Y, maxZ = first.Z;
                for (var i = 1; i < QueryVertices.Length; i++)
                {
                    var point = QueryVertices[i];
                    minX = Math.Min(minX, point.X); minY = Math.Min(minY, point.Y); minZ = Math.Min(minZ, point.Z);
                    maxX = Math.Max(maxX, point.X); maxY = Math.Max(maxY, point.Y); maxZ = Math.Max(maxZ, point.Z);
                }
                return new Extents3d(new Point3d(minX, minY, minZ), new Point3d(maxX, maxY, maxZ));
            }
        }
    }
    public class Curve : Entity
    {
        public double StartParam { get; set; }
        public double EndParam { get; set; } = 1;
        public double GetDistanceAtParameter(double parameter) => parameter;
    }
    public partial class Line
    {
        public Point3d StartPoint { get; set; }
        public Point3d EndPoint { get; set; }
    }
    public partial class Circle
    {
        public Point3d Center { get; set; }
        public double Radius { get; set; }
    }
    public class Arc : Curve
    {
        public Point3d Center { get; set; }
        public double Radius { get; set; }
        public double StartAngle { get; set; }
        public double EndAngle { get; set; }
    }
    public class Polyline : Curve
    {
        internal List<Point3d> QueryPolylineVertices { get; } = new();
        internal int QueryVertexReads;
        public int NumberOfVertices => QueryPolylineVertices.Count;
        public bool Closed { get; set; }
        public double Area { get; set; }
        public Point3d GetPoint3dAt(int index) { QueryVertexReads++; return QueryPolylineVertices[index]; }
        public double GetBulgeAt(int index) => 0;
    }
    public enum TextHorizontalMode { TextLeft, TextCenter, TextRight }
    public enum TextVerticalMode { TextBase, TextBottom, TextVertMid, TextTop }
    public partial class DBText
    {
        public string TextString { get; set; }
        public Point3d Position { get; set; }
        public double Height { get; set; }
        public double Rotation { get; set; }
        public TextHorizontalMode HorizontalMode { get; set; }
        public TextVerticalMode VerticalMode { get; set; }
    }
    public enum AttachmentPoint { TopLeft, TopCenter, MiddleCenter }
    public partial class MText : Entity, IDisposable
    {
        private string contents;
        private string plainText;
        internal int ProfilePlainTextReads;
        public string Contents { get { if (TextReadContentsFailure != null) throw TextReadContentsFailure; return contents; } set => contents = value; }
        public string Text { get { ProfilePlainTextReads++; return plainText; } set => plainText = value; }
        public Point3d Location { get; set; }
        public double TextHeight { get; set; }
        public double Width { get; set; }
        public double Rotation { get; set; }
        public AttachmentPoint Attachment { get; set; }
        public void Dispose() { TextReadDisposeCalls++; }
    }
    public partial class BlockReference
    {
        public Point3d Position { get; set; }
        public double Rotation { get; set; }
        public Scale3d ScaleFactors { get; set; }
        public AttributeCollection AttributeCollection { get; } = new();
    }
    public enum ContentType { NoneContent, BlockContent, MTextContent, ToleranceContent }
    public partial class MLeader : Entity
    {
        public ContentType ContentType { get; set; } = ContentType.MTextContent;
        private MText mtext;
        public MText MText { get { TextReadCloneReads++; return TextReadMTextFactory == null ? mtext : TextReadMTextFactory(); } set => mtext = value; }
        public double TextHeight { get; set; }
        public int LeaderCount { get; set; }
        public int LeaderLineCount { get; set; }
        public Point3d GetFirstVertex(int line) { if (TextReadAnchorFailure != null) throw TextReadAnchorFailure; return TextReadAnchor; }
        public Point3d GetLastVertex(int line) => default;
    }
    public class Table : Entity
    {
        public Point3d Position { get; set; }
        public List<object> Rows { get; } = new();
        public List<object> Columns { get; } = new();
    }
    public class Hatch : Entity
    {
        public string PatternName { get; set; }
        public double PatternScale { get; set; }
        public double PatternAngle { get; set; }
        public bool Associative { get; set; }
        public int NumberOfLoops { get; set; }
        public double Area { get; set; }
    }
    public class Ellipse : Curve
    {
        public Point3d Center { get; set; }
        public Vector3d MajorAxis { get; set; }
        public double RadiusRatio { get; set; }
        public double StartAngle { get; set; }
        public double EndAngle { get; set; }
    }
}
