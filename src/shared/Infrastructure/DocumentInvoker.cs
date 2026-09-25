using System;
using Autodesk.AutoCAD.ApplicationServices;

namespace Bimwright.Dwg.Plugin
{
    public static class DocumentInvoker
    {
        private static MainThreadExecutor mainThread;

        // Called by the add-in entry point on AutoCAD's main thread, before
        // accepting transport requests. Document locking alone is not UI dispatch.
        internal static void Initialize()
        {
            if (mainThread != null) return;
            mainThread = new MainThreadExecutor();
            Application.Idle += OnIdle;
        }

        internal static void Shutdown()
        {
            Application.Idle -= OnIdle;
            mainThread = null;
        }

        private static void OnIdle(object sender, EventArgs args) => mainThread?.RunPending();

        /// <summary>
        /// Serializes AutoCAD API access, locks the active document, and runs the action.
        /// </summary>
        public static T Invoke<T>(Func<Document, T> action)
        {
            var executor = mainThread ?? throw new InvalidOperationException("AutoCAD main-thread executor is not initialized.");
            return DwgApiExecutor.Invoke(() => executor.Invoke(() =>
            {
                var doc = Application.DocumentManager.MdiActiveDocument
                    ?? throw new InvalidOperationException("no active document");
                using (doc.LockDocument())
                {
                    return action(doc);
                }
            }));
        }
    }
}
