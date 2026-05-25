using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.ToolBaker
{
    public static class BakedToolRuntimeSource
    {
        public static string BuildPreset(string handlerTool, JObject fixedArgs)
        {
            return new JObject
            {
                ["kind"] = "preset",
                ["handler_tool"] = handlerTool,
                ["fixed_args"] = fixedArgs ?? new JObject()
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        public static string BuildMacro(string[] sequence)
        {
            return new JObject
            {
                ["kind"] = "macro",
                ["sequence"] = new JArray(sequence ?? new string[0])
            }.ToString(Newtonsoft.Json.Formatting.None);
        }
    }
}
