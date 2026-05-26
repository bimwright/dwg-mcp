using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Bimwright.Dwg.Plugin.Cad;
using Bimwright.Dwg.Plugin.Pid;
using Bimwright.Dwg.Pid;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers.Pid
{
    public class PidAddFlowArrowHandler : IAcadCommand
    {
        public string Name => "pid_add_flow_arrow";
        public string Description => "Add a procedural flow arrow in current drawing space.";
        public CommandSchema Schema => CommandSchemas.AddFlowArrow;

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

            if (!CadWire.TryParsePoint(obj["direction"], out var direction, out var dirError))
            {
                return CommandResult.Fail("direction " + dirError);
            }

            var posPoint = new Point3d(position.X, position.Y, position.Z);
            var dirVector = new Vector3d(direction.X, direction.Y, direction.Z);
            if (dirVector.Length == 0d)
            {
                return CommandResult.Fail("direction vector cannot be zero");
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

                    var arrow = PidProceduralGeometry.DrawFlowArrow(posPoint, dirVector, 1.0);
                    arrow.Layer = layerName;

                    CadPrimitiveWriter.AppendToCurrentSpace(db, tx, arrow);
                    var entity = CadEntityProperties.Describe(arrow, tx, includeGeometry: true);
                    var result = new
                    {
                        handle = arrow.Handle.ToString(),
                        entity
                    };

                    tx.Commit();
                    return CommandResult.Success(result);
                }
            }
            catch (Exception ex)
            {
                return CommandResult.Fail("failed to add flow arrow: " + ex.Message);
            }
        }
    }
}
