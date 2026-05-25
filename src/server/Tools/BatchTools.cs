using System;
using System.ComponentModel;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server.Tools
{
    [McpServerToolType]
    public class BatchTools
    {
        [McpServerTool(Name = "dwg_batch_execute"), Description(
            "Run multiple internal DWG commands sequentially as a logical batch. commands must be a JSON array of {cmd, params}.")]
        public static Task<string> BatchExecute(
            [Description("JSON array: [{\"cmd\":\"get_selected_texts\",\"params\":{}}]. Wire command names are unprefixed.")] string commands)
        {
            var parsed = string.IsNullOrWhiteSpace(commands) ? new JArray() : JArray.Parse(commands);
            var request = new JObject { ["commands"] = parsed };
            return ToolGateway.LoggedCall("batch_execute", request, request);
        }
    }
}
