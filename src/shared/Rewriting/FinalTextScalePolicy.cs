namespace Bimwright.Dwg.Plugin.Rewriting
{
    /// <summary>
    /// Applies one final post-processing scale to text height after the main
    /// sizing logic has already chosen a height. Scale is caller-provided and
    /// clamped into <see cref="MinScale"/>..<see cref="MaxScale"/>.
    /// </summary>
    public static class FinalTextScalePolicy
    {
        public const double DefaultScale = 0.80;
        public const double MinScale = 0.50;
        public const double MaxScale = 0.90;

        /// <summary>
        /// Clamps <paramref name="scale"/> into the allowed range. Non-positive
        /// or NaN values fall back to <see cref="DefaultScale"/>.
        /// </summary>
        public static double Clamp(double scale)
        {
            if (double.IsNaN(scale) || scale <= 0)
            {
                return DefaultScale;
            }

            if (scale < MinScale) return MinScale;
            if (scale > MaxScale) return MaxScale;
            return scale;
        }

        public static double Apply(double height, double scale)
        {
            return height > 0
                ? height * Clamp(scale)
                : height;
        }
    }
}
