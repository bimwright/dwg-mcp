// Test-only host types for compiling the real script handlers. No Autodesk DLLs,
// native calls, document edits, or live MCP connections are used by these doubles.
using System;

namespace Autodesk.AutoCAD.ApplicationServices
{
    public class Document
    {
        public Autodesk.AutoCAD.DatabaseServices.Database Database = new Autodesk.AutoCAD.DatabaseServices.Database();
        public Autodesk.AutoCAD.EditorInput.Editor Editor = new Autodesk.AutoCAD.EditorInput.Editor();
        public int NativeGetterReads;
        public int MutationCount;
        public Action<string> OnSendString;

        public string NativeProperty
        {
            get
            {
                NativeGetterReads++;
                throw new InvalidOperationException("Host getters must not be traversed by JSON serialization.");
            }
        }

        public void SendStringToExecute(string input, bool activate, bool wrapUpInactiveDoc, bool echoCommand)
            => OnSendString(input);
    }
}
namespace Autodesk.AutoCAD.DatabaseServices { public class Database { } }
namespace Autodesk.AutoCAD.EditorInput { public class Editor { } }
namespace Autodesk.AutoCAD.Geometry { public struct Point3d { } }
