using System;
using Autodesk.AutoCAD.ApplicationServices;
using Bimwright.Dwg.Plugin.Cad;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class CountEntitiesHandler : IAcadCommand
    {
        public string Name => "count_entities";
        public string Description => "Count model-space entities by optional type, layer, and color filters.";
        public CommandSchema Schema => CommandSchemas.CountEntities;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!QueryHandlerInput.TryValidateEntityType(parameters, out var typeError)) return CommandResult.Fail(typeError);
            if (EntityQueryOptions.IsExtended(parameters))
            {
                foreach (var field in new[] { "fields", "cursor", "paged", "max_output_bytes", "include_geometry" })
                    if (parameters[field] != null && parameters[field].Type != JTokenType.Null)
                        return CommandResult.Fail("Extended count_entities does not accept " + field + "; use bbox, bbox_mode and space only.");
                try { return CommandResult.Success(CadEntityPageService.Query(doc, EntityQueryOptions.Create(parameters), countOnly: true)); }
                catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is OverflowException)
                { return CommandResult.Fail(ex.Message); }
            }
            if (!QueryHandlerInput.TryCreateOptions(
                parameters,
                includeGeometry: false,
                readLimit: false,
                out var options,
                out var error))
            {
                return CommandResult.Fail(error);
            }

            try
            {
                var typeHint = new CadEntityTypeHint();
                var count = CadQueryService.CountEntities(doc.Database, options, typeHint);
                return count == 0 ? CommandResult.Success(new { count, hint = typeHint.ToJson("model") })
                    : CommandResult.Success(new { count });
            }
            catch (Exception ex)
            {
                return CommandResult.Fail("failed to count entities: " + ex.Message);
            }
        }
    }
}
