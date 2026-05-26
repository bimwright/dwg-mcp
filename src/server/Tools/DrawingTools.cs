using System.ComponentModel;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server.Tools
{
    [McpServerToolType]
    public class DrawingTools
    {
        [McpServerTool(Name = "dwg_get_variables", ReadOnly = true, Idempotent = true), Description(
            "Read current values of an allowlist of AutoCAD drawing system variables.")]
        public static Task<string> GetVariables()
        {
            var request = new JObject();
            return ToolGateway.LoggedCall("get_variables", request, request);
        }
    }
}
