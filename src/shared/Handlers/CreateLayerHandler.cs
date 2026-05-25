using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Bimwright.Dwg.Plugin.Cad;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class CreateLayerHandler : IAcadCommand
    {
        public string Name => "create_layer";
        public string Description => "Ensure a layer exists without overwriting existing layer properties.";
        public CommandSchema Schema => CommandSchemas.CreateLayer;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            var obj = parameters as JObject;
            if (obj == null)
            {
                return CommandResult.Fail("params must be an object");
            }

            var name = obj["name"]?.Value<string>();
            if (!CadWire.TryReadAciColor(parameters, "color_index", 7, out var colorIndex, out var colorError))
            {
                return CommandResult.Fail(colorError);
            }

            var db = doc.Database;
            using (var tx = db.TransactionManager.StartTransaction())
            {
                if (!CadLayerService.TryEnsureLayer(db, tx, name, colorIndex, out var layerId, out var created, out var error))
                {
                    return CommandResult.Fail(error);
                }

                var layer = (LayerTableRecord)tx.GetObject(layerId, OpenMode.ForRead);
                var result = new
                {
                    name = layer.Name,
                    created,
                    handle = layer.Handle.ToString(),
                    color_index = (int)layer.Color.ColorIndex
                };

                tx.Commit();
                return CommandResult.Success(result);
            }
        }
    }
}
