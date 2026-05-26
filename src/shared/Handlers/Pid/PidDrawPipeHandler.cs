using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Bimwright.Dwg.Plugin.Cad;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers.Pid
{
    public class PidDrawPipeHandler : IAcadCommand
    {
        public string Name => "pid_draw_pipe";
        public string Description => "Draw process/utility piping line in the current drawing space.";
        public CommandSchema Schema => CommandSchemas.DrawPipe;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!(parameters is JObject obj))
            {
                return CommandResult.Fail("params must be an object");
            }

            if (!CadWire.TryParsePoint(obj["start"], out var start, out var startError))
            {
                return CommandResult.Fail("start " + startError);
            }

            if (!CadWire.TryParsePoint(obj["end"], out var end, out var endError))
            {
                return CommandResult.Fail("end " + endError);
            }

            var startPoint = new Point3d(start.X, start.Y, start.Z);
            var endPoint = new Point3d(end.X, end.Y, end.Z);
            if (startPoint.DistanceTo(endPoint) == 0d)
            {
                return CommandResult.Fail("start and end points must be different");
            }

            var layerName = obj["layer"]?.Value<string>() ?? "PID-PROCESS-PIPING";
            var db = doc.Database;

            try
            {
                using (var tx = db.TransactionManager.StartTransaction())
                {
                    // Ensure the piping layer exists (PID-PROCESS-PIPING by default uses ACI 4)
                    int layerColor = 4; // PID-PROCESS-PIPING is ACI 4, PID-UTILITY-PIPING is ACI 3
                    if (string.Equals(layerName, "PID-UTILITY-PIPING", StringComparison.OrdinalIgnoreCase))
                    {
                        layerColor = 3;
                    }
                    else if (string.Equals(layerName, "PID-CHEMICAL-DOSING", StringComparison.OrdinalIgnoreCase))
                    {
                        layerColor = 30;
                    }
                    else if (string.Equals(layerName, "PID-AIR-DIFFUSION", StringComparison.OrdinalIgnoreCase))
                    {
                        layerColor = 151;
                    }
                    else if (string.Equals(layerName, "PID-SLUDGE", StringComparison.OrdinalIgnoreCase))
                    {
                        layerColor = 34;
                    }
                    else if (string.Equals(layerName, "PID-EFFLUENT", StringComparison.OrdinalIgnoreCase))
                    {
                        layerColor = 130;
                    }

                    if (!CadLayerService.TryEnsureLayer(db, tx, layerName, layerColor, out _, out _, out var layerError))
                    {
                        return CommandResult.Fail(layerError);
                    }

                    var line = new Line(startPoint, endPoint);
                    line.Layer = layerName;

                    CadPrimitiveWriter.AppendToCurrentSpace(db, tx, line);
                    var entity = CadEntityProperties.Describe(line, tx, includeGeometry: true);
                    var result = new
                    {
                        handle = line.Handle.ToString(),
                        entity
                    };

                    tx.Commit();
                    return CommandResult.Success(result);
                }
            }
            catch (Exception ex)
            {
                return CommandResult.Fail("failed to draw pipe: " + ex.Message);
            }
        }
    }
}
