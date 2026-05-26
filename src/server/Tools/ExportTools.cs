using System.ComponentModel;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server.Tools
{
    [McpServerToolType]
    public class ExportTools
    {
        [McpServerTool(Name = "dwg_export_pdf", ReadOnly = false), Description(
            "Export the current drawing layout/view to a PDF file.")]
        public static Task<string> ExportPdf(
            [Description("Absolute path where the output PDF will be saved.")] string output_path,
            [Description("Overwrite the output file if it already exists.")] bool? overwrite_existing = null,
            [Description("Allow output to the repository root directory.")] bool? allow_repo_output = null)
        {
            var request = new JObject
            {
                ["output_path"] = output_path
            };
            if (overwrite_existing.HasValue) request["overwrite_existing"] = overwrite_existing.Value;
            if (allow_repo_output.HasValue) request["allow_repo_output"] = allow_repo_output.Value;

            return ToolGateway.LoggedCall("export_pdf", request, request);
        }

        [McpServerTool(Name = "dwg_export_dxf", ReadOnly = false), Description(
            "Export the drawing to a DXF file.")]
        public static Task<string> ExportDxf(
            [Description("Absolute path where the output DXF will be saved.")] string output_path,
            [Description("Overwrite the output file if it already exists.")] bool? overwrite_existing = null,
            [Description("Allow output to the repository root directory.")] bool? allow_repo_output = null)
        {
            var request = new JObject
            {
                ["output_path"] = output_path
            };
            if (overwrite_existing.HasValue) request["overwrite_existing"] = overwrite_existing.Value;
            if (allow_repo_output.HasValue) request["allow_repo_output"] = allow_repo_output.Value;

            return ToolGateway.LoggedCall("export_dxf", request, request);
        }

        [McpServerTool(Name = "dwg_export_image", ReadOnly = false), Description(
            "Export the drawing view to a raster image file (BMP/PNG/JPG).")]
        public static Task<string> ExportImage(
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

            return ToolGateway.LoggedCall("export_image", request, request);
        }
    }
}
