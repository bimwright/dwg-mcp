using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Bimwright.Dwg.Plugin.Blocks;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class ListBlocksHandler : IAcadCommand
    {
        public string Name => "list_blocks";
        public string Description => "List non-anonymous, non-layout block definitions and their reference counts.";
        public CommandSchema Schema => CommandSchemas.ListBlocks;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            var db = doc.Database;
            var blocks = new List<object>();
            var layoutOwners = new Dictionary<ObjectId, bool>();
            using var tx = db.TransactionManager.StartTransaction();
            var blockTable = (BlockTable)tx.GetObject(db.BlockTableId, OpenMode.ForRead);
            foreach (ObjectId blockId in blockTable)
            {
                if (blockId.IsErased) continue;
                var record = (BlockTableRecord)tx.GetObject(blockId, OpenMode.ForRead);
                if (record.IsAnonymous || record.IsLayout) continue;

                var (entityCount, attributeCount) = CountBlockContents(record, tx);
                var (direct, nested) = CountReferences(record, tx, layoutOwners);
                blocks.Add(new
                {
                    name = record.Name,
                    handle = record.Handle.ToString(),
                    entity_count = entityCount,
                    attribute_definition_count = attributeCount,
                    has_attribute_definitions = attributeCount > 0,
                    reference_count = direct,
                    nested_reference_count = nested,
                    unused = direct.HasValue ? (bool?)(direct == 0 && nested == 0) : null
                });
            }
            tx.Commit();
            return CommandResult.Success(new { blocks });
        }

        private static (int Entities, int Attributes) CountBlockContents(BlockTableRecord record, Transaction tx)
        {
            var entities = 0;
            var attributes = 0;
            foreach (ObjectId id in record)
            {
                if (id.IsErased) continue;
                var obj = tx.GetObject(id, OpenMode.ForRead);
                if (obj is AttributeDefinition) attributes++;
                else if (obj is Entity) entities++;
            }
            return (entities, attributes);
        }

        private static (long? Direct, long? Nested) CountReferences(BlockTableRecord record, Transaction tx,
            Dictionary<ObjectId, bool> layoutOwners)
        {
            long direct = 0, nested = 0;
            try
            {
                foreach (var id in BlockUsageReader.ActiveReferenceIds(record, tx).Distinct())
                {
                    var reference = (BlockReference)tx.GetObject(id, OpenMode.ForRead);
                    var owner = reference.OwnerId;
                    if (!layoutOwners.TryGetValue(owner, out var isLayout))
                    {
                        isLayout = ((BlockTableRecord)tx.GetObject(owner, OpenMode.ForRead)).IsLayout;
                        layoutOwners.Add(owner, isLayout);
                    }
                    if (isLayout) direct++; else nested++;
                }
                return (direct, nested);
            }
            catch { return (null, null); }
        }
    }
}
