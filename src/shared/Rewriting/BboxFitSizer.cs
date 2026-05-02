using System;

namespace Bimwright.Dwg.Plugin.Rewriting
{
    /// <summary>
    /// Pure-math sizer for translated MText. Given the original cluster bbox
    /// and the visual-unit count of the translated text, returns the width and
    /// text height that keep the translation inside the original footprint.
    /// </summary>
    public static class BboxFitSizer
    {
        // Matches MText defaults (Latin advance ≈ 0.5 cap-height, 1.2 line spacing).
        private const double CharWidthRatio  = 0.50;
        private const double LineSpacing     = 1.20;
        private const double CapacityFactor  = CharWidthRatio * LineSpacing; // 0.60

        private const double HeightFloorRatio    = 0.40;
        private const double VerticalStackRatio  = 2.50;
        private const double VerticalStackWiden  = 2.00;

        public readonly struct Result
        {
            public Result(double? width, double? textHeight, string layoutHint)
            {
                Width = width;
                TextHeight = textHeight;
                LayoutHint = layoutHint;
            }

            public double? Width { get; }
            public double? TextHeight { get; }
            public string LayoutHint { get; }

            public static Result ScaleOnly() => new Result(null, null, "scale_only");
        }

        public static Result Compute(
            RewriteAction action,
            double origW,
            double origH,
            double origTextHeight,
            double translatedCharCount)
        {
            if (action != RewriteAction.Collapse && action != RewriteAction.RewriteInBlock)
            {
                return Result.ScaleOnly();
            }

            if (origW <= 0 || origH <= 0 || origTextHeight <= 0 || translatedCharCount <= 0)
            {
                return Result.ScaleOnly();
            }

            double wLayout = origW;
            double hLayout = origH;
            string hint = "bbox_fit";

            if (origH / origW > VerticalStackRatio)
            {
                wLayout = Math.Min(origH, origW * VerticalStackWiden);
                hLayout = origH;
                hint = "vertical_stack_reflowed";
            }

            double hIdeal = Math.Sqrt(wLayout * hLayout / (CapacityFactor * translatedCharCount));
            double hNew = Math.Min(hIdeal, origTextHeight);

            double hFloor = origTextHeight * HeightFloorRatio;
            if (hNew < hFloor)
            {
                hNew = hFloor;
                hint = hint + "+height_overflow";
            }

            return new Result(wLayout, hNew, hint);
        }
    }
}
