using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Bimwright.Dwg.Plugin.Cad;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class CreateLineHandler : IAcadCommand
    {
        public string Name => "create_line";
        public string Description => "Create a line in the current drawing space.";
        public CommandSchema Schema => CommandSchemas.CreateLine;

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

            var startPoint = ToPoint3d(start);
            var endPoint = ToPoint3d(end);
            if (startPoint.DistanceTo(endPoint) == 0d)
            {
                return CommandResult.Fail("start and end points must be different");
            }

            if (!TryReadOptionalColor(parameters, out var colorIndex, out var hasColorIndex, out var colorError))
            {
                return CommandResult.Fail(colorError);
            }

            var layer = obj["layer"]?.Value<string>();
            var hasLayer = obj["layer"] != null && obj["layer"].Type != JTokenType.Null;
            var db = doc.Database;

            try
            {
                using (var tx = db.TransactionManager.StartTransaction())
                {
                    if (hasLayer &&
                        !CadLayerService.TryEnsureLayer(db, tx, layer, colorIndex, out _, out _, out var layerError))
                    {
                        return CommandResult.Fail(layerError);
                    }

                    var line = new Line(startPoint, endPoint);
                    if (hasLayer)
                    {
                        line.Layer = layer;
                    }

                    if (hasColorIndex)
                    {
                        line.Color = Color.FromColorIndex(ColorMethod.ByAci, (short)colorIndex);
                    }

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
                return CommandResult.Fail("failed to create line: " + ex.Message);
            }
        }

        private static Point3d ToPoint3d(CadPointInput point)
            => new Point3d(point.X, point.Y, point.Z);

        private static bool TryReadOptionalColor(
            JToken parameters,
            out int colorIndex,
            out bool hasColorIndex,
            out string error)
        {
            hasColorIndex = false;
            var obj = parameters as JObject;
            var token = obj?["color_index"];
            if (token != null && token.Type != JTokenType.Null)
            {
                hasColorIndex = true;
            }

            return CadWire.TryReadAciColor(parameters, "color_index", 7, out colorIndex, out error);
        }
    }
}
