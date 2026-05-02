namespace Bimwright.Dwg.Plugin.Rewriting
{
    /// <summary>
    /// Shared text measurement helpers. Pure functions — no AutoCAD deps.
    /// </summary>
    public static class TextMetrics
    {
        /// <summary>
        /// Approximate visual width of <paramref name="text"/> in "em units"
        /// (1 em ≈ 1 CJK glyph width). Used by sizing heuristics that need a
        /// rough character-width estimate without touching a font metrics API.
        /// </summary>
        public static double CountVisualUnits(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0;
            }

            double total = 0;
            foreach (char c in text)
            {
                total += GetGlyphWeight(c);
            }
            return total;
        }

        private static double GetGlyphWeight(char c)
        {
            if (char.IsWhiteSpace(c))
            {
                return 0.28;
            }

            if (IsWideGlyph(c))
            {
                return 1.0;
            }

            if (char.IsDigit(c))
            {
                return 0.62;
            }

            if (char.IsPunctuation(c) || char.IsSymbol(c))
            {
                return 0.35;
            }

            if (char.IsUpper(c))
            {
                return 0.62;
            }

            if (char.IsLetter(c))
            {
                return 0.56;
            }

            return 0.56;
        }

        private static bool IsWideGlyph(char c)
        {
            return
                (c >= '⺀' && c <= '鿿') ||
                (c >= '豈' && c <= '﫿') ||
                (c >= '぀' && c <= 'ヿ') ||
                (c >= '가' && c <= '힯');
        }
    }
}
