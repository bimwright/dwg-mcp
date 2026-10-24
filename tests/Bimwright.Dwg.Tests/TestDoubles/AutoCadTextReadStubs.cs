// Only host API doubles and unreachable writer guards. Extraction and clustering
// tests compile and execute the real production sources.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace Autodesk.AutoCAD.Geometry
{
    public partial struct Point3d
    {
        public static Point3d Origin => new Point3d(0, 0, 0);
        public Point3d TransformBy(Matrix3d matrix) => matrix.Transform(this);
    }
    public readonly struct CoordinateSystem3d
    {
        public Vector3d Yaxis { get; }
        internal CoordinateSystem3d(Vector3d yaxis) { Yaxis = yaxis; }
    }
    public readonly struct Matrix3d
    {
        private readonly double[] values;
        public Matrix3d(double[] values) { this.values = (double[])values.Clone(); }
        public static Matrix3d Identity => new Matrix3d(new double[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 });
        public static Matrix3d Displacement(Vector3d vector)
            => new Matrix3d(new double[] { 1, 0, 0, vector.X, 0, 1, 0, vector.Y, 0, 0, 1, vector.Z, 0, 0, 0, 1 });
        public CoordinateSystem3d CoordinateSystem3d => new CoordinateSystem3d(new Vector3d(Data[1], Data[5], Data[9]));
        private double[] Data => values ?? Identity.values;
        internal Point3d Transform(Point3d point)
        {
            var m = Data;
            return new Point3d(m[0] * point.X + m[1] * point.Y + m[2] * point.Z + m[3],
                m[4] * point.X + m[5] * point.Y + m[6] * point.Z + m[7], m[8] * point.X + m[9] * point.Y + m[10] * point.Z + m[11]);
        }
        public Matrix3d PostMultiplyBy(Matrix3d other)
        {
            var a = Data; var b = other.Data; var result = new double[16];
            for (var row = 0; row < 4; row++) for (var column = 0; column < 4; column++)
                for (var k = 0; k < 4; k++) result[row * 4 + column] += a[row * 4 + k] * b[k * 4 + column];
            return new Matrix3d(result);
        }
    }
}

namespace Autodesk.AutoCAD.EditorInput
{
    public enum PromptStatus { OK = 5100, None = 5000, Error = -5001, Cancel = -5002 }
    public sealed class SelectedObject
    {
        public ObjectId ObjectId { get; }
        internal SelectedObject(ObjectId id) { ObjectId = id; }
    }
    public sealed class SelectionSet : IEnumerable
    {
        private readonly SelectedObject[] selected;
        internal SelectionSet(IEnumerable<ObjectId> ids) { selected = ids.Select(id => new SelectedObject(id)).ToArray(); }
        public int Count => selected.Length;
        public IEnumerator GetEnumerator() => selected.GetEnumerator();
    }
    public sealed class PromptSelectionResult
    {
        public PromptStatus Status { get; }
        public SelectionSet Value { get; }
        internal PromptSelectionResult(PromptStatus status, SelectionSet value) { Status = status; Value = value; }
    }
    public partial class Editor
    {
        internal PromptSelectionResult TextReadSelection;
        internal int TextReadSelectCalls, TextReadSetSelectionCalls;
        public PromptSelectionResult SelectImplied()
        {
            TextReadSelectCalls++;
            return TextReadSelection ?? new PromptSelectionResult(PromptStatus.None, null);
        }
        public void SetImpliedSelection(ObjectId[] ids)
        { TextReadSetSelectionCalls++; throw new InvalidOperationException("Selection mutation is forbidden in text read tests."); }
    }
}

namespace Autodesk.AutoCAD.DatabaseServices
{
    public class AttributeReference : DBText { }
    public class AttributeDefinition : DBText { public bool Constant { get; set; } }
    public sealed class AttributeCollection : IEnumerable
    {
        private readonly List<ObjectId> ids = new();
        public int Count => ids.Count;
        internal void Add(ObjectId id) => ids.Add(id);
        public IEnumerator GetEnumerator() => ids.GetEnumerator();
    }
    public partial class BlockReference { public Matrix3d BlockTransform { get; set; } = Matrix3d.Identity; }
    public partial class MText
    {
        internal Exception TextReadContentsFailure;
        internal int TextReadDisposeCalls;
    }
    public partial class MLeader
    {
        internal Func<MText> TextReadMTextFactory;
        internal int TextReadCloneReads;
        internal Point3d TextReadAnchor;
        internal Exception TextReadAnchorFailure;
    }
}

namespace Bimwright.Dwg.Plugin
{
    internal sealed class UnicodeStyleInfo { public ObjectId StyleId { get; set; } }
    internal static partial class UnicodeStyleService
    {
        internal static int TextReadEnsureStyleCalls;
        internal static UnicodeStyleInfo EnsureStyle(Database database)
        {
            TextReadEnsureStyleCalls++;
            throw new InvalidOperationException("Unreachable styling path in read-only text tests.");
        }
    }
}
namespace Bimwright.Dwg.Plugin.Rewriting
{
    internal static class RewriteExecutor
    {
        internal static int TextReadExecuteCalls;
        public static RewriteResult Execute(Database database, RewriteRequest request, ObjectId style)
        {
            TextReadExecuteCalls++;
            throw new InvalidOperationException("Unreachable write path in read-only text tests.");
        }
    }
}
