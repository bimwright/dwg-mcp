using System;
using Autodesk.AutoCAD.ApplicationServices;
using Bimwright.Dwg.Plugin.Cad;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class SelectByTypeHandler : IAcadCommand
    {
        public string Name => "select_by_type";
        public string Description => "Return handles for model-space entities of a type without changing pickfirst selection.";
        public CommandSchema Schema => CommandSchemas.SelectByType;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            var entityType = QueryHandlerInput.ReadOptionalString(parameters, "entity_type");
            if (string.IsNullOrWhiteSpace(entityType))
            {
                return CommandResult.Fail("entity_type must be a non-empty string");
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
                entityType,
                QueryHandlerInput.ReadOptionalString(parameters, "layer"),
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
                return CommandResult.Fail("failed to select by type: " + ex.Message);
            }
        }
    }
}
