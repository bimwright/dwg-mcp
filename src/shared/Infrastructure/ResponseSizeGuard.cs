using Newtonsoft.Json;
using System.Text;
using Newtonsoft.Json.Linq;

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

            var data = JToken.Parse(json);
            var receipt = OutputReceiptFacts.Compact(data as JObject);
            var artifact = OutputArtifactStore.Write(json, "plugin_result");
            receipt["truncated"] = true;
            receipt["original_length"] = json.Length;
            receipt["original_size_bytes"] = sizeBytes;
            receipt["output_artifact"] = artifact;
            receipt["hint"] = artifact.Value<bool>("complete")
                ? "Full plugin result JSON is in output_artifact; verify bytes/hash before reading. Do not replay a write to recover output."
                : "Full output could not be retained. Inspect drawing/files and the reported outcome; do not replay a write to recover output.";
            receipt["preview"] = Utf8Preview(json, System.Math.Min(512, maxSerializedBytes));
            return receipt;
        }

        private static string Utf8Preview(string json, int maxBytes)
        {
            if (maxBytes <= 0)
            {
                return string.Empty;
            }

            var used = 0;
            var builder = new StringBuilder();
            for (var i = 0; i < json.Length; i++)
            {
                var count = char.IsHighSurrogate(json[i]) && i + 1 < json.Length && char.IsLowSurrogate(json[i + 1]) ? 2 : 1;
                var size = Encoding.UTF8.GetByteCount(json.Substring(i, count));
                if (used + size > maxBytes)
                {
                    break;
                }
                builder.Append(json, i, count);
                i += count - 1;
                used += size;
            }

            return builder.ToString();
        }
    }
}
