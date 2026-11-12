using System.ComponentModel;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server.Tools
{
    [McpServerToolType]
    public class BlockTools
    {
        [McpServerTool(Name = "dwg_list_blocks", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description(
            "List named, non-layout block definitions in the current drawing. entity_count counts entities inside the definition, excluding separately counted attribute definitions. " +
            "reference_count counts active inserts in model space and paper-space layouts; nested_reference_count counts active inserts owned by other block definitions once each, without multiplying by parent inserts. " +
            "Dynamic block variants count toward their named definition. Counts use the current drawing database without opening external DWG files. " +
            "unused is true when both counts are zero. Unavailable counts and unused status are null.")]
        public static Task<string> ListBlocks()
        {
            var request = new JObject();
            return ToolGateway.LoggedCall("list_blocks", request, request);
        }

        [McpServerTool(Name = "dwg_get_block_attributes", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description(
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
