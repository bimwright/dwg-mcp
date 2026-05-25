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
                    var appendedObjectIds = new List<ObjectId>();

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

                        var createdHandles = AppendOffsetEntities(tx, curve, offsets, appendedObjectIds);
                        results.Add(new { handle, ok = true, created_handles = createdHandles, error = (string)null });
                    }
                    catch (Exception ex)
                    {
                        var cleanupError = EraseAppendedEntities(tx, appendedObjectIds);
                        var error = ErrorSanitizer.Sanitize(ex.Message);
                        if (!string.IsNullOrEmpty(cleanupError))
                        {
                            error = error + "; cleanup failed: " + cleanupError;
                        }

                        results.Add(new
                        {
                            handle,
                            ok = false,
                            created_handles = Array.Empty<string>(),
                            error
                        });
                    }
                }

                tx.Commit();
            }

            return CommandResult.Success(new { distance, results });
        }

        private static string[] AppendOffsetEntities(
            Transaction tx,
            Curve source,
            DBObjectCollection offsets,
            IList<ObjectId> appendedObjectIds)
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

                if (!TryOpenOwnerForWrite(tx, source, out var target, out var ownerError))
                {
                    throw new InvalidOperationException(ownerError);
                }

                foreach (var entity in entities)
                {
                    var objectId = target.AppendEntity(entity);
                    unappended.Remove(entity);
                    appendedObjectIds.Add(objectId);
                    tx.AddNewlyCreatedDBObject(entity, true);
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

        private static bool TryOpenOwnerForWrite(
            Transaction tx,
            Entity source,
            out BlockTableRecord owner,
            out string error)
        {
            owner = null;
            error = null;

            if (source.OwnerId.IsNull)
            {
                error = "source entity owner is not available";
                return false;
            }

            try
            {
                var dbObject = tx.GetObject(source.OwnerId, OpenMode.ForWrite);
                owner = dbObject as BlockTableRecord;
                if (owner == null)
                {
                    error = "source entity owner is not a block table record: " + dbObject.GetType().Name;
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = "failed to open source entity owner for write: " + ErrorSanitizer.Sanitize(ex.Message);
                return false;
            }
        }

        private static string EraseAppendedEntities(Transaction tx, IEnumerable<ObjectId> appendedObjectIds)
        {
            string cleanupError = null;

            foreach (var objectId in appendedObjectIds)
            {
                if (objectId.IsNull)
                {
                    continue;
                }

                try
                {
                    var dbObject = tx.GetObject(objectId, OpenMode.ForWrite);
                    dbObject.Erase();
                }
                catch (Exception ex)
                {
                    cleanupError = ErrorSanitizer.Sanitize(ex.Message);
                }
            }

            return cleanupError;
        }
    }
}
