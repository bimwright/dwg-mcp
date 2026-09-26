using System;
using System.IO;

namespace Bimwright.Dwg.Plugin.Views.Toast
{
    public static class ToastImagePolicy
    {
        public const int MaxBytes = 8 * 1024 * 1024;
        public static string CapturesDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bimwright", "Dwg", "captures");

        public static bool IsUnderRoot(string path, string root)
        {
            try
            {
                var directory = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                return Path.GetFullPath(path).StartsWith(directory, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public static bool IsSafeImagePath(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) return false;
                var full = Path.GetFullPath(path);
                if (!IsUnderRoot(full, Path.GetTempPath()) && !IsUnderRoot(full, CapturesDirectory)) return false;
                var ext = Path.GetExtension(full).ToLowerInvariant();
                if (ext != ".png" && ext != ".jpg" && ext != ".jpeg" && ext != ".bmp") return false;
                // Avoid directory links changing an approved local root into an arbitrary location.
                for (var parent = new DirectoryInfo(Path.GetDirectoryName(full)); parent != null; parent = parent.Parent)
                    if ((parent.Attributes & FileAttributes.ReparsePoint) != 0) return false;
                var file = new FileInfo(full);
                return file.Exists && (file.Attributes & FileAttributes.ReparsePoint) == 0 && file.Length <= MaxBytes;
            }
            catch { return false; }
        }
    }
}
