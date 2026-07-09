using System;
using System.IO;
using System.Reflection;

namespace Bimwright.Dwg.Plugin.Export
{
    public static class ExportPathPolicy
    {
        public static string ValidateAndNormalize(
            string outputPath,
            string expectedExtension,
            bool overwriteExisting,
            bool allowRepoOutput,
            out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(outputPath))
            {
                error = "output path is required";
                return null;
            }

            if (!IsPathFullyQualified(outputPath))
            {
                error = "output path must be a fully qualified absolute path";
                return null;
            }

            string normalized;
            try
            {
                normalized = Path.GetFullPath(outputPath);
            }
            catch (Exception ex)
            {
                error = "invalid path: " + ex.Message;
                return null;
            }

            string ext = Path.GetExtension(normalized);
            if (expectedExtension.Equals(".image", StringComparison.OrdinalIgnoreCase))
            {
                if (!ext.Equals(".png", StringComparison.OrdinalIgnoreCase) &&
                    !ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) &&
                    !ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) &&
                    !ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase))
                {
                    error = "extension must be .png, .jpg, .jpeg, or .bmp";
                    return null;
                }
            }
            else
            {
                if (!ext.Equals(expectedExtension, StringComparison.OrdinalIgnoreCase))
                {
                    error = $"extension must be {expectedExtension}";
                    return null;
                }
            }

            if (File.Exists(normalized) && !overwriteExisting)
            {
                error = "file already exists and overwrite_existing is false";
                return null;
            }

            string dir = Path.GetDirectoryName(normalized);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                error = "target directory does not exist";
                return null;
            }

            if (!allowRepoOutput)
            {
                string repoRoot = FindRepositoryRoot();
                if (!string.IsNullOrEmpty(repoRoot))
                {
                    string fullRoot = Path.GetFullPath(repoRoot);
                    if (normalized.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        error = "writing to repository root is rejected; set allow_repo_output to true to override";
                        return null;
                    }
                }
            }

            return normalized;
        }

        private static string FindRepositoryRoot()
        {
            var dirs = new[]
            {
                AppDomain.CurrentDomain.BaseDirectory,
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            };

            foreach (var baseDir in dirs)
            {
                if (string.IsNullOrEmpty(baseDir)) continue;

                string current = baseDir;
                while (!string.IsNullOrEmpty(current))
                {
                    if (Directory.Exists(Path.Combine(current, ".git")) ||
                        File.Exists(Path.Combine(current, "Bimwright.Dwg.sln")))
                    {
                        return current;
                    }
                    string parent = Path.GetDirectoryName(current);
                    if (parent == current) break;
                    current = parent;
                }
            }

            return null;
        }

        private static bool IsPathFullyQualified(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            // Windows UNC
            if (path.StartsWith("\\\\") || path.StartsWith("//")) return true;

            // Windows drive-rooted: C:\ or C:/
            if (path.Length >= 3 &&
                char.IsLetter(path[0]) &&
                path[1] == ':' &&
                (path[2] == '\\' || path[2] == '/'))
            {
                return true;
            }

            // Unix absolute (CI Linux runners use /tmp/... for Path.GetTempPath())
            if (path[0] == '/') return true;

            return false;
        }
    }
}
