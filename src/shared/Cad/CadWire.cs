using System;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Cad
{
    internal readonly struct CadPointInput
    {
        public CadPointInput(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public double X { get; }
        public double Y { get; }
        public double Z { get; }
    }

    internal static class CadWire
    {
        internal static bool TryParsePoint(JToken token, out CadPointInput point, out string error)
        {
            point = default;
            error = null;

            var obj = token as JObject;
            if (obj == null)
            {
                error = "point must be an object with numeric x and y fields";
                return false;
            }

            if (!TryReadDouble(obj, "x", required: true, fallback: 0d, out var x, out error) ||
                !TryReadDouble(obj, "y", required: true, fallback: 0d, out var y, out error) ||
                !TryReadDouble(obj, "z", required: false, fallback: 0d, out var z, out error))
            {
                return false;
            }

            point = new CadPointInput(x, y, z);
            return true;
        }

        internal static bool TryParseHandleValue(string handle, out long value, out string error)
        {
            value = 0L;
            error = null;

            if (string.IsNullOrEmpty(handle))
            {
                error = "handle must be a non-empty hexadecimal string";
                return false;
            }

            if (handle.Any(char.IsWhiteSpace))
            {
                error = "handle must not contain whitespace";
                return false;
            }

            if (handle[0] == '-' || handle[0] == '+')
            {
                error = "handle must be an unsigned hexadecimal string";
                return false;
            }

            if (handle.Any(c => !Uri.IsHexDigit(c)))
            {
                error = "handle contains invalid hexadecimal characters";
                return false;
            }

            if (!long.TryParse(handle, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value) || value < 0L)
            {
                value = 0L;
                error = "handle is outside the supported range";
                return false;
            }

            return true;
        }

        internal static string[] ReadStringArray(JToken parameters, string fieldName)
        {
            var obj = parameters as JObject;
            var array = obj?[fieldName] as JArray;
            if (array == null)
            {
                return Array.Empty<string>();
            }

            return array
                .Where(item => item != null && item.Type == JTokenType.String)
                .Select(item => item.Value<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();
        }

        internal static bool TryReadAciColor(
            JToken parameters,
            string fieldName,
            int fallback,
            out int colorIndex,
            out string error)
        {
            colorIndex = fallback;
            error = null;

            var obj = parameters as JObject;
            var token = obj?[fieldName];
            if (token == null || token.Type == JTokenType.Null)
            {
                return true;
            }

            if (token.Type != JTokenType.Integer)
            {
                error = $"{fieldName} must be an integer ACI color index between 1 and 256";
                return false;
            }

            long value;
            try
            {
                value = token.Value<long>();
            }
            catch (Exception ex) when (ex is FormatException || ex is InvalidCastException || ex is OverflowException)
            {
                error = $"{fieldName} must be an integer ACI color index between 1 and 256";
                return false;
            }

            if (value < 1L || value > 256L)
            {
                error = $"{fieldName} must be an ACI color index between 1 and 256";
                return false;
            }

            colorIndex = (int)value;
            return true;
        }

        private static bool TryReadDouble(
            JObject obj,
            string fieldName,
            bool required,
            double fallback,
            out double value,
            out string error)
        {
            value = fallback;
            error = null;

            var token = obj[fieldName];
            if (token == null || token.Type == JTokenType.Null)
            {
                if (required)
                {
                    error = $"point missing required numeric field '{fieldName}'";
                    return false;
                }

                return true;
            }

            if (token.Type != JTokenType.Float && token.Type != JTokenType.Integer)
            {
                error = $"point field '{fieldName}' must be numeric";
                return false;
            }

            value = token.Value<double>();
            return true;
        }
    }
}
