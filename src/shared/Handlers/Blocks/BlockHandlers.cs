using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Bimwright.Dwg.Plugin.Blocks;
using Bimwright.Dwg.Plugin.Cad;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class ListBlocksHandler : IAcadCommand
    {
        public string Name => "list_blocks";
        public string Description => "List non-anonymous, non-layout block definitions in the current drawing.";
        public CommandSchema Schema => CommandSchemas.ListBlocks;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            var db = doc.Database;
            var blocks = new List<object>();

            using (var tx = db.TransactionManager.StartTransaction())
            {
                var blockTable = (BlockTable)tx.GetObject(db.BlockTableId, OpenMode.ForRead);
                foreach (ObjectId blockId in blockTable)
                {
                    var record = (BlockTableRecord)tx.GetObject(blockId, OpenMode.ForRead);
                    if (record.IsAnonymous || record.IsLayout)
                    {
                        continue;
                    }

                    var counts = CountBlockContents(record, tx);
                    blocks.Add(new
                    {
                        name = record.Name,
                        handle = record.Handle.ToString(),
                        entity_count = counts.EntityCount,
                        attribute_definition_count = counts.AttributeDefinitionCount,
                        has_attribute_definitions = counts.AttributeDefinitionCount > 0
                    });
                }

                tx.Commit();
            }

            return CommandResult.Success(new { blocks });
        }

        private static BlockDefinitionCounts CountBlockContents(BlockTableRecord record, Transaction tx)
        {
            var entityCount = 0;
            var attributeDefinitionCount = 0;

            foreach (ObjectId objectId in record)
            {
                var dbObject = tx.GetObject(objectId, OpenMode.ForRead);
                if (dbObject is AttributeDefinition)
                {
                    attributeDefinitionCount++;
                }
                else if (dbObject is Entity)
                {
                    entityCount++;
                }
            }

            return new BlockDefinitionCounts(entityCount, attributeDefinitionCount);
        }

        private readonly struct BlockDefinitionCounts
        {
            internal BlockDefinitionCounts(int entityCount, int attributeDefinitionCount)
            {
                EntityCount = entityCount;
                AttributeDefinitionCount = attributeDefinitionCount;
            }

            internal int EntityCount { get; }
            internal int AttributeDefinitionCount { get; }
        }
    }

    public class GetBlockAttributesHandler : IAcadCommand
    {
        public string Name => "get_block_attributes";
        public string Description => "Read attributes from a block reference.";
        public CommandSchema Schema => CommandSchemas.GetBlockAttributes;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!(parameters is JObject obj))
            {
                return CommandResult.Fail("params must be an object");
            }

            var handle = obj["handle"]?.Value<string>();
            var db = doc.Database;

            try
            {
                using (var tx = db.TransactionManager.StartTransaction())
                {
                    if (!BlockReferenceResolver.TryOpen(db, tx, handle, OpenMode.ForRead, out var blockReference, out var error))
                    {
                        return CommandResult.Fail(error);
                    }

                    var result = new
                    {
                        handle = blockReference.Handle.ToString(),
                        entity = CadEntityProperties.Describe(blockReference, tx, includeGeometry: true),
                        attributes = BlockAttributeService.ReadAttributes(blockReference, tx)
                    };

                    tx.Commit();
                    return CommandResult.Success(result);
                }
            }
            catch (Exception ex)
            {
                return CommandResult.Fail("failed to get block attributes: " + ErrorSanitizer.Sanitize(ex.Message));
            }
        }
    }

    public class InsertBlockHandler : IAcadCommand
    {
        public string Name => "insert_block";
        public string Description => "Insert a block reference from an existing block definition or absolute DWG path.";
        public CommandSchema Schema => CommandSchemas.InsertBlock;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!(parameters is JObject obj))
            {
                return CommandResult.Fail("params must be an object");
            }

            var blockName = obj["block_name"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(blockName))
            {
                return CommandResult.Fail("block_name must be a non-empty block definition name");
            }

            if (!CadWire.TryParsePoint(obj["insertion_point"], out var insertionPoint, out var pointError))
            {
                return CommandResult.Fail("insertion_point " + pointError);
            }

            if (!BlockHandlerInput.TryReadOptionalPositiveDouble(obj, "scale", 1d, out var scale, out var scaleError))
            {
                return CommandResult.Fail(scaleError);
            }

            if (!BlockHandlerInput.TryReadOptionalFiniteDouble(obj, "rotation", 0d, out var rotation, out var rotationError))
            {
                return CommandResult.Fail(rotationError);
            }

            if (!BlockHandlerInput.TryReadOptionalObject(obj, "attributes", out var attributes, out var attributesError))
            {
                return CommandResult.Fail(attributesError);
            }

            var db = doc.Database;
            var blockPath = obj["block_path"]?.Value<string>();

            try
            {
                using (var tx = db.TransactionManager.StartTransaction())
                {
                    if (!BlockDefinitionResolver.TryResolve(
                        db,
                        tx,
                        blockName,
                        blockPath,
                        out var blockDefinitionId,
                        out var resolvedName,
                        out var imported,
                        out var resolveError))
                    {
                        return CommandResult.Fail(resolveError);
                    }

                    BlockReference blockReference = null;
                    var ownsBlockReference = false;
                    try
                    {
                        blockReference = new BlockReference(BlockHandlerInput.ToPoint3d(insertionPoint), blockDefinitionId)
                        {
                            ScaleFactors = new Scale3d(scale),
                            Rotation = BlockHandlerInput.DegreesToRadians(rotation)
                        };
                        ownsBlockReference = true;

                        CadPrimitiveWriter.AppendToCurrentSpace(db, tx, blockReference);
                        // AutoCAD owns the block reference after AddNewlyCreatedDBObject succeeds.
                        ownsBlockReference = false;

                        var attributeUpdate = BlockAttributeService.AddAttributeReferencesAndApplyValues(
                            tx,
                            blockReference,
                            blockDefinitionId,
                            attributes);

                        var result = new
                        {
                            handle = blockReference.Handle.ToString(),
                            block_name = resolvedName,
                            imported,
                            entity = CadEntityProperties.Describe(blockReference, tx, includeGeometry: true),
                            attribute_update = attributeUpdate.ToWireObject(strictTags: false)
                        };

                        tx.Commit();
                        return CommandResult.Success(result);
                    }
                    finally
                    {
                        if (ownsBlockReference)
                        {
                            blockReference?.Dispose();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return CommandResult.Fail("failed to insert block: " + ErrorSanitizer.Sanitize(ex.Message));
            }
        }
    }

    public class SetBlockAttributesHandler : IAcadCommand
    {
        public string Name => "set_block_attributes";
        public string Description => "Set block reference attributes by tag.";
        public CommandSchema Schema => CommandSchemas.SetBlockAttributes;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!(parameters is JObject obj))
            {
                return CommandResult.Fail("params must be an object");
            }

            var handle = obj["handle"]?.Value<string>();
            if (!BlockHandlerInput.TryReadOptionalObject(obj, "attributes", out var attributes, out var attributesError) ||
                attributes == null)
            {
                return CommandResult.Fail(attributesError ?? "attributes must be a JSON object");
            }

            var strictTags = obj["strict_tags"]?.Value<bool>() ?? false;
            var db = doc.Database;

            try
            {
                using (var tx = db.TransactionManager.StartTransaction())
                {
                    if (!BlockReferenceResolver.TryOpen(db, tx, handle, OpenMode.ForRead, out var blockReference, out var error))
                    {
                        return CommandResult.Fail(error);
                    }

                    var attributeUpdate = BlockAttributeService.SetAttributes(blockReference, tx, attributes, strictTags);
                    var result = new
                    {
                        handle = blockReference.Handle.ToString(),
                        entity = CadEntityProperties.Describe(blockReference, tx, includeGeometry: true),
                        attribute_update = attributeUpdate.ToWireObject(strictTags)
                    };

                    tx.Commit();
                    return CommandResult.Success(result);
                }
            }
            catch (Exception ex)
            {
                return CommandResult.Fail("failed to set block attributes: " + ErrorSanitizer.Sanitize(ex.Message));
            }
        }
    }

    public class ExplodeBlockHandler : IAcadCommand
    {
        public string Name => "explode_block";
        public string Description => "Explode a block reference and erase the original.";
        public CommandSchema Schema => CommandSchemas.ExplodeBlock;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!(parameters is JObject obj))
            {
                return CommandResult.Fail("params must be an object");
            }

            var handle = obj["handle"]?.Value<string>();
            var db = doc.Database;

            try
            {
                using (var tx = db.TransactionManager.StartTransaction())
                {
                    if (!BlockReferenceResolver.TryOpen(db, tx, handle, OpenMode.ForWrite, out var blockReference, out var error))
                    {
                        return CommandResult.Fail(error);
                    }

                    var originalHandle = blockReference.Handle.ToString();
                    var createdHandles = ExplodeIntoSourceOwner(tx, blockReference);
                    blockReference.Erase();

                    tx.Commit();
                    return CommandResult.Success(new
                    {
                        handle = originalHandle,
                        erased = true,
                        created_handles = createdHandles
                    });
                }
            }
            catch (Exception ex)
            {
                return CommandResult.Fail("failed to explode block: " + ErrorSanitizer.Sanitize(ex.Message));
            }
        }

        private static string[] ExplodeIntoSourceOwner(Transaction tx, BlockReference blockReference)
        {
            var explodedObjects = new DBObjectCollection();
            var unappended = new List<DBObject>();
            var entities = new List<Entity>();
            var createdHandles = new List<string>();

            try
            {
                blockReference.Explode(explodedObjects);

                foreach (DBObject explodedObject in explodedObjects)
                {
                    var entity = explodedObject as Entity;
                    if (entity == null)
                    {
                        explodedObject.Dispose();
                        throw new InvalidOperationException("exploded object is not an entity: " + explodedObject.GetType().Name);
                    }

                    entities.Add(entity);
                    unappended.Add(entity);
                }

                if (blockReference.OwnerId.IsNull)
                {
                    throw new InvalidOperationException("block reference owner is not available");
                }

                var owner = tx.GetObject(blockReference.OwnerId, OpenMode.ForWrite) as BlockTableRecord;
                if (owner == null)
                {
                    throw new InvalidOperationException("block reference owner is not a block table record");
                }

                foreach (var entity in entities)
                {
                    owner.AppendEntity(entity);
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
    }

    internal static class BlockHandlerInput
    {
        internal static Point3d ToPoint3d(CadPointInput point)
            => new Point3d(point.X, point.Y, point.Z);

        internal static bool TryReadOptionalObject(
            JObject obj,
            string fieldName,
            out JObject value,
            out string error)
        {
            value = null;
            error = null;

            var token = obj[fieldName];
            if (token == null || token.Type == JTokenType.Null)
            {
                return true;
            }

            value = token as JObject;
            if (value == null)
            {
                error = fieldName + " must be a JSON object";
                return false;
            }

            return true;
        }

        internal static bool TryReadOptionalPositiveDouble(
            JObject obj,
            string fieldName,
            double fallback,
            out double value,
            out string error)
        {
            if (!TryReadOptionalFiniteDouble(obj, fieldName, fallback, out value, out error))
            {
                return false;
            }

            if (obj[fieldName] != null && obj[fieldName].Type != JTokenType.Null && value <= 0d)
            {
                error = fieldName + " must be a finite positive number";
                return false;
            }

            return true;
        }

        internal static bool TryReadOptionalFiniteDouble(
            JObject obj,
            string fieldName,
            double fallback,
            out double value,
            out string error)
        {
            value = fallback;
            error = null;

            var token = obj[fieldName];
            if (token == null || token.Type == JTokenType.Null)
            {
                return true;
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

        internal static double DegreesToRadians(double degrees)
            => degrees * Math.PI / 180d;
    }
}
