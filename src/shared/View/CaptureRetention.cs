using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Bimwright.Dwg.Plugin
{
    /// <summary>
    /// Bounds the generated-captures folder like rvt-mcp: after a capture is saved there, generated captures older
    /// than <see cref="RetentionDays"/> go, then the oldest until the rest fits <see cref="RetentionBytes"/>.
    /// Host-free so it is unit tested without AutoCAD.
    /// </summary>
    internal static class CaptureRetention
    {
        public const int RetentionDays = 30;
        public const long RetentionBytes = 200L * 1024 * 1024;

        /// <summary>%LOCALAPPDATA%\Bimwright\dwg-mcp\captures, where captures without output_path are written.</summary>
        internal static string DefaultDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bimwright", "dwg-mcp", "captures");

        // The names CaptureViewService.BuildDefaultOutputPath generates.
        private static readonly Regex Generated = new Regex(@"^dwg-capture-\d{8}-\d{6}-\d{3}-[0-9a-f]{32}\.(png|jpg|bmp)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// Only files with a generated name directly in <paramref name="directory"/> are touched, never
        /// <paramref name="keep"/> (the capture just written) or a file the caller named. <c>BIMWRIGHT_KEEP_ALL_LOGS</c>
        /// keeps every capture. Never throws: a capture must not fail because an old one is locked.
        /// </summary>
        internal static int PruneGenerated(string directory, string keep, DateTime utcNow, int days = RetentionDays, long budget = RetentionBytes)
        {
            var deleted = 0;
            if (LogRetentionPolicy.KeepAll) return 0;
            try
            {
                if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return 0;
                var files = new DirectoryInfo(directory).GetFiles("dwg-capture-*")
                    .Where(f => Generated.IsMatch(f.Name) && (f.Attributes & FileAttributes.ReparsePoint) == 0)
                    .OrderBy(f => f.LastWriteTimeUtc).ThenBy(f => f.Name, StringComparer.Ordinal).ToList();
                var total = files.Sum(f => f.Length);
                var cutoff = utcNow.AddDays(-days);
                foreach (var file in files)
                {
                    if (string.Equals(file.FullName, keep, StringComparison.OrdinalIgnoreCase)) continue;
                    if (file.LastWriteTimeUtc >= cutoff && total <= budget) break;
                    try { var length = file.Length; file.Delete(); total -= length; deleted++; }
                    catch { }
                }
            }
            catch { }
            return deleted;
        }
    }
}
