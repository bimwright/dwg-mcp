using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.ToolBaker
{
    public static class BakedToolParameterDefaults
    {
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
