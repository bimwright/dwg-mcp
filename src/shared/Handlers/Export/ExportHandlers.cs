using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Bimwright.Dwg.Plugin.Export;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class ExportDxfHandler : IAcadCommand
    {
        public string Name => "export_dxf";
        public string Description => "Export the drawing to a DXF file.";
        public CommandSchema Schema => CommandSchemas.ExportDxf;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!(parameters is JObject obj))
            {
                return CommandResult.Fail("params must be an object");
            }

            var outputPath = obj["output_path"]?.Value<string>();
            var overwriteExisting = obj["overwrite_existing"]?.Value<bool>() ?? false;
            var allowRepoOutput = obj["allow_repo_output"]?.Value<bool>() ?? false;

            var normalizedPath = ExportPathPolicy.ValidateAndNormalize(
                outputPath,
                ".dxf",
                overwriteExisting,
                allowRepoOutput,
                out var error);

            if (normalizedPath == null)
            {
                return CommandResult.Fail(error);
            }

            try
            {
                doc.Database.DxfOut(normalizedPath, 16, DwgVersion.Current);
                return CommandResult.Success(new { output_path = normalizedPath });
            }
            catch (Exception ex)
            {
                return CommandResult.Fail("failed to export dxf: " + ErrorSanitizer.Sanitize(ex.Message));
            }
        }
    }
}
