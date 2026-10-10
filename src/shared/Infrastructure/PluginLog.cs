using System;
using System.IO;

namespace Bimwright.Dwg.Plugin
{
    /// <summary>
    /// Timestamped append-only debug log under %LOCALAPPDATA%\Bimwright\dwg-mcp\debug.log.
    /// Never throws — logging must not break the plugin.
    /// </summary>
    internal static class PluginLog
    {
        internal static void Debug(string message)
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Bimwright", "dwg-mcp");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "debug.log"),
                    $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
            catch { }
        }
    }
}
