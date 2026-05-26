using System;
using Bimwright.Dwg.Plugin.Cad;

namespace Bimwright.Dwg.Plugin.Dimensions
{
    internal static class DimensionRequestValidator
    {
        private const double Tolerance = 1e-9;

        internal static bool TryValidateTwoPointDimension(
            string commandName,
            CadPointInput start,
            CadPointInput end,
            out string error)
        {
            error = null;

            if (!IsFinite(start) || !IsFinite(end))
            {
                error = commandName + " start and end points must be finite";
                return false;
            }

            if (Distance(start, end) <= Tolerance)
            {
                error = commandName + " start and end points must be different";
                return false;
            }

            return true;
        }

        internal static bool TryValidateRadialTargetType(string entityTypeName, out string error)
        {
            error = null;
            if (string.Equals(entityTypeName, "Circle", StringComparison.Ordinal) ||
                string.Equals(entityTypeName, "Arc", StringComparison.Ordinal))
            {
                return true;
            }

            error = "entity_handle must resolve to a circle or arc";
            return false;
        }

        internal static bool TryValidateRadialDimensionGeometry(
            CadPointInput center,
            double radius,
            CadPointInput dimensionLinePoint,
            out double leaderLength,
            out string error)
        {
            leaderLength = 0d;
            error = null;

            if (!IsFinite(center))
            {
                error = "target center must be finite";
                return false;
            }

            if (double.IsNaN(radius) || double.IsInfinity(radius) || radius <= 0d)
            {
                error = "target radius must be positive and finite";
                return false;
            }

            if (!IsFinite(dimensionLinePoint))
            {
                error = "dimension_line_point must be finite";
                return false;
            }

            var distance = Distance(center, dimensionLinePoint);
            if (distance <= radius + Tolerance)
            {
                error = "dimension_line_point must be outside target radius";
                return false;
            }

            leaderLength = distance - radius;
            return true;
        }

        internal static double AngleRadians(CadPointInput start, CadPointInput end)
            => Math.Atan2(end.Y - start.Y, end.X - start.X);

        internal static double DegreesToRadians(double degrees)
            => degrees * Math.PI / 180d;

        internal static CadPointInput PointOnRadius(
            CadPointInput center,
            CadPointInput through,
            double radius)
        {
            var distance = Distance(center, through);
            if (distance <= Tolerance)
            {
                return center;
            }

            var scale = radius / distance;
            return new CadPointInput(
                center.X + (through.X - center.X) * scale,
                center.Y + (through.Y - center.Y) * scale,
                center.Z + (through.Z - center.Z) * scale);
        }

        internal static CadPointInput OppositePointOnRadius(
            CadPointInput center,
            CadPointInput pointOnRadius)
        {
            return new CadPointInput(
                center.X - (pointOnRadius.X - center.X),
                center.Y - (pointOnRadius.Y - center.Y),
                center.Z - (pointOnRadius.Z - center.Z));
        }

        private static bool IsFinite(CadPointInput point)
            => IsFinite(point.X) && IsFinite(point.Y) && IsFinite(point.Z);

        private static bool IsFinite(double value)
            => !double.IsNaN(value) && !double.IsInfinity(value);

        private static double Distance(CadPointInput a, CadPointInput b)
        {
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var dz = b.Z - a.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }
}
