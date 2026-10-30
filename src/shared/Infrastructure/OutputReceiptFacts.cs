using System;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin
{
    /// <summary>Bounded reported facts from known response layers; never infer mutation.</summary>
    public static class OutputReceiptFacts
    {
        public static JObject Compact(JObject data, int textLimit = 256, int depth = 0)
        {
            var result = new JObject();
            if (data == null) return result;
            foreach (var key in new[] { "index", "cmd", "ok", "command_ok", "outcome", "output_verified", "partial_failure", "transactional", "rollback",
                "mutation_applied", "error", "output_path", "saved_path", "count", "created_count", "modified_count", "failed_count" })
            {
                var value = data[key];
                if (!(value is JValue)) continue;
                if (value.Type == JTokenType.String && value.Value<string>().Length > textLimit)
                { result[key] = OutputArtifactStore.Preview(value.Value<string>(), textLimit); result[key + "_omitted"] = true; }
                else if (value.ToString(Newtonsoft.Json.Formatting.None).Length <= 256) result[key] = value.DeepClone();
                else result[key + "_omitted"] = true;
            }
            foreach (var key in new[] { "file_receipt", "output_artifact" })
                if (data[key] is JObject receipt) result[key] = Receipt(receipt, textLimit);
            if (depth < 2 && data["results"] is JArray items)
            {
                result["items_reported"] = items.Count;
                var sample = new JArray();
                for (var i = 0; i < Math.Min(items.Count, 8); i++)
                {
                    var facts = Compact(items[i] as JObject, textLimit, depth + 1); facts["index"] = i;
                    if (items[i]?["result"] is JObject inner) facts["result"] = Compact(inner, textLimit, depth + 1);
                    sample.Add(facts);
                }
                result["results"] = sample;
                result["item_details_omitted"] = true;
            }
            return result;
        }

        public static JObject Receipt(JObject data, int previewLimit = 256)
        {
            var result = new JObject();
            foreach (var key in new[] { "status", "complete", "scope", "path", "sha256", "bytes", "reason", "created_at_utc", "verified", "exists", "new_or_changed" })
            {
                var value = data?[key];
                if (!(value is JValue)) continue;
                var limit = key == "path" ? 1024 : 128;
                if (value.ToString(Newtonsoft.Json.Formatting.None).Length <= limit) result[key] = value.DeepClone();
                else result[key + "_omitted"] = true;
            }
            if (data?["preview"]?.Type == JTokenType.String && previewLimit > 0)
            {
                result["preview"] = OutputArtifactStore.Preview(data.Value<string>("preview"), Math.Min(512, previewLimit));
                result["preview_complete"] = false;
            }
            if (previewLimit > 0 && data?["retention"] is JObject retention)
            {
                var policy = new JObject();
                foreach (var key in new[] { "max_age_days", "group_max_bytes", "max_files", "eviction" })
                    if (retention[key] is JValue value && value.ToString().Length <= 128) policy[key] = value.DeepClone();
                result["retention"] = policy;
            }
            return result;
        }
    }
}
