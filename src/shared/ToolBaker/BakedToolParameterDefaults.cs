using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

namespace Bimwright.Dwg.Plugin.ToolBaker
{
    public static class BakedToolParameterDefaults
    {
        public static bool ContainsStandaloneRead(BakedToolRecord record, JObject runtimeArgs)
        {
            if (record == null) return false;
            if (record.Source == "macro")
            {
                JArray sequence;
                try { sequence = JArray.Parse(string.IsNullOrWhiteSpace(record.Sequence) ? "[]" : record.Sequence); }
                catch (JsonException) { return false; } // Existing compiler owns malformed records.
                foreach (var token in sequence)
                {
                    var step = token as JObject;
                    var command = step == null ? token.Value<string>() : step.Value<string>("cmd");
                    if (EntityQueryOptions.RequiresDirectCall(command, step?["params"])) return true;
                }
                return false;
            }
            JObject fixedArgs;
            try { fixedArgs = JObject.Parse(string.IsNullOrWhiteSpace(record.FixedArgs) ? "{}" : record.FixedArgs); }
            catch (JsonException) { fixedArgs = new JObject(); }
            return EntityQueryOptions.RequiresDirectCall(record.HandlerTool, Merge(fixedArgs, runtimeArgs));
        }

        public static JObject Merge(JObject fixedArgs, JObject runtimeArgs)
        {
            var merged = fixedArgs == null ? new JObject() : (JObject)fixedArgs.DeepClone();
            if (runtimeArgs != null)
            {
                foreach (var property in runtimeArgs.Properties())
                {
                    merged[property.Name] = property.Value.DeepClone();
                }
            }
            return merged;
        }
    }
}
