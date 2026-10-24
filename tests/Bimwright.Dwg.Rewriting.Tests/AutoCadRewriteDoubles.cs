using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.Geometry;

// Managed boundary doubles only: no Autodesk assemblies, fonts or native host.
namespace Autodesk.AutoCAD.Geometry
{
    public readonly struct Point3d
    {
        public Point3d(double x, double y, double z) { X = x; Y = y; Z = z; }
        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public static Point3d Origin => new Point3d(0, 0, 0);
    }
}

namespace Autodesk.AutoCAD.DatabaseServices
{
    public enum OpenMode { ForRead, ForWrite }
    public enum AttachmentPoint { TopLeft }
    public readonly struct Handle
    {
        internal long Value { get; }
        public Handle(long value) { Value = value; }
        public override string ToString() => Value.ToString("X");
    }
    public readonly struct ObjectId
    {
        internal long Value { get; }
        internal ObjectId(long value) { Value = value; }
        public static ObjectId Null => default;
        public bool IsNull => Value == 0;
        public Handle Handle => new Handle(Value);
    }
    public readonly struct Extents3d
    {
        public Extents3d(Point3d min, Point3d max) { MinPoint = min; MaxPoint = max; }
        public Point3d MinPoint { get; }
        public Point3d MaxPoint { get; }
    }
    public class DBObject : IDisposable
    {
        internal Database Database { get; set; }
        public ObjectId ObjectId { get; internal set; }
        public ObjectId OwnerId { get; internal set; }
        public Handle Handle => ObjectId.Handle;
        public bool IsErased { get; private set; }
        public void Erase() { IsErased = true; }
        public void Dispose() { }
    }
    public class Entity : DBObject
    {
        public string Layer { get; set; } = "Original";
        public Extents3d GeometricExtents { get; set; } = new Extents3d(new Point3d(0, 0, 0), new Point3d(10, 2, 0));
    }
    public class DBText : Entity
    {
        public string TextString { get; set; } = "Original";
        public double Height { get; set; }
        public Point3d Position { get; set; }
        public double Rotation { get; set; }
        public ObjectId TextStyleId { get; set; }
    }
    public class AttributeReference : DBText { }
    public class MText : Entity
    {
        public string Contents { get; set; } = "Original";
        public double TextHeight { get; set; }
        public Point3d Location { get; set; }
        public double Rotation { get; set; }
        public ObjectId TextStyleId { get; set; }
        public double Width { get; set; }
        public AttachmentPoint Attachment { get; set; }
        internal MText Clone() => new MText { Contents = Contents, TextHeight = TextHeight,
            Location = Location, Rotation = Rotation, TextStyleId = TextStyleId, Width = Width, Layer = Layer };
    }
    public class MLeader : Entity
    {
        private MText text;
        // The real API returns a detached MText value; mutations require reassignment.
        public MText MText { get => text?.Clone(); set => text = value?.Clone(); }
        public double TextHeight { get; set; }
        public ObjectId TextStyleId { get; set; }
        public Point3d GetFirstVertex(int index) => Point3d.Origin;
    }
    public class BlockTableRecord : DBObject
    {
        public ObjectId AppendEntity(Entity entity) => Database.Add(entity, ObjectId);
    }
    public sealed class Database
    {
        private readonly Dictionary<long, DBObject> objects = new Dictionary<long, DBObject>();
        private readonly Dictionary<long, int> lookups = new Dictionary<long, int>();
        private long next;
        public Database()
        {
            TransactionManager = new TransactionManager(this);
            CurrentSpaceId = Add(new BlockTableRecord());
        }
        public ObjectId CurrentSpaceId { get; }
        public TransactionManager TransactionManager { get; }
        internal string FailRepeatedLookupHandle { get; set; }
        internal ObjectId Add(DBObject value, ObjectId owner = default)
        {
            var id = new ObjectId(++next);
            value.ObjectId = id; value.OwnerId = owner; value.Database = this;
            objects.Add(id.Value, value);
            return id;
        }
        public ObjectId GetObjectId(bool create, Handle handle, int xref)
        {
            lookups.TryGetValue(handle.Value, out var count); lookups[handle.Value] = count + 1;
            if (count > 0 && handle.ToString() == FailRepeatedLookupHandle)
                throw new InvalidOperationException("Result readback unavailable after successful write.");
            if (!objects.ContainsKey(handle.Value)) throw new InvalidOperationException("Unknown handle.");
            return new ObjectId(handle.Value);
        }
        internal DBObject Read(ObjectId id) => objects[id.Value];
    }
    public sealed class TransactionManager
    {
        private readonly Database database;
        internal TransactionManager(Database database) { this.database = database; }
        public Transaction StartTransaction() => new Transaction(database);
    }
    public sealed class Transaction : IDisposable
    {
        private readonly Database database;
        private bool completed;
        internal Transaction(Database database) { this.database = database; }
        public DBObject GetObject(ObjectId id, OpenMode mode)
        {
            if (completed) throw new InvalidOperationException("Transaction has completed.");
            return database.Read(id);
        }
        public void AddNewlyCreatedDBObject(DBObject value, bool add) { }
        public void Commit() { completed = true; }
        public void Abort() { completed = true; }
        public void Dispose() { }
    }
}

namespace Bimwright.Dwg.Plugin
{
    using Autodesk.AutoCAD.DatabaseServices;

    // Font normalization is a controlled external prerequisite. The executor and
    // its real FinalTextScalePolicy must perform the final scaling themselves.
    internal static class UnicodeStyleService
    {
        public static double ComputeTargetHeight(Transaction tx, ObjectId source, ObjectId target, double height) => height * 0.9;
        public static void ApplyToObject(Transaction tx, Entity entity, ObjectId style)
        {
            switch (entity)
            {
                case DBText text: text.Height *= 0.9; text.TextStyleId = style; break;
                case MText text: text.TextHeight *= 0.9; text.TextStyleId = style; break;
                case MLeader leader:
                    leader.TextHeight *= 0.9; leader.TextStyleId = style;
                    var inner = leader.MText;
                    inner.TextHeight *= 0.9; inner.TextStyleId = style; leader.MText = inner;
                    break;
            }
        }
    }
    internal static class McpLayerService
    {
        public static string EnsureWhiteLayer(Database database) => "Unicode";
        public static void ApplyToObject(Entity entity, string layer) { if (layer != null) entity.Layer = layer; }
    }
}
