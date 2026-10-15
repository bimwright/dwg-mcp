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

        /// <summary>Health-only main-thread observation; no document lookup requirement or document lock.</summary>
        internal static T InvokeHostQuery<T>(Func<T> action)
        {
            var executor = mainThread ?? throw new InvalidOperationException("AutoCAD main-thread executor is not initialized.");
            return executor.Invoke(action);
        }

        /// <summary>Standalone session operations share the CAD queue, but do not require or lock a document.</summary>
        internal static T InvokeSession<T>(Func<T> action, Func<bool> isAccepting = null)
        {
            var executor = mainThread ?? throw new InvalidOperationException("AutoCAD main-thread executor is not initialized.");
            return DwgApiExecutor.Invoke(() => executor.Invoke(() =>
            {
                EnsureAccepting(isAccepting);
                return action();
            }));
        }

        /// <summary>
        /// Serializes AutoCAD API access, locks the active document, and runs the action.
        /// </summary>
        public static T Invoke<T>(Func<Document, T> action, Func<bool> isAccepting = null)
        {
            var executor = mainThread ?? throw new InvalidOperationException("AutoCAD main-thread executor is not initialized.");
            return DwgApiExecutor.Invoke(() => executor.Invoke(() =>
            {
                EnsureAccepting(isAccepting);
                var doc = Application.DocumentManager.MdiActiveDocument
                    ?? throw new InvalidOperationException(DrawingSessionContract.NoActiveDocument);
                using (doc.LockDocument())
                {
                    return action(doc);
                }
            }));
        }

        private static void EnsureAccepting(Func<bool> isAccepting)
        {
            if (isAccepting != null && !isAccepting())
                throw new InvalidOperationException("DWG listener stopped; command was not started.");
        }
    }
}
