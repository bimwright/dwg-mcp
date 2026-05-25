using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Bimwright.Dwg.Plugin.Cad;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class MoveEntitiesHandler : IAcadCommand
    {
        public string Name => "move_entities";
        public string Description => "Move entities identified by handle by a displacement vector.";
        public CommandSchema Schema => CommandSchemas.MoveEntities;

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

            if (!CadTransformService.TryParseVector(obj["vector"], out var vector, out var vectorError))
            {
                return CommandResult.Fail(vectorError);
            }

            var matrix = Matrix3d.Displacement(TransformEntityHandlerSupport.ToVector3d(vector));
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

    internal static class TransformEntityHandlerSupport
    {
        internal static bool TryReadHandles(JToken parameters, out JArray handles, out string error)
        {
            handles = (parameters as JObject)?["handles"] as JArray;
            error = null;

            if (handles == null)
            {
                error = "handles must be an array";
                return false;
            }

            return true;
        }

        internal static bool TryReadHandle(JToken token, out string handle, out string error)
        {
            handle = null;
            error = null;

            if (token == null || token.Type == JTokenType.Null)
            {
                error = "handle must be a string";
                return false;
            }

            if (token.Type != JTokenType.String)
            {
                handle = token.ToString(Formatting.None);
                error = "handle must be a string";
                return false;
            }

            handle = token.Value<string>();
            if (string.IsNullOrWhiteSpace(handle))
            {
                error = "handle must be a non-empty hexadecimal string";
                return false;
            }

            return true;
        }

        internal static bool TryOpenEntity(
            Database db,
            Transaction tx,
            string handle,
            OpenMode openMode,
            out Entity entity,
            out string error)
        {
            entity = null;
            error = null;

            if (!CadHandleResolver.TryResolve(db, handle, out var objectId, out error))
            {
                return false;
            }

            var dbObject = tx.GetObject(objectId, openMode);
            entity = dbObject as Entity;
            if (entity == null)
            {
                error = "object is not an entity: " + dbObject.GetType().Name;
                return false;
            }

            return true;
        }

        internal static Point3d ToPoint3d(CadPointInput point)
            => new Point3d(point.X, point.Y, point.Z);

        internal static Vector3d ToVector3d(CadPointInput vector)
            => new Vector3d(vector.X, vector.Y, vector.Z);
    }
}
