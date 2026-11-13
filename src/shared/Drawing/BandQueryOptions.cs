using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin
{
    internal sealed class BandQueryOptions
    {
        internal const int MaxBandsPerPage = 128;
        internal const int MaxSamples = 5;
        internal const int SampleMaxLength = 120;
        internal EntityQueryOptions Query { get; private set; }
        internal string Axis { get; private set; }
        internal double Gap { get; private set; }
        internal string[] Types { get; private set; }
        internal bool SampleTexts { get; private set; }
        internal string Fingerprint { get; private set; }

        internal static BandQueryOptions Create(JToken parameters)
        {
            var data = parameters as JObject ?? throw new ArgumentException("Band parameters must be an object.");
            var axis = data["axis"]?.Type == JTokenType.String ? data.Value<string>("axis").ToLowerInvariant() : null;
            if (axis != "x" && axis != "y") throw new ArgumentException("axis must be x or y.");
            if (data["gap"]?.Type is not (JTokenType.Integer or JTokenType.Float))
                throw new ArgumentException("gap must be a finite, nonnegative drawing-unit distance.");
            var gap = data.Value<double>("gap");
            if (gap < 0 || double.IsNaN(gap) || double.IsInfinity(gap))
                throw new ArgumentException("gap must be a finite, nonnegative drawing-unit distance.");

            var sample = data["sample_texts"];
            if (sample != null && sample.Type != JTokenType.Boolean)
                throw new ArgumentException("sample_texts must be a boolean.");
            var types = Array.Empty<string>();
            if (data["types"] is JToken filter && filter.Type != JTokenType.Null)
            {
                if (filter is not JArray list || list.Count is < 1 or > 16
                    || list.Any(t => t.Type != JTokenType.String || string.IsNullOrWhiteSpace(t.Value<string>()) || t.Value<string>().Length > 256))
                    throw new ArgumentException("types must be a JSON array of 1..16 entity type names, each at most 256 characters.");
                types = list.Values<string>().Select(EntityQueryOptions.NormalizeType)
                    .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
            }

            var query = new JObject { ["limit"] = MaxBandsPerPage };
            foreach (var key in new[] { "bbox", "space", "cursor", "limit", "max_output_bytes" })
                if (data[key] != null) query[key] = data[key].DeepClone();
            var result = new BandQueryOptions
            {
                Axis = axis, Gap = gap == 0 ? 0 : gap, Types = types, SampleTexts = sample?.Value<bool>() == true,
                Query = EntityQueryOptions.Create(query)
            };
            result.Fingerprint = EntityQueryOptions.Hash(new JObject
            {
                ["purpose"] = "axis_bands:v1", ["query"] = result.Query.Fingerprint,
                ["axis"] = result.Axis, ["gap"] = result.Gap, ["sample_texts"] = result.SampleTexts,
                ["types"] = new JArray(types.Select(type => type.ToUpperInvariant()))
            });
            return result;
        }

        internal JObject ToArguments()
        {
            var result = new JObject
            {
                ["axis"] = Axis, ["gap"] = Gap, ["space"] = Query.Space,
                ["sample_texts"] = SampleTexts, ["limit"] = Math.Min(Query.Limit, MaxBandsPerPage)
            };
            if (Types.Length > 0) result["types"] = new JArray(Types).ToString(Formatting.None);
            if (Query.Bbox != null)
                result["bbox"] = new JObject { ["min"] = new JArray(Query.Bbox.Take(3)), ["max"] = new JArray(Query.Bbox.Skip(3)) }
                    .ToString(Formatting.None);
            return result;
        }
    }
}
