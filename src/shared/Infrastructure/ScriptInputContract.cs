using System;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin
{
    public static class ScriptInputContract
    {
        public const int Version = 1;
        public const int MaxDataBytes = 512 * 1024;

        public static bool IsEnhanced(JToken parameters)
            => parameters is JObject obj && new[] { "data", "data_json", "compile_only", "script_inputs_version" }.Any(k => obj.Property(k) != null);

        public static bool RequiresDirectCall(string command, JToken parameters)
            => string.Equals(command, "send_code", StringComparison.OrdinalIgnoreCase) && IsEnhanced(parameters);

        public static void Validate(JToken parameters)
        {
            if (!IsEnhanced(parameters)) return;
            if (parameters["script_inputs_version"]?.Type != JTokenType.Integer ||
                parameters.Value<int>("script_inputs_version") != Version)
                throw new ArgumentException("SCRIPT_INPUTS_UNSUPPORTED: enhanced requests require script_inputs_version=1.");
            if (parameters["data"] != null)
                throw new ArgumentException("SCRIPT_INPUT_INVALID: the wire accepts data_json; use the gateway data parameter.");
            if (parameters["compile_only"] != null && parameters["compile_only"].Type != JTokenType.Boolean)
                throw new ArgumentException("SCRIPT_INPUT_INVALID: compile_only must be a boolean.");
            if (parameters["data_json"] != null && parameters["data_json"].Type != JTokenType.String)
                throw new ArgumentException("SCRIPT_INPUT_INVALID: data_json must be JSON text.");
        }

        public static JToken ParseData(string json)
        {
            if (json == null) return null;
            if (Encoding.UTF8.GetByteCount(json) > MaxDataBytes)
                throw new ArgumentException("SCRIPT_DATA_TOO_LARGE: data exceeds 512 KiB UTF-8.");
            try
            {
                using (var reader = new JsonTextReader(new StringReader(json)) {
                    DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Double, MaxDepth = 64 })
                {
                    var data = JToken.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                    while (reader.Read())
                        if (reader.TokenType != JsonToken.Comment) throw new JsonReaderException("Trailing JSON content.");
                    var values = data is JContainer container ? container.DescendantsAndSelf() : new[] { data };
                    foreach (var value in values.Where(t => t.Type == JTokenType.Float))
                    {
                        var number = value.Value<double>();
                        if (double.IsNaN(number) || double.IsInfinity(number)) throw new JsonReaderException("Non-finite number.");
                    }
                    return data;
                }
            }
            catch (JsonException) { throw new ArgumentException("SCRIPT_INPUT_INVALID: data must be valid JSON (depth <=64, unique object keys, finite numbers)."); }
        }
    }
}
