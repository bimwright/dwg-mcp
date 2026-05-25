using System;

namespace Bimwright.Dwg.Plugin.Cad
{
    internal static class CadPrimitiveValidation
    {
        internal static bool TryValidateRectangleCorners(
            CadPointInput corner1,
            CadPointInput corner2,
            out string error)
        {
            error = null;
            if (corner1.X == corner2.X)
            {
                error = "corner1 and corner2 must define non-zero rectangle width";
                return false;
            }

            if (corner1.Y == corner2.Y)
            {
                error = "corner1 and corner2 must define non-zero rectangle height";
                return false;
            }

            return true;
        }

        internal static bool TryValidateArcSweepDegrees(
            double startAngle,
            double endAngle,
            out string error)
        {
            error = null;
            if (NormalizeDegrees(endAngle - startAngle) == 0d)
            {
                error = "start_angle and end_angle must define a non-zero sweep";
                return false;
            }

            return true;
        }

        internal static bool TryValidateEllipseRadiusRatio(
            double majorRadius,
            double minorRadius,
            out double radiusRatio,
            out string error)
        {
            radiusRatio = 0d;
            error = null;

            if (minorRadius > majorRadius)
            {
                error = "minor_radius must be less than or equal to major_radius";
                return false;
            }

            radiusRatio = minorRadius / majorRadius;
            if (double.IsNaN(radiusRatio) ||
                double.IsInfinity(radiusRatio) ||
                radiusRatio <= 0d ||
                radiusRatio > 1d)
            {
                error = "minor_radius and major_radius must produce a finite radius ratio greater than 0 and less than or equal to 1";
                return false;
            }

            return true;
        }

        private static double NormalizeDegrees(double angle)
        {
            var normalized = angle % 360d;
            if (normalized < 0d)
            {
                normalized += 360d;
            }

            return normalized;
        }
    }
}
