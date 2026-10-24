using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin
{
    /// <summary>Closed request contract for opt-in spatial/paged entity reads.</summary>
    public sealed class EntityQueryOptions
    {
        public const int DefaultMaxOutputBytes = 16384;
        public static IReadOnlyList<string> AllowedFields { get; } = Array.AsReadOnly(new[] { "handle", "type", "runtime_type", "layer", "color_index", "text", "position", "extents" });
        private static readonly string[] ExtensionKeys = { "bbox", "bbox_mode", "space", "fields", "cursor", "max_output_bytes" };
        public string EntityType { get; private set; }
        public string Layer { get; private set; }
        public int? ColorIndex { get; private set; }
        public int Limit { get; private set; }
        public string[] Fields { get; private set; }
        public string Space { get; private set; }
        public double[] Bbox { get; private set; }
        public string BboxMode { get; private set; }
        public string Cursor { get; private set; }
        public int MaxOutputBytes { get; private set; }
        public string Fingerprint { get; private set; }

        public static bool IsExtended(JToken parameters)
        {
            var data = parameters as JObject;
            return data != null && (data["paged"]?.Type == JTokenType.Boolean && data.Value<bool>("paged")
                || ExtensionKeys.Any(key => data[key] != null && data[key].Type != JTokenType.Null));
        }

        public static bool RequiresDirectCall(string command, JToken parameters)
        {
            if (command == "get_selected_texts") return TextReadOptions.IsExtended(parameters);
            if (command == "get_drawing_profile")
            {
                var space = (parameters as JObject)?["space"];
                return space != null && space.Type != JTokenType.Null
                    && (space.Type != JTokenType.String || !string.Equals(space.Value<string>(), "all", StringComparison.OrdinalIgnoreCase));
            }
            if (command != "query_entities" && command != "count_entities") return false;
            if (IsExtended(parameters)) return true;
            var paged = (parameters as JObject)?["paged"];
            return paged != null && paged.Type != JTokenType.Null && paged.Type != JTokenType.Boolean;
        }

        public static EntityQueryOptions Create(JToken parameters)
        {
            var data = parameters as JObject ?? throw new ArgumentException("query parameters must be an object");
            if (data["include_geometry"] != null && data["include_geometry"].Type != JTokenType.Null
                && (data["include_geometry"].Type != JTokenType.Boolean || data.Value<bool>("include_geometry")))
                throw new ArgumentException("Paged queries use the fields allowlist; include_geometry=true is available only on the legacy query path.");
            if (data["paged"] != null && data["paged"].Type != JTokenType.Null && data["paged"].Type != JTokenType.Boolean)
                throw new ArgumentException("paged must be a boolean");
            var result = new EntityQueryOptions
            {
                EntityType = NormalizeType(OptionalString(data, "entity_type")), Layer = Normalize(OptionalString(data, "layer")),
                Limit = (int)Math.Max(1L, Math.Min(5000L, Integer(data, "limit") ?? 500)),
                Space = OptionalString(data, "space") ?? "model", BboxMode = OptionalString(data, "bbox_mode") ?? "intersects",
                Cursor = OptionalString(data, "cursor"), MaxOutputBytes = CheckedInt(Integer(data, "max_output_bytes") ?? DefaultMaxOutputBytes, "max_output_bytes")
            };
            if (result.MaxOutputBytes < 4096 || result.MaxOutputBytes > 65536)
                throw new ArgumentException("max_output_bytes must be between 4096 and 65536");
            var color = Integer(data, "color_index");
            if (color.HasValue && (color < 1 || color > 256)) throw new ArgumentException("color_index must be between 1 and 256");
            result.ColorIndex = color.HasValue ? (int?)color.Value : null;
            if (new[] { result.EntityType, result.Layer, result.Space }.Any(value => value != null && value.Length > 1024))
                throw new ArgumentException("Paged entity_type, layer and space filters are limited to 1024 characters each");
            if (result.Space.Equals("model", StringComparison.OrdinalIgnoreCase)) result.Space = "model";
            else if (result.Space.StartsWith("layout:", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(result.Space.Substring(7)))
                result.Space = "layout:" + result.Space.Substring(7);
            else throw new ArgumentException("space must be model or layout:<exact layout name>; inspect dwg_get_drawing_profile for layout names");
            if (result.Cursor != null && (string.IsNullOrWhiteSpace(result.Cursor) || result.Cursor.Length > 1024))
                throw new ArgumentException("CURSOR_INVALID: cursor must be nonempty and at most 1024 characters");
            if (result.BboxMode != "intersects" && result.BboxMode != "contains")
                throw new ArgumentException("bbox_mode must be intersects or contains");
            if (data["bbox"] != null && data["bbox"].Type != JTokenType.Null)
            {
                var box = data["bbox"] as JObject ?? throw new ArgumentException("bbox must be {min:[x,y,z],max:[x,y,z]}");
                if (box.Properties().Any(p => p.Name != "min" && p.Name != "max")) throw new ArgumentException("bbox accepts only min and max");
                result.Bbox = Coordinates(box["min"], "min").Concat(Coordinates(box["max"], "max")).ToArray();
                for (var i = 0; i < 3; i++) if (result.Bbox[i] > result.Bbox[i + 3]) throw new ArgumentException("bbox min must be <= max on each WCS axis");
            }
            else if (data["bbox_mode"] != null && data["bbox_mode"].Type != JTokenType.Null)
                throw new ArgumentException("bbox_mode requires bbox");
            var requested = data["fields"] == null || data["fields"].Type == JTokenType.Null
                ? new[] { "handle", "type", "layer" }
                : ReadFields(data["fields"]);
            result.Fields = AllowedFields.Where(field => field == "handle" || requested.Contains(field)).ToArray();
            var canonical = new JObject
            {
                ["entity_type"] = result.EntityType?.ToUpperInvariant(), ["layer"] = result.Layer?.ToUpperInvariant(),
                ["color_index"] = result.ColorIndex, ["space"] = result.Space.ToUpperInvariant(),
                ["bbox"] = result.Bbox == null ? JValue.CreateNull() : (JToken)new JArray(result.Bbox),
                ["bbox_mode"] = result.Bbox == null ? null : result.BboxMode, ["fields"] = new JArray(result.Fields),
                ["order"] = "numeric_handle_ascending"
            };
            using (var hash = SHA256.Create()) result.Fingerprint = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(canonical.ToString(Formatting.None)))).Replace("-", "").ToLowerInvariant();
            return result;
        }

        public static string NormalizeType(string value)
        {
            var normalized = Normalize(value);
            if (normalized == "POLYLINE")
                throw new ArgumentException("AMBIGUOUS_ENTITY_TYPE: uppercase POLYLINE is an ambiguous DXF token. Use LWPOLYLINE or Polyline for lightweight polylines; use Polyline2d, Polyline3d or an explicit qualified managed name for other types. No mesh or polyline family is inferred.");
            if (string.Equals(normalized, "TEXT", StringComparison.OrdinalIgnoreCase)) return "DBText";
            if (string.Equals(normalized, "INSERT", StringComparison.OrdinalIgnoreCase)) return "BlockReference";
            if (string.Equals(normalized, "LWPOLYLINE", StringComparison.OrdinalIgnoreCase)) return "Polyline";
            return normalized;
        }

        public static bool TypeMatches(Type actual, string filter)
        {
            filter = NormalizeType(filter);
            return filter == null || string.Equals(actual.Name, filter, StringComparison.OrdinalIgnoreCase)
                || string.Equals(actual.FullName, filter, StringComparison.OrdinalIgnoreCase);
        }

        private static string[] ReadFields(JToken token)
        {
            var array = token as JArray ?? throw new ArgumentException("fields must be a JSON array of allowlisted field names");
            if (array.Count == 0 || array.Count > AllowedFields.Count || array.Any(t => t.Type != JTokenType.String))
                throw new ArgumentException("fields must contain 1..8 allowlisted names");
            var fields = array.Values<string>().ToArray();
            if (fields.Any(field => !AllowedFields.Contains(field)) || fields.Distinct().Count() != fields.Length)
                throw new ArgumentException("fields must be unique and case-sensitive: " + string.Join(", ", AllowedFields));
            return fields;
        }

        private static double[] Coordinates(JToken value, string name)
        {
            var array = value as JArray;
            if (array == null || array.Count != 3 || array.Any(t => t.Type != JTokenType.Integer && t.Type != JTokenType.Float))
                throw new ArgumentException("bbox " + name + " must contain three finite drawing-unit numbers");
            var numbers = array.Values<double>().ToArray();
            if (numbers.Any(n => double.IsNaN(n) || double.IsInfinity(n))) throw new ArgumentException("bbox coordinates must be finite");
            return numbers;
        }

        private static string OptionalString(JObject data, string key)
        {
            var value = data[key];
            if (value == null || value.Type == JTokenType.Null) return null;
            if (value.Type != JTokenType.String) throw new ArgumentException(key + " must be a string");
            return value.Value<string>();
        }
        private static string Normalize(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        private static long? Integer(JObject data, string key)
        {
            var value = data[key];
            if (value == null || value.Type == JTokenType.Null) return null;
            if (value.Type != JTokenType.Integer || !long.TryParse(value.ToString(Formatting.None), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                throw new ArgumentException(key + " must be an integer within Int64 range");
            return parsed;
        }
        private static int CheckedInt(long value, string key)
        {
            if (value < int.MinValue || value > int.MaxValue) throw new ArgumentException(key + " must be within Int32 range");
            return (int)value;
        }
    }
}
