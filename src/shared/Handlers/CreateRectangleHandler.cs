using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Bimwright.Dwg.Plugin.Cad;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class CreateRectangleHandler : IAcadCommand
    {
        public string Name => "create_rectangle";
        public string Description => "Create a rectangular closed polyline in the current drawing space.";
        public CommandSchema Schema => CommandSchemas.CreateRectangle;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!(parameters is JObject obj))
            {
                return CommandResult.Fail("params must be an object");
            }

            if (!CadWire.TryParsePoint(obj["corner1"], out var corner1, out var corner1Error))
            {
                return CommandResult.Fail("corner1 " + corner1Error);
            }

            if (!CadWire.TryParsePoint(obj["corner2"], out var corner2, out var corner2Error))
            {
                return CommandResult.Fail("corner2 " + corner2Error);
            }

            if (!CadPrimitiveValidation.TryValidateRectangleCorners(corner1, corner2, out var rectangleError))
            {
                return CommandResult.Fail(rectangleError);
            }

            if (!CreatePrimitiveInput.TryReadEntityOptions(
                obj,
                parameters,
                out var layer,
                out var hasLayer,
                out var colorIndex,
                out var hasColorIndex,
                out var optionsError))
            {
                return CommandResult.Fail(optionsError);
            }

            var db = doc.Database;
            try
            {
                using (var tx = db.TransactionManager.StartTransaction())
                {
                    if (!CreatePrimitiveInput.TryEnsureLayer(db, tx, layer, hasLayer, colorIndex, out var layerError))
                    {
                        return CommandResult.Fail(layerError);
                    }

                    var rectangle = new Polyline();
                    rectangle.AddVertexAt(0, new Point2d(corner1.X, corner1.Y), 0d, 0d, 0d);
                    rectangle.AddVertexAt(1, new Point2d(corner2.X, corner1.Y), 0d, 0d, 0d);
                    rectangle.AddVertexAt(2, new Point2d(corner2.X, corner2.Y), 0d, 0d, 0d);
                    rectangle.AddVertexAt(3, new Point2d(corner1.X, corner2.Y), 0d, 0d, 0d);
                    rectangle.Closed = true;
                    CreatePrimitiveInput.ApplyEntityOptions(rectangle, layer, hasLayer, colorIndex, hasColorIndex);

                    CadPrimitiveWriter.AppendToCurrentSpace(db, tx, rectangle);
                    var entity = CadEntityProperties.Describe(rectangle, tx, includeGeometry: true);
                    var result = new
                    {
                        handle = rectangle.Handle.ToString(),
                        entity
                    };

                    tx.Commit();
                    return CommandResult.Success(result);
                }
            }
            catch (Exception ex)
            {
                return CommandResult.Fail("failed to create rectangle: " + ex.Message);
            }
        }
    }
}
