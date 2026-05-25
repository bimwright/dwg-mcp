using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Bimwright.Dwg.Plugin.Cad;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class CreateEllipseHandler : IAcadCommand
    {
        public string Name => "create_ellipse";
        public string Description => "Create an ellipse in the current drawing space.";
        public CommandSchema Schema => CommandSchemas.CreateEllipse;

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

            if (!CreatePrimitiveInput.TryReadPositiveFiniteDouble(obj, "major_radius", out var majorRadius, out var majorError))
            {
                return CommandResult.Fail(majorError);
            }

            if (!CreatePrimitiveInput.TryReadPositiveFiniteDouble(obj, "minor_radius", out var minorRadius, out var minorError))
            {
                return CommandResult.Fail(minorError);
            }

            if (!CadPrimitiveValidation.TryValidateEllipseRadiusRatio(
                majorRadius,
                minorRadius,
                out var radiusRatio,
                out var ellipseError))
            {
                return CommandResult.Fail(ellipseError);
            }

            if (!CreatePrimitiveInput.TryReadFiniteDouble(obj, "rotation", out var rotation, out var rotationError))
            {
                return CommandResult.Fail(rotationError);
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

                    var rotationRadians = CreatePrimitiveInput.DegreesToRadians(rotation);
                    var majorAxis = new Vector3d(
                        Math.Cos(rotationRadians) * majorRadius,
                        Math.Sin(rotationRadians) * majorRadius,
                        0d);
                    var ellipse = new Ellipse(
                        CreatePrimitiveInput.ToPoint3d(center),
                        Vector3d.ZAxis,
                        majorAxis,
                        radiusRatio,
                        0d,
                        Math.PI * 2d);
                    CreatePrimitiveInput.ApplyEntityOptions(ellipse, layer, hasLayer, colorIndex, hasColorIndex);

                    CadPrimitiveWriter.AppendToCurrentSpace(db, tx, ellipse);
                    var entity = CadEntityProperties.Describe(ellipse, tx, includeGeometry: true);
                    var result = new
                    {
                        handle = ellipse.Handle.ToString(),
                        entity
                    };

                    tx.Commit();
                    return CommandResult.Success(result);
                }
            }
            catch (Exception ex)
            {
                return CommandResult.Fail("failed to create ellipse: " + ex.Message);
            }
        }
    }
}
