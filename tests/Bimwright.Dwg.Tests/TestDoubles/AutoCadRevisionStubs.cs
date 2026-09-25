using System;
using System.Runtime.CompilerServices;

namespace Autodesk.AutoCAD.DatabaseServices
{
    public class DBObject { }
    public class Viewport : DBObject { }
    public class ViewportTableRecord : DBObject { }
    public class ObjectEventArgs : EventArgs { public DBObject DBObject { get; set; } }
    public class ObjectErasedEventArgs : ObjectEventArgs { }
    public delegate void ObjectEventHandler(object sender, ObjectEventArgs args);
    public delegate void ObjectErasedEventHandler(object sender, ObjectErasedEventArgs args);

    // Models the observed native behavior: multiple equal Database wrappers share
    // one native event source, but have different managed reference identities.
    public partial class Database
    {
        private sealed class NativeState
        {
            internal ObjectEventHandler Modified, Appended, Unappended, Reappended;
            internal ObjectErasedEventHandler Erased;
        }
        private readonly NativeState state;
        public Database() : this(new NativeState()) { }
        private Database(NativeState state) { this.state = state; }
        public Database NewWrapper() => new Database(state);
        public override bool Equals(object other) => other is Database db && ReferenceEquals(state, db.state);
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(state);
        public event ObjectEventHandler ObjectModified { add => state.Modified += value; remove => state.Modified -= value; }
        public event ObjectEventHandler ObjectAppended { add => state.Appended += value; remove => state.Appended -= value; }
        public event ObjectEventHandler ObjectUnappended { add => state.Unappended += value; remove => state.Unappended -= value; }
        public event ObjectEventHandler ObjectReappended { add => state.Reappended += value; remove => state.Reappended -= value; }
        public event ObjectErasedEventHandler ObjectErased { add => state.Erased += value; remove => state.Erased -= value; }
        public int Subscribers => Count(state.Modified) + Count(state.Appended) + Count(state.Unappended) + Count(state.Reappended) + Count(state.Erased);
        private static int Count(Delegate value) => value?.GetInvocationList().Length ?? 0;
        public void Raise(string name, DBObject value)
        {
            var args = new ObjectEventArgs { DBObject = value };
            switch (name)
            {
                case "modified": state.Modified?.Invoke(this, args); break;
                case "appended": state.Appended?.Invoke(this, args); break;
                case "unappended": state.Unappended?.Invoke(this, args); break;
                case "reappended": state.Reappended?.Invoke(this, args); break;
                case "erased": state.Erased?.Invoke(this, new ObjectErasedEventArgs { DBObject = value }); break;
                default: throw new ArgumentException(name);
            }
        }
    }
}

namespace Autodesk.AutoCAD.ApplicationServices
{
    public class DocumentCollectionEventArgs : EventArgs { public Document Document { get; set; } }
    public delegate void DocumentCollectionEventHandler(object sender, DocumentCollectionEventArgs args);
    public class DocumentCollection
    {
        public event DocumentCollectionEventHandler DocumentToBeDestroyed;
        public int Subscribers => DocumentToBeDestroyed?.GetInvocationList().Length ?? 0;
        public void Destroy(Document document) => DocumentToBeDestroyed?.Invoke(this, new DocumentCollectionEventArgs { Document = document });
    }
    public static class Application
    {
        public static DocumentCollection DocumentManager { get; } = new DocumentCollection();
    }
}
