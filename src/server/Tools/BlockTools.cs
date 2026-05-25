using System.ComponentModel;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server.Tools
{
    [McpServerToolType]
    public class BlockTools
    {
        [McpServerTool(Name = "dwg_list_blocks", ReadOnly = true, Idempotent = true), Description(
            "List block definitions in the current AutoCAD drawing with lightweight metadata.")]
        public static Task<string> ListBlocks()
        {
            var request = new JObject();
            return ToolGateway.LoggedCall("list_blocks", request, request);
        }

        [McpServerTool(Name = "dwg_get_block_attributes", ReadOnly = true, Idempotent = true), Description(
            "Read attribute values from a block reference identified by handle.")]
        public static Task<string> GetBlockAttributes(
            [Description("AutoCAD handle of the block reference.")] string handle)
        {
            var request = new JObject
            {
                ["handle"] = handle
            };

            return ToolGateway.LoggedCall("get_block_attributes", request, request);
        }
    }
}
