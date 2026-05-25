using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Bimwright.Dwg.Plugin.Cad;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class CreatePointHandler : IAcadCommand
    {
        public string Name => "create_point";
        public string Description => "Create a point in the current drawing space.";
        public CommandSchema Schema => CommandSchemas.CreatePoint;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!(parameters is JObject obj))
            {
                return CommandResult.Fail("params must be an object");
            }

            if (!CadWire.TryParsePoint(obj["point"], out var point, out var pointError))
            {
                return CommandResult.Fail("point " + pointError);
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

                    var dbPoint = new DBPoint(CreatePrimitiveInput.ToPoint3d(point));
                    CreatePrimitiveInput.ApplyEntityOptions(dbPoint, layer, hasLayer, colorIndex, hasColorIndex);

                    CadPrimitiveWriter.AppendToCurrentSpace(db, tx, dbPoint);
                    var entity = CadEntityProperties.Describe(dbPoint, tx, includeGeometry: true);
                    var result = new
                    {
                        handle = dbPoint.Handle.ToString(),
                        entity
                    };

                    tx.Commit();
                    return CommandResult.Success(result);
                }
            }
            catch (Exception ex)
            {
                return CommandResult.Fail("failed to create point: " + ex.Message);
            }
        }
    }

    internal static class CreatePrimitiveInput
    {
        internal static Point3d ToPoint3d(CadPointInput point)
            => new Point3d(point.X, point.Y, point.Z);

        internal static bool TryReadEntityOptions(
            JObject obj,
            JToken parameters,
            out string layer,
            out bool hasLayer,
            out int colorIndex,
            out bool hasColorIndex,
            out string error)
        {
            layer = obj["layer"]?.Value<string>();
            hasLayer = obj["layer"] != null && obj["layer"].Type != JTokenType.Null;
            hasColorIndex = false;
            var colorToken = obj["color_index"];
            if (colorToken != null && colorToken.Type != JTokenType.Null)
            {
                hasColorIndex = true;
            }

            return CadWire.TryReadAciColor(parameters, "color_index", 7, out colorIndex, out error);
        }

        internal static bool TryEnsureLayer(
            Database db,
            Transaction tx,
            string layer,
            bool hasLayer,
            int colorIndex,
            out string error)
        {
            error = null;
            return !hasLayer ||
                CadLayerService.TryEnsureLayer(db, tx, layer, colorIndex, out _, out _, out error);
        }

        internal static void ApplyEntityOptions(
            Entity entity,
            string layer,
            bool hasLayer,
            int colorIndex,
            bool hasColorIndex)
        {
            if (hasLayer)
            {
                entity.Layer = layer;
            }

            if (hasColorIndex)
            {
                entity.Color = Color.FromColorIndex(ColorMethod.ByAci, (short)colorIndex);
            }
        }

        internal static bool TryReadFiniteDouble(
            JObject obj,
            string fieldName,
            out double value,
            out string error)
        {
            value = 0d;
            error = null;

            var token = obj[fieldName];
            if (token == null || token.Type == JTokenType.Null)
            {
                error = fieldName + " is required";
                return false;
            }

            if (token.Type != JTokenType.Float && token.Type != JTokenType.Integer)
            {
                error = fieldName + " must be numeric";
                return false;
            }

            value = token.Value<double>();
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                error = fieldName + " must be finite";
                return false;
            }

            return true;
        }

        internal static bool TryReadPositiveFiniteDouble(
            JObject obj,
            string fieldName,
            out double value,
            out string error)
        {
            if (!TryReadFiniteDouble(obj, fieldName, out value, out error))
            {
                return false;
            }

            if (value <= 0d)
            {
                error = fieldName + " must be a finite positive number";
                return false;
            }

            return true;
        }

        internal static double DegreesToRadians(double degrees)
            => degrees * Math.PI / 180d;
    }
}
