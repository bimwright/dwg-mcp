using System;
using System.IO;

namespace Bimwright.Dwg.Plugin
{
    internal static class UnicodeScaleHeuristics
    {
        internal const double ShxHeightScale = 0.60;
        internal const double TrueTypeHeightScale = 0.90;
        internal const double UnknownHeightScale = 0.82;

        internal static double DetermineScaleFactor(string styleName, string fileName, string typeFace)
        {
            if (MatchesUnicodeStyle(styleName, fileName, typeFace))
            {
                return 1.0;
            }

            var extension = Path.GetExtension(fileName ?? string.Empty);
            if (string.Equals(extension, ".shx", StringComparison.OrdinalIgnoreCase))
            {
                return ShxHeightScale;
            }

            if (string.Equals(extension, ".ttf", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".otf", StringComparison.OrdinalIgnoreCase))
            {
                return TrueTypeHeightScale;
            }

            if (!string.IsNullOrWhiteSpace(typeFace))
            {
                return TrueTypeHeightScale;
            }

            return UnknownHeightScale;
        }

        private static bool MatchesUnicodeStyle(string styleName, string fileName, string typeFace)
        {
            return string.Equals(styleName, UnicodeStyleService.StyleName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(typeFace, UnicodeStyleService.FontTypeFace, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFileName(fileName ?? string.Empty), UnicodeStyleService.FontFileName, StringComparison.OrdinalIgnoreCase);
        }
    }
}
