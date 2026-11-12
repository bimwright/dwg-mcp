using Autodesk.AutoCAD.ApplicationServices;
using Bimwright.Dwg.Plugin.Cad;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class ListLayersHandler : IAcadCommand
    {
        public string Name => "list_layers";
        public string Description => "List layers in the current AutoCAD drawing, optionally with top-level entity counts.";
        public CommandSchema Schema => CommandSchemas.ListLayers;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            var includeCounts = (parameters as JObject)?.Value<bool?>("include_counts") ?? false;
            var counts = includeCounts ? DrawingProfileHandler.CollectLayerCounts(doc.Database) : null;
            var layers = CadLayerService.ListLayers(doc.Database, counts);
            return counts == null
                ? CommandResult.Success(new { layers })
                : CommandResult.Success(new { layers, counts_complete = counts.LayerCountsComplete, count_scope = "model_and_layout_top_level" });
        }
    }
}
