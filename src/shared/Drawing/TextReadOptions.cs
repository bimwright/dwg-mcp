using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin
{
    /// <summary>Opt-in read-only source contract; the legacy pickfirst path is separate.</summary>
    public sealed class TextReadOptions
    {
        public const int MaxTextRecords = 5000;
        public const int MaxTextUtf8Bytes = 8 * 1024 * 1024;
        private static readonly string[] ExtendedKeys = { "handles", "bbox", "bbox_mode", "space", "cursor", "max_output_bytes" };
        public EntityQueryOptions Query { get; private set; }
        public string[] Handles { get; private set; }
        public string GroupingStrength { get; private set; }
        public bool IncludeEntities { get; private set; }
        public string Cursor => Query.Cursor;
        public int MaxOutputBytes => Query.MaxOutputBytes;
        public string Fingerprint { get; private set; }

        public static bool IsExtended(JToken parameters)
        {
            var data = parameters as JObject;
            return data != null && ExtendedKeys.Any(key => data[key] != null && data[key].Type != JTokenType.Null);
        }

        public static TextReadOptions Create(JToken parameters)
        {
            var data = parameters as JObject ?? throw new ArgumentException("Text parameters must be an object.");
            var handles = data["handles"];
            var hasHandles = handles != null && handles.Type != JTokenType.Null;
            var hasBox = data["bbox"] != null && data["bbox"].Type != JTokenType.Null;
            if (hasHandles == hasBox) throw new ArgumentException("Provide exactly one source: handles or bbox. Omit all new source options for legacy manual selection.");
            var result = new TextReadOptions
            {
                GroupingStrength = ReadString(data, "grouping_strength", "normal").Trim().ToLowerInvariant()
            };
            if (result.GroupingStrength != "weak" && result.GroupingStrength != "normal" && result.GroupingStrength != "strong")
                throw new ArgumentException("grouping_strength must be weak, normal or strong.");
            var include = data["include_entities"];
            if (include != null && include.Type != JTokenType.Null && include.Type != JTokenType.Boolean)
                throw new ArgumentException("include_entities must be a boolean.");
            result.IncludeEntities = include?.Type == JTokenType.Boolean && include.Value<bool>();
            var query = new JObject { ["paged"] = true, ["fields"] = new JArray("handle") };
            foreach (var key in new[] { "bbox", "bbox_mode", "space", "cursor", "max_output_bytes" })
                if (data[key] != null) query[key] = data[key].DeepClone();
            result.Query = EntityQueryOptions.Create(query);
            if (hasHandles)
            {
                var values = handles as JArray ?? throw new ArgumentException("handles must be an array of hexadecimal strings.");
                if (values.Count > MaxTextRecords) throw new ArgumentException("At most 5000 handles are accepted; narrow the source.");
                var canonical = values.Select(ReadHandle).Distinct().OrderBy(value => value).ToArray();
                result.Handles = canonical.Select(value => value.ToString("X", CultureInfo.InvariantCulture)).ToArray();
            }
            var digest = new JObject
            {
                ["purpose"] = "text_clusters:v1", ["query"] = result.Query.Fingerprint,
                ["handles"] = result.Handles == null ? JValue.CreateNull() : (JToken)new JArray(result.Handles),
                ["grouping_strength"] = result.GroupingStrength, ["include_entities"] = result.IncludeEntities
            };
            result.Fingerprint = Hash(digest.ToString(Formatting.None));
            return result;
        }

        public JObject ToArguments()
        {
            var result = new JObject
            {
                ["groupingStrength"] = GroupingStrength, ["includeEntities"] = IncludeEntities,
                ["space"] = Query.Space
            };
            if (Handles != null) result["handles"] = new JArray(Handles).ToString(Formatting.None);
            else
            {
                result["bbox"] = new JObject { ["min"] = new JArray(Query.Bbox.Take(3)), ["max"] = new JArray(Query.Bbox.Skip(3)) }.ToString(Formatting.None);
                result["bbox_mode"] = Query.BboxMode;
            }
            return result;
        }

        public string ClusterKey(string snapshot, int ordinal)
            => "read:" + Hash(snapshot + ":" + Fingerprint).Substring(0, 32) + ":" + ordinal.ToString("X", CultureInfo.InvariantCulture);

        private static ulong ReadHandle(JToken value)
        {
            if (value.Type != JTokenType.String) throw new ArgumentException("Each handle must be a hexadecimal string.");
            var text = value.Value<string>()?.Trim();
            if (string.IsNullOrEmpty(text) || text.Length > 16 || !text.All(c => c >= '0' && c <= '9' || c >= 'A' && c <= 'F' || c >= 'a' && c <= 'f')
                || !ulong.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var handle) || handle == 0)
                throw new ArgumentException("Each handle must be a nonzero hexadecimal value of at most 16 digits.");
            return handle;
        }

        private static string ReadString(JObject data, string key, string fallback)
        {
            var value = data[key];
            if (value == null || value.Type == JTokenType.Null) return fallback;
            if (value.Type != JTokenType.String) throw new ArgumentException(key + " must be a string.");
            return value.Value<string>() ?? fallback;
        }

        private static string Hash(string text)
        {
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
        }
    }
}
