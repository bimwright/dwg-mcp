using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Bimwright.Dwg.Plugin.Cad;
using Bimwright.Dwg.Pid;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers.Pid
{
    public class PidAddEquipmentTagHandler : IAcadCommand
    {
        public string Name => "pid_add_equipment_tag";
        public string Description => "Add an equipment tag annotation in current drawing space.";
        public CommandSchema Schema => CommandSchemas.AddEquipmentTag;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!(parameters is JObject obj))
            {
                return CommandResult.Fail("params must be an object");
            }

            if (!CadWire.TryParsePoint(obj["position"], out var position, out var posError))
            {
                return CommandResult.Fail("position " + posError);
            }

            var tagText = obj["tag_text"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(tagText))
            {
                return CommandResult.Fail("tag_text must be a non-empty string");
            }

            var layerName = obj["layer"]?.Value<string>() ?? "PID-ANNOTATION";
            var db = doc.Database;

            try
            {
                using (var tx = db.TransactionManager.StartTransaction())
                {
                    if (!CadLayerService.TryEnsureLayer(db, tx, layerName, 7, out _, out _, out var layerError))
                    {
                        return CommandResult.Fail(layerError);
                    }

                    var text = new DBText();
                    text.Position = new Point3d(position.X, position.Y, position.Z);
                    text.Height = 2.5;
                    text.TextString = tagText;
                    text.Layer = layerName;

                    CadPrimitiveWriter.AppendToCurrentSpace(db, tx, text);
                    var entity = CadEntityProperties.Describe(text, tx, includeGeometry: true);
                    var result = new
                    {
                        handle = text.Handle.ToString(),
                        entity
                    };

                    tx.Commit();
                    return CommandResult.Success(result);
                }
            }
            catch (Exception ex)
            {
                return CommandResult.Fail("failed to add equipment tag: " + ex.Message);
            }
        }
    }
}
