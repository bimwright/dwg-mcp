using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Bimwright.Dwg.Plugin
{
    public static class ErrorSanitizer
    {
        private static readonly Regex WindowsPath = new Regex(
            @"[A-Za-z]:\\[^\r\n""]+?(?=(?:\s+[A-Za-z_][A-Za-z0-9_]*=)|\r|\n|$)",
            RegexOptions.Compiled);

        private static readonly Regex UncPath = new Regex(
            @"\\\\[^\r\n""]+?(?=(?:\s+[A-Za-z_][A-Za-z0-9_]*=)|\r|\n|$)",
            RegexOptions.Compiled);

        public static string Sanitize(string error)
        {
            if (string.IsNullOrEmpty(error))
            {
                return error;
            }

            var withoutStack = RemoveStackFrames(error);
            var masked = SecretMasker.Mask(withoutStack);
            masked = WindowsPath.Replace(masked, "<path>");
            masked = UncPath.Replace(masked, "<path>");
            return masked;
        }

        private static string RemoveStackFrames(string text)
        {
            var kept = new List<string>();
            var lines = text.Replace("\r\n", "\n").Split('\n');
            foreach (var line in lines)
            {
                if (line.TrimStart().StartsWith("at ", StringComparison.Ordinal))
                {
                    continue;
                }
                kept.Add(line);
            }
            return string.Join("\n", kept);
        }
    }
}
