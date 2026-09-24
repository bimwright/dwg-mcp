using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Autodesk.AutoCAD.DatabaseServices;

namespace Bimwright.Dwg.Plugin.View
{
    internal sealed class DrawingRevision
    {
        private static readonly ConditionalWeakTable<Database, DrawingRevision> States =
            new ConditionalWeakTable<Database, DrawingRevision>();
        private long revision;
        internal string SessionId { get; } = Guid.NewGuid().ToString("N");
        internal long Revision => Interlocked.Read(ref revision);
        internal static DrawingRevision For(Database db) => States.GetValue(db, Create);

        private static DrawingRevision Create(Database db)
        {
            var state = new DrawingRevision();
            db.ObjectModified += (sender, args) => state.Changed(args.DBObject);
            db.ObjectAppended += (sender, args) => state.Changed(args.DBObject);
            db.ObjectErased += (sender, args) => state.Changed(args.DBObject);
            db.ObjectUnappended += (sender, args) => state.Changed(args.DBObject);
            db.ObjectReappended += (sender, args) => state.Changed(args.DBObject);
            return state;
        }

        private void Changed(DBObject value)
        {
            // Navigation itself updates view state; its full metadata is checked
            // independently. All other observed database-object changes invalidate history.
            if (!(value is ViewportTableRecord) && !(value is Viewport))
                Interlocked.Increment(ref revision);
        }
    }
}
