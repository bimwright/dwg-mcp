using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class OffsetEntitiesHandler : IAcadCommand
    {
        public string Name => "offset_entities";
        public string Description => "Offset curve entities identified by handle.";
        public CommandSchema Schema => CommandSchemas.OffsetEntities;

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

            var distance = obj["distance"]?.Value<double>() ?? 0d;
            if (distance == 0d || double.IsNaN(distance) || double.IsInfinity(distance))
            {
                return CommandResult.Fail("distance must be finite and non-zero");
            }

            var db = doc.Database;
            var results = new List<object>();

            using (var tx = db.TransactionManager.StartTransaction())
            {
                foreach (var handleToken in handles)
                {
                    if (!TransformEntityHandlerSupport.TryReadHandle(handleToken, out var handle, out var handleError))
                    {
                        results.Add(new { handle, ok = false, created_handles = Array.Empty<string>(), error = handleError });
                        continue;
                    }

                    try
                    {
                        if (!TransformEntityHandlerSupport.TryOpenEntity(db, tx, handle, OpenMode.ForRead, out var entity, out var openError))
                        {
                            results.Add(new { handle, ok = false, created_handles = Array.Empty<string>(), error = openError });
                            continue;
                        }

                        var curve = entity as Curve;
                        if (curve == null)
                        {
                            results.Add(new
                            {
                                handle,
                                ok = false,
                                created_handles = Array.Empty<string>(),
                                error = "entity is not a curve: " + entity.GetType().Name
                            });
                            continue;
                        }

                        var offsets = curve.GetOffsetCurves(distance);
                        if (offsets == null || offsets.Count == 0)
                        {
                            results.Add(new
                            {
                                handle,
                                ok = false,
                                created_handles = Array.Empty<string>(),
                                error = "offset produced no entities"
                            });
                            continue;
                        }

                        var createdHandles = AppendOffsetEntities(db, tx, curve, offsets);
                        results.Add(new { handle, ok = true, created_handles = createdHandles, error = (string)null });
                    }
                    catch (Exception ex)
                    {
                        results.Add(new
                        {
                            handle,
                            ok = false,
                            created_handles = Array.Empty<string>(),
                            error = ErrorSanitizer.Sanitize(ex.Message)
                        });
                    }
                }

                tx.Commit();
            }

            return CommandResult.Success(new { distance, results });
        }

        private static string[] AppendOffsetEntities(
            Database db,
            Transaction tx,
            Curve source,
            DBObjectCollection offsets)
        {
            var entities = new List<Entity>();
            var unappended = new List<DBObject>();
            var createdHandles = new List<string>();

            try
            {
                foreach (DBObject offsetObject in offsets)
                {
                    var entity = offsetObject as Entity;
                    if (entity == null)
                    {
                        offsetObject.Dispose();
                        throw new InvalidOperationException("offset object is not an entity: " + offsetObject.GetType().Name);
                    }

                    entities.Add(entity);
                    unappended.Add(entity);
                }

                var target = TryOpenOwnerForWrite(tx, source) ??
                    (BlockTableRecord)tx.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                foreach (var entity in entities)
                {
                    target.AppendEntity(entity);
                    tx.AddNewlyCreatedDBObject(entity, true);
                    unappended.Remove(entity);
                    createdHandles.Add(entity.Handle.ToString());
                }

                return createdHandles.ToArray();
            }
            catch
            {
                foreach (var dbObject in unappended)
                {
                    dbObject.Dispose();
                }

                throw;
            }
        }

        private static BlockTableRecord TryOpenOwnerForWrite(Transaction tx, Entity source)
        {
            if (source.OwnerId.IsNull)
            {
                return null;
            }

            try
            {
                return tx.GetObject(source.OwnerId, OpenMode.ForWrite) as BlockTableRecord;
            }
            catch
            {
                return null;
            }
        }
    }
}
