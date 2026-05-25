using Newtonsoft.Json;
using System.Text;

namespace Bimwright.Dwg.Plugin
{
    public static class ResponseSizeGuard
    {
        public const int DefaultMaxSerializedBytes = 10 * 1024 * 1024;

        public static object ApplyResult(object result, int maxSerializedBytes = DefaultMaxSerializedBytes)
        {
            var json = JsonConvert.SerializeObject(result);
            var sizeBytes = json == null ? 0 : Encoding.UTF8.GetByteCount(json);
            if (json == null || sizeBytes <= maxSerializedBytes)
            {
                return result;
            }

            return new
            {
                truncated = true,
                original_length = json.Length,
                original_size_bytes = sizeBytes,
                hint = "Response exceeded the 10MB serialized payload limit; narrow the selection or request smaller batches.",
                preview = Utf8Preview(json, maxSerializedBytes)
            };
        }

        private static string Utf8Preview(string json, int maxBytes)
        {
            if (maxBytes <= 0)
            {
                return string.Empty;
            }

            var used = 0;
            var builder = new StringBuilder();
            foreach (var ch in json)
            {
                var size = Encoding.UTF8.GetByteCount(new[] { ch });
                if (used + size > maxBytes)
                {
                    break;
                }
                builder.Append(ch);
                used += size;
            }

            return builder.ToString();
        }
    }
}
