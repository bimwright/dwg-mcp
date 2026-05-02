using System;
using Autodesk.AutoCAD.ApplicationServices;

namespace Bimwright.Dwg.Plugin
{
    public static class DocumentInvoker
    {
        /// <summary>
        /// Locks the active document and runs the action.
        /// Can be called from any thread (AutoCAD .NET API allows cross-thread LockDocument).
        /// </summary>
        public static T Invoke<T>(Func<Document, T> action)
        {
            var doc = Application.DocumentManager.MdiActiveDocument
                ?? throw new InvalidOperationException("no active document");
            using (doc.LockDocument())
            {
                return action(doc);
            }
        }
    }
}
