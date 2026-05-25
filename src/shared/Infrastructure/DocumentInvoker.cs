using System;
using Autodesk.AutoCAD.ApplicationServices;

namespace Bimwright.Dwg.Plugin
{
    public static class DocumentInvoker
    {
        /// <summary>
        /// Serializes AutoCAD API access, locks the active document, and runs the action.
        /// </summary>
        public static T Invoke<T>(Func<Document, T> action)
        {
            return DwgApiExecutor.Invoke(() =>
            {
                var doc = Application.DocumentManager.MdiActiveDocument
                    ?? throw new InvalidOperationException("no active document");
                using (doc.LockDocument())
                {
                    return action(doc);
                }
            });
        }
    }
}
