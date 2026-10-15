using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Bimwright.Dwg.Plugin
{
    public sealed class RecentDrawingEntry
    {
        public int Index { get; internal set; }
        public string Path { get; internal set; }
        public bool Pinned { get; internal set; }
    }

    /// <summary>Parse the observed AutoCAD Recent File List FileN/FilePinnedN layout.
    /// The registry layout is not a managed API contract; missing keys are reported, never invented.</summary>
    public static class RecentDrawingList
    {
        public static RecentDrawingEntry[] Parse(IEnumerable<KeyValuePair<string, object>> values, string name = null, int maxItems = 50)
        {
            DrawingSessionContract.ValidateMaxItems(maxItems);
            var entries = (values ?? Enumerable.Empty<KeyValuePair<string, object>>())
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            var files = new List<RecentDrawingEntry>();
            foreach (var pair in entries)
            {
                if (!pair.Key.StartsWith("File", StringComparison.OrdinalIgnoreCase) ||
                    !int.TryParse(pair.Key.Substring(4), NumberStyles.None, CultureInfo.InvariantCulture, out var index) || index < 1 ||
                    !(pair.Value is string path) || string.IsNullOrWhiteSpace(path)) continue;
                try { path = DrawingSessionContract.NormalizeDwgPath(path); }
                catch (ArgumentException) { continue; }
                catch (NotSupportedException) { continue; }
                catch (PathTooLongException) { continue; }
                if (!string.IsNullOrWhiteSpace(name) && Path.GetFileName(path).IndexOf(name.Trim(), StringComparison.OrdinalIgnoreCase) < 0) continue;
                entries.TryGetValue("FilePinned" + index, out var pinned);
                files.Add(new RecentDrawingEntry { Index = index, Path = path, Pinned = Convert.ToString(pinned, CultureInfo.InvariantCulture) == "1" });
            }
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return files.OrderBy(file => file.Index).Where(file => seen.Add(file.Path)).Take(maxItems).ToArray();
        }
    }
}
