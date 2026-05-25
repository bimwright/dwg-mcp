using System.Text.RegularExpressions;

namespace Bimwright.Dwg.Plugin
{
    public static class SecretMasker
    {
        private static readonly Regex KeyValueSecret = new Regex(
            @"(?i)\b(auth[_-]?token|api[_-]?key|password|secret|token)\b\s*[:=]\s*['""]?[^'""\s,;}]+",
            RegexOptions.Compiled);

        private static readonly Regex BearerSecret = new Regex(
            @"(?i)\bBearer\s+[A-Za-z0-9._\-]+",
            RegexOptions.Compiled);

        public static string Mask(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            text = KeyValueSecret.Replace(text, match =>
            {
                var key = match.Groups[1].Value;
                return key + "=<secret>";
            });
            return BearerSecret.Replace(text, "Bearer <secret>");
        }
    }
}
