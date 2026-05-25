using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Bimwright.Dwg.Plugin.Cad;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class CreatePolylineHandler : IAcadCommand
    {
        public string Name => "create_polyline";
        public string Description => "Create a 2D polyline in the current drawing space.";
        public CommandSchema Schema => CommandSchemas.CreatePolyline;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!(parameters is JObject obj))
            {
                return CommandResult.Fail("params must be an object");
            }

            if (!TryParsePoints(obj["points"], out var points, out var pointsError))
            {
                return CommandResult.Fail(pointsError);
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

            var closed = obj["closed"]?.Value<bool>() ?? false;
            var db = doc.Database;
            try
            {
                using (var tx = db.TransactionManager.StartTransaction())
                {
                    if (!CreatePrimitiveInput.TryEnsureLayer(db, tx, layer, hasLayer, colorIndex, out var layerError))
                    {
                        return CommandResult.Fail(layerError);
                    }

                    var polyline = new Polyline();
                    for (var i = 0; i < points.Count; i++)
                    {
                        var point = points[i];
                        polyline.AddVertexAt(i, new Point2d(point.X, point.Y), 0d, 0d, 0d);
                    }
                    polyline.Closed = closed;
                    CreatePrimitiveInput.ApplyEntityOptions(polyline, layer, hasLayer, colorIndex, hasColorIndex);

                    CadPrimitiveWriter.AppendToCurrentSpace(db, tx, polyline);
                    var entity = CadEntityProperties.Describe(polyline, tx, includeGeometry: true);
                    var result = new
                    {
                        handle = polyline.Handle.ToString(),
                        entity
                    };

                    tx.Commit();
                    return CommandResult.Success(result);
                }
            }
            catch (Exception ex)
            {
                return CommandResult.Fail("failed to create polyline: " + ex.Message);
            }
        }

        private static bool TryParsePoints(
            JToken token,
            out List<CadPointInput> points,
            out string error)
        {
            points = null;
            error = null;

            var array = token as JArray;
            if (array == null)
            {
                error = "points must be an array of point objects";
                return false;
            }

            var parsed = new List<CadPointInput>();
            for (var i = 0; i < array.Count; i++)
            {
                if (!CadWire.TryParsePoint(array[i], out var point, out var pointError))
                {
                    error = "points[" + i + "] " + pointError;
                    return false;
                }

                parsed.Add(point);
            }

            if (parsed.Count < 2)
            {
                error = "points must contain at least 2 point objects";
                return false;
            }

            points = parsed;
            return true;
        }
    }
}
