using System.Text.RegularExpressions;

namespace Bimwright.Dwg.Plugin
{
    public static class BakeRedactor
    {
        private static readonly Regex AssignmentSecret = new Regex(
            @"(?i)\b(api[_-]?key|auth[_-]?token|password|secret|token)\b\s*=\s*[""'][^""']+[""']",
            RegexOptions.Compiled);

        public static string RedactSource(string source)
        {
            if (string.IsNullOrEmpty(source))
            {
                return source;
            }

            return SecretMasker.Mask(AssignmentSecret.Replace(source, match =>
            {
                var key = match.Groups[1].Value;
                return key + " = \"<secret>\"";
            }));
        }
    }
}
