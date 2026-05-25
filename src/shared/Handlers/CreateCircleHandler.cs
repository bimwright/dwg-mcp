using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Bimwright.Dwg.Plugin.Cad;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class CreateCircleHandler : IAcadCommand
    {
        public string Name => "create_circle";
        public string Description => "Create a circle in the current drawing space.";
        public CommandSchema Schema => CommandSchemas.CreateCircle;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!(parameters is JObject obj))
            {
                return CommandResult.Fail("params must be an object");
            }

            if (!CadWire.TryParsePoint(obj["center"], out var center, out var centerError))
            {
                return CommandResult.Fail("center " + centerError);
            }

            var radius = obj["radius"]?.Value<double>() ?? 0d;
            if (radius <= 0d || double.IsNaN(radius) || double.IsInfinity(radius))
            {
                return CommandResult.Fail("radius must be a finite positive number");
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

                    var circle = new Circle(ToPoint3d(center), Vector3d.ZAxis, radius);
                    if (hasLayer)
                    {
                        circle.Layer = layer;
                    }

                    if (hasColorIndex)
                    {
                        circle.Color = Color.FromColorIndex(ColorMethod.ByAci, (short)colorIndex);
                    }

                    CadPrimitiveWriter.AppendToCurrentSpace(db, tx, circle);
                    var entity = CadEntityProperties.Describe(circle, tx, includeGeometry: true);
                    var result = new
                    {
                        handle = circle.Handle.ToString(),
                        entity
                    };

                    tx.Commit();
                    return CommandResult.Success(result);
                }
            }
            catch (Exception ex)
            {
                return CommandResult.Fail("failed to create circle: " + ex.Message);
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
