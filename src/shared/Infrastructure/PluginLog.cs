namespace Bimwright.Dwg.Plugin
{
    /// <summary>
    /// Bounded background diagnostics. Never touches disk on the AutoCAD caller.
    /// </summary>
    internal static class PluginLog
    {
        internal static void Debug(string message)
        {
            try { DiagnosticLog.Write("Plugin", message, PluginTarget.AutoCadYear); }
            catch { } // A diagnostics startup failure must not break AutoCAD callers.
        }
    }
}
