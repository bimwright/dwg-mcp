using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

namespace Bimwright.Dwg.Plugin.View
{
    internal sealed class DrawingRevision
    {
        private static readonly object Gate = new object();
        private static ConditionalWeakTable<Document, DrawingRevision> states =
            new ConditionalWeakTable<Document, DrawingRevision>();
        private static readonly HashSet<DrawingRevision> LiveStates = new HashSet<DrawingRevision>();
        private static DocumentCollection documents;
        private Database database;
        private long revision;
        internal string SessionId { get; } = Guid.NewGuid().ToString("N");
        internal long Revision => Interlocked.Read(ref revision);
        internal static DrawingRevision For(Document document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            lock (Gate)
            {
                if (documents == null)
                {
                    documents = Application.DocumentManager;
                    documents.DocumentToBeDestroyed += OnDocumentToBeDestroyed;
                }
                return states.GetValue(document, Create);
            }
        }

        private static DrawingRevision Create(Document document)
        {
            // Document.Database can return a fresh managed wrapper on every read.
            // Key by the document lifetime and retain the wrapper owning our handlers.
            var state = new DrawingRevision { database = document.Database };
            state.database.ObjectModified += state.OnObjectChanged;
            state.database.ObjectAppended += state.OnObjectChanged;
            state.database.ObjectErased += state.OnObjectErased;
            state.database.ObjectUnappended += state.OnObjectChanged;
            state.database.ObjectReappended += state.OnObjectChanged;
            LiveStates.Add(state);
            return state;
        }

        private static void OnDocumentToBeDestroyed(object sender, DocumentCollectionEventArgs args)
        {
            lock (Gate)
            {
                if (!states.TryGetValue(args.Document, out var state)) return;
                state.Detach();
                states.Remove(args.Document);
                LiveStates.Remove(state);
            }
        }

        internal static void Shutdown()
        {
            lock (Gate)
            {
                foreach (var state in LiveStates) state.Detach();
                LiveStates.Clear();
                states = new ConditionalWeakTable<Document, DrawingRevision>();
                if (documents != null)
                    documents.DocumentToBeDestroyed -= OnDocumentToBeDestroyed;
                documents = null;
            }
        }

        private void Detach()
        {
            database.ObjectModified -= OnObjectChanged;
            database.ObjectAppended -= OnObjectChanged;
            database.ObjectErased -= OnObjectErased;
            database.ObjectUnappended -= OnObjectChanged;
            database.ObjectReappended -= OnObjectChanged;
            // AutoCAD owns this database; do not dispose it.
            database = null;
        }

        private void OnObjectChanged(object sender, ObjectEventArgs args) => Changed(args.DBObject);
        private void OnObjectErased(object sender, ObjectErasedEventArgs args) => Changed(args.DBObject);

        private void Changed(DBObject value)
        {
            // Navigation itself updates view state; its full metadata is checked
            // independently. All other observed database-object changes invalidate history.
            if (!(value is ViewportTableRecord) && !(value is Viewport))
                Interlocked.Increment(ref revision);
        }
    }
}
