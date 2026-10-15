using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin
{
    /// <summary>Host-free request rules shared by the gateway and session handlers.</summary>
    public static class DrawingSessionContract
    {
        public const int OpenTimeoutSeconds = 600;
        public const string NoActiveDocument = "no active document: AutoCAD is running with no drawing open. " +
            "Call dwg_list_recent_drawings, ask the user which file to open, then call dwg_open_drawing with its absolute path.";
        public const string AskUser = "Ask the user which path to open, then call dwg_open_drawing. Do not open a file before the user chooses.";
        public const string AskForPath = "No matching recent DWG files were found. Ask the user for an absolute path on the AutoCAD machine, then call dwg_open_drawing. Do not guess a path.";

        public static void ValidateTimeout(int seconds)
        {
            if (seconds < 1 || seconds > 900)
                throw new ArgumentException("timeout_seconds must be between 1 and 900 (default 600).");
        }

        public static void ValidateMaxItems(int maxItems)
        {
            if (maxItems < 1 || maxItems > 50)
                throw new ArgumentException("max_items must be between 1 and 50 (default 50).");
        }

        public static int ReadTimeoutSeconds(string command, JToken parameters)
        {
            if (!string.Equals(command, "open_drawing", StringComparison.Ordinal)) return 30;
            var value = (parameters as JObject)?["timeout_seconds"];
            if (value != null && value.Type != JTokenType.Null && value.Type != JTokenType.Integer)
                throw new ArgumentException("timeout_seconds must be an integer between 1 and 900.");
            var seconds = value == null || value.Type == JTokenType.Null ? OpenTimeoutSeconds : value.Value<int>();
            ValidateTimeout(seconds);
            return seconds;
        }

        public static string NormalizeDwgPath(string path)
        {
            path = (path ?? string.Empty).Trim().Trim('"');
            // Path.IsPathRooted alone accepts C:relative.dwg and \root-relative.dwg.
            if (!IsAbsolutePath(path))
                throw new ArgumentException("path must be an absolute drive or UNC path to a .dwg on the machine AutoCAD runs on, not on the MCP client.");
            path = Path.GetFullPath(path);
            if (!string.Equals(Path.GetExtension(path), ".dwg", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Expected a .dwg drawing. Templates, DXF, URLs and cloud identifiers are not supported by dwg_open_drawing.");
            return path;
        }

        public static bool IsAbsolutePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            // Device paths are intentionally unsupported; accept ordinary drive/UNC paths only.
            if (path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal)) return false;
            if (path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/')) return true;
            if (!path.StartsWith(@"\\", StringComparison.Ordinal)) return false;
            var parts = path.Substring(2).Split('\\');
            return parts.Length >= 3 && !string.IsNullOrWhiteSpace(parts[0]) && !string.IsNullOrWhiteSpace(parts[1]);
        }
    }
}
