using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Bimwright.Dwg.Plugin.Cad;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class CopyEntitiesHandler : IAcadCommand
    {
        public string Name => "copy_entities";
        public string Description => "Copy entities identified by handle by a displacement vector.";
        public CommandSchema Schema => CommandSchemas.CopyEntities;

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
                        results.Add(new { handle, ok = false, new_handle = (string)null, error = handleError });
                        continue;
                    }

                    try
                    {
                        if (!TransformEntityHandlerSupport.TryOpenEntity(db, tx, handle, OpenMode.ForRead, out var entity, out var openError))
                        {
                            results.Add(new { handle, ok = false, new_handle = (string)null, error = openError });
                            continue;
                        }

                        var clone = CloneAndAppend(db, tx, entity, matrix);
                        results.Add(new { handle, ok = true, new_handle = clone.Handle.ToString(), error = (string)null });
                    }
                    catch (Exception ex)
                    {
                        results.Add(new { handle, ok = false, new_handle = (string)null, error = ErrorSanitizer.Sanitize(ex.Message) });
                    }
                }

                tx.Commit();
            }

            return CommandResult.Success(new { results });
        }

        private static Entity CloneAndAppend(Database db, Transaction tx, Entity source, Matrix3d matrix)
        {
            Entity clone = null;
            var appended = false;

            try
            {
                clone = source.Clone() as Entity;
                if (clone == null)
                {
                    throw new InvalidOperationException("cloned object is not an entity");
                }

                clone.TransformBy(matrix);
                if (!source.OwnerId.IsNull)
                {
                    var owner = tx.GetObject(source.OwnerId, OpenMode.ForWrite) as BlockTableRecord;
                    if (owner != null)
                    {
                        owner.AppendEntity(clone);
                        tx.AddNewlyCreatedDBObject(clone, true);
                        appended = true;
                        return clone;
                    }
                }

                CadPrimitiveWriter.AppendToCurrentSpace(db, tx, clone);
                appended = true;
                return clone;
            }
            catch
            {
                if (clone != null && !appended)
                {
                    clone.Dispose();
                }

                throw;
            }
        }
    }
}
