using System;
using Autodesk.AutoCAD.ApplicationServices;
using Bimwright.Dwg.Plugin.Cad;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class SelectByLayerHandler : IAcadCommand
    {
        public string Name => "select_by_layer";
        public string Description => "Return handles for model-space entities on a layer without changing pickfirst selection.";
        public CommandSchema Schema => CommandSchemas.SelectByLayer;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            var layer = QueryHandlerInput.ReadOptionalString(parameters, "layer");
            if (string.IsNullOrWhiteSpace(layer))
            {
                return CommandResult.Fail("layer must be a non-empty string");
            }

            if (!QueryHandlerInput.TryReadOptionalColorIndex(parameters, out var colorIndex, out var colorError))
            {
                return CommandResult.Fail(colorError);
            }

            if (!QueryHandlerInput.TryReadOptionalLimit(parameters, readLimit: true, out var limit, out var limitError))
            {
                return CommandResult.Fail(limitError);
            }

            var options = new CadEntityQueryOptions(
                QueryHandlerInput.ReadOptionalString(parameters, "entity_type"),
                layer,
                colorIndex,
                limit,
                includeGeometry: false);

            try
            {
                var handles = CadQueryService.QueryHandles(doc.Database, options);
                return CommandResult.Success(new { count = handles.Count, handles });
            }
            catch (Exception ex)
            {
                return CommandResult.Fail("failed to select by layer: " + ex.Message);
            }
        }
    }
}
