using System;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Cad
{
    internal static class CadTransformService
    {
        internal static bool TryReadScale(double factor, out double value, out string error)
        {
            value = 0d;
            error = null;

            if (factor <= 0d || factor > 1000d || double.IsNaN(factor) || double.IsInfinity(factor))
            {
                error = "scale must be a finite positive number less than or equal to 1000";
                return false;
            }

            value = factor;
            return true;
        }

        internal static double DegreesToRadians(double degrees)
            => degrees * Math.PI / 180d;

        internal static bool TryParseVector(JToken token, out CadPointInput vector, out string error)
        {
            if (CadWire.TryParsePoint(token, out vector, out error))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(error) &&
                error.StartsWith("point", StringComparison.Ordinal))
            {
                error = "vector" + error.Substring("point".Length);
            }
            else
            {
                error = "vector " + error;
            }

            return false;
        }
    }
}
