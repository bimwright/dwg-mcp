using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Bimwright.Dwg.Plugin.Cad;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class ScaleEntitiesHandler : IAcadCommand
    {
        public string Name => "scale_entities";
        public string Description => "Scale entities identified by handle around a base point.";
        public CommandSchema Schema => CommandSchemas.ScaleEntities;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!(parameters is JObject obj))
            {
                return CommandResult.Fail("params must be an object");
            }

            if (!TransformEntityHandlerSupport.TryReadHandles(parameters, out var handles, out var handlesError))
            {
                return CommandResult.Fail(handlesError);
            }

            if (!CadWire.TryParsePoint(obj["basePoint"], out var basePoint, out var pointError))
            {
                return CommandResult.Fail("basePoint " + pointError);
            }

            var factor = obj["scale"]?.Value<double>() ?? 0d;
            if (!CadTransformService.TryReadScale(factor, out var scale, out var scaleError))
            {
                return CommandResult.Fail(scaleError);
            }

            var matrix = Matrix3d.Scaling(scale, TransformEntityHandlerSupport.ToPoint3d(basePoint));
            var db = doc.Database;
            var results = new List<object>();

            using (var tx = db.TransactionManager.StartTransaction())
            {
                foreach (var handleToken in handles)
                {
                    if (!TransformEntityHandlerSupport.TryReadHandle(handleToken, out var handle, out var handleError))
                    {
                        results.Add(new { handle, ok = false, error = handleError });
                        continue;
                    }

                    try
                    {
                        if (!TransformEntityHandlerSupport.TryOpenEntity(db, tx, handle, OpenMode.ForWrite, out var entity, out var openError))
                        {
                            results.Add(new { handle, ok = false, error = openError });
                            continue;
                        }

                        entity.TransformBy(matrix);
                        results.Add(new { handle, ok = true, error = (string)null });
                    }
                    catch (Exception ex)
                    {
                        results.Add(new { handle, ok = false, error = ErrorSanitizer.Sanitize(ex.Message) });
                    }
                }

                tx.Commit();
            }

            return CommandResult.Success(new { results });
        }
    }
}
