using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Bimwright.Dwg.Plugin.Cad;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class CreateArcHandler : IAcadCommand
    {
        public string Name => "create_arc";
        public string Description => "Create an arc in the current drawing space.";
        public CommandSchema Schema => CommandSchemas.CreateArc;

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

            if (!CreatePrimitiveInput.TryReadPositiveFiniteDouble(obj, "radius", out var radius, out var radiusError))
            {
                return CommandResult.Fail(radiusError);
            }

            if (!CreatePrimitiveInput.TryReadFiniteDouble(obj, "start_angle", out var startAngle, out var startAngleError))
            {
                return CommandResult.Fail(startAngleError);
            }

            if (!CreatePrimitiveInput.TryReadFiniteDouble(obj, "end_angle", out var endAngle, out var endAngleError))
            {
                return CommandResult.Fail(endAngleError);
            }

            if (!CadPrimitiveValidation.TryValidateArcSweepDegrees(startAngle, endAngle, out var arcError))
            {
                return CommandResult.Fail(arcError);
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

                    var arc = new Arc(
                        CreatePrimitiveInput.ToPoint3d(center),
                        radius,
                        CreatePrimitiveInput.DegreesToRadians(startAngle),
                        CreatePrimitiveInput.DegreesToRadians(endAngle));
                    CreatePrimitiveInput.ApplyEntityOptions(arc, layer, hasLayer, colorIndex, hasColorIndex);

                    CadPrimitiveWriter.AppendToCurrentSpace(db, tx, arc);
                    var entity = CadEntityProperties.Describe(arc, tx, includeGeometry: true);
                    var result = new
                    {
                        handle = arc.Handle.ToString(),
                        entity
                    };

                    tx.Commit();
                    return CommandResult.Success(result);
                }
            }
            catch (Exception ex)
            {
                return CommandResult.Fail("failed to create arc: " + ex.Message);
            }
        }
    }
}
