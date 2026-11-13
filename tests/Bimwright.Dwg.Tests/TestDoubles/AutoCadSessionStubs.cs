// Explicit host doubles for the actual session handlers and DocumentInvoker.
// No Autodesk assemblies, host UI, drawing mutation, or registry writes.
using System;
using System.Collections;
using System.Collections.Generic;

namespace Autodesk.AutoCAD.ApplicationServices
{
    public partial class Document
    {
        public string Name { get; set; }
        public bool IsReadOnly { get; set; }
        public bool IsNamedDrawing { get; set; }
        public object AcadDocument { get; set; }
        public string CommandInProgress { get; set; } = "";
        public int LockCount { get; private set; }
        public int ActiveLockCount { get; private set; }
        public IDisposable LockDocument() { LockCount++; ActiveLockCount++; return new TestLock(this); }
        private sealed class TestLock : IDisposable
        {
            private readonly Document owner;
            public TestLock(Document owner) { this.owner = owner; }
            public void Dispose() { owner.ActiveLockCount--; }
        }
    }

    public static class DocumentExtension
    {
        public static object GetAcadDocument(this Document document) => document.AcadDocument;
    }

    public partial class DocumentCollection : IEnumerable
    {
        public List<Document> Documents { get; } = new List<Document>();
        public Document MdiActiveDocument { get; set; }
        public bool IsApplicationContext { get; set; } = true;
        public int OpenCalls { get; set; }
        public Func<string, bool, Document> OnOpen { get; set; }
        public IEnumerator GetEnumerator() => Documents.GetEnumerator();
    }

    public static class DocumentCollectionExtension
    {
        public static Document Open(this DocumentCollection collection, string path, bool forReadOnly)
        {
            collection.OpenCalls++;
            var opened = collection.OnOpen == null ? new Document { Name = path, IsReadOnly = forReadOnly } : collection.OnOpen(path, forReadOnly);
            if (opened != null) { collection.Documents.Add(opened); collection.MdiActiveDocument = opened; }
            return opened;
        }
    }

    public static partial class Application
    {
        public static int Sdi { get; set; }
        public static object GetSystemVariable(string name) => name == "SDI" ? Sdi : throw new NotSupportedException(name);
        public static event EventHandler Idle;
        public static void RaiseIdle() => Idle?.Invoke(null, EventArgs.Empty);
    }
}

namespace Autodesk.AutoCAD.DatabaseServices
{
    public partial class Database
    {
        public string Filename { get; set; }
    }

    public class HostApplicationServices
    {
        public static HostApplicationServices Current { get; set; } = new HostApplicationServices();
        public string UserRegistryProductRootKey { get; set; } = @"Software\Bimwright\NonexistentSessionTestRoot";
    }
}
