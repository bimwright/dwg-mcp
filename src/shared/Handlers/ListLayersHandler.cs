using Autodesk.AutoCAD.ApplicationServices;
using Bimwright.Dwg.Plugin.Cad;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class ListLayersHandler : IAcadCommand
    {
        public string Name => "list_layers";
        public string Description => "List layers in the current AutoCAD drawing.";
        public CommandSchema Schema => CommandSchemas.ListLayers;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            return CommandResult.Success(new { layers = CadLayerService.ListLayers(doc.Database) });
        }
    }
}
