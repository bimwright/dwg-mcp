using System;

namespace Bimwright.Dwg.Plugin.View
{
    /// <summary>
    /// Pure helpers for sizing a captured view image. No AutoCAD dependency so it can be unit tested.
    /// </summary>
    internal static class CaptureViewMath
    {
        public const int MinPixelSize = 64;
        public const int MaxPixelSize = 8192;

        /// <summary>
        /// Computes an output bitmap size whose longer dimension equals the (clamped) pixel size,
        /// preserving the aspect ratio of the on-screen display. Falls back to a square when the
        /// display size is unavailable. Never returns a zero dimension.
        /// </summary>
        public static (int width, int height) ComputeOutputSize(int displayWidth, int displayHeight, int pixelSize)
        {
            int p = Math.Max(MinPixelSize, Math.Min(MaxPixelSize, pixelSize));

            if (displayWidth <= 0 || displayHeight <= 0)
            {
                return (p, p);
            }

            if (displayWidth >= displayHeight)
            {
                int width = p;
                int height = (int)Math.Round(p * (double)displayHeight / displayWidth);
                return (width, Math.Max(1, height));
            }
            else
            {
                int height = p;
                int width = (int)Math.Round(p * (double)displayWidth / displayHeight);
                return (Math.Max(1, width), height);
            }
        }
    }
}
