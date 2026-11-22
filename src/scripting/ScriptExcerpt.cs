using System;
using System.Collections.Generic;
using System.Text;

namespace Bimwright.Dwg.Scripting
{
    /// <summary>Source lines around a diagnostic: the error line ±2, a column marker, at most 200 chars per line.</summary>
    public static class ScriptExcerpt
    {
        public const int Context = 2;
        public const int MaxLineChars = 200;

        public static string[] SplitLines(string code)
        {
            var lines = (code ?? "").Split('\n');
            for (var i = 0; i < lines.Length; i++) lines[i] = lines[i].TrimEnd('\r');
            return lines;
        }

        /// <summary>Returns null when the 1-based line is outside the source.</summary>
        public static Dictionary<string, object>[] Build(string[] lines, int line, int column)
        {
            if (lines == null || line < 1 || line > lines.Length) return null;
            var first = Math.Max(1, line - Context);
            var last = Math.Min(lines.Length, line + Context);
            var index = Math.Max(0, column - 1);
            // One window for every excerpt line so columns stay aligned and the marker stays visible.
            var longest = 0;
            for (var n = first; n <= last; n++) longest = Math.Max(longest, lines[n - 1].Length);
            var offset = longest <= MaxLineChars ? 0 : Math.Max(0, Math.Min(index - MaxLineChars / 2, longest - MaxLineChars));
            var result = new List<Dictionary<string, object>>();
            for (var n = first; n <= last; n++)
            {
                var text = lines[n - 1];
                var visible = offset >= text.Length ? "" : text.Substring(offset, Math.Min(MaxLineChars, text.Length - offset));
                var row = new Dictionary<string, object> { ["line"] = n, ["text"] = visible };
                if (offset > 0 || text.Length > offset + MaxLineChars) row["truncated"] = true;
                if (n == line) row["marker"] = Marker(visible, index - offset);
                result.Add(row);
            }
            return result.ToArray();
        }

        private static string Marker(string visible, int position)
        {
            position = Math.Max(0, Math.Min(position, MaxLineChars - 1));
            var marker = new StringBuilder(position + 1);
            // Copy tabs so the caret lines up under the column in any tab width.
            for (var i = 0; i < position; i++) marker.Append(i < visible.Length && visible[i] == '\t' ? '\t' : ' ');
            return marker.Append('^').ToString();
        }
    }
}
