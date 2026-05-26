using System.ComponentModel;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server.Tools
{
    [McpServerToolType]
    public class ViewOutputTools
    {
        [McpServerTool(Name = "dwg_capture_view", ReadOnly = false), Description(
            "Capture the current viewport as an image file. Fails if target directory doesn't exist.")]
        public static Task<string> CaptureView(
            [Description("Absolute path where the output image will be saved.")] string output_path,
            [Description("Overwrite the output file if it already exists.")] bool? overwrite_existing = null,
            [Description("Allow output to the repository root directory.")] bool? allow_repo_output = null)
        {
            var request = new JObject
            {
                ["output_path"] = output_path
            };
            if (overwrite_existing.HasValue) request["overwrite_existing"] = overwrite_existing.Value;
            if (allow_repo_output.HasValue) request["allow_repo_output"] = allow_repo_output.Value;

            return ToolGateway.LoggedCall("capture_view", request, request);
        }
    }
}
