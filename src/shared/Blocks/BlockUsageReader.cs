using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;

namespace Bimwright.Dwg.Plugin.Blocks
{
    internal static class BlockUsageReader
    {
        internal static IEnumerable<ObjectId> ActiveReferenceIds(BlockTableRecord definition, Transaction transaction)
        {
            foreach (var id in ActiveReferenceIds(definition)) yield return id;
            if (!definition.IsDynamicBlock) yield break;

            using var variants = definition.GetAnonymousBlockIds();
            foreach (ObjectId variantId in variants)
            {
                if (variantId.IsEffectivelyErased) continue;
                var variant = (BlockTableRecord)transaction.GetObject(variantId, OpenMode.ForRead);
                foreach (var id in ActiveReferenceIds(variant)) yield return id;
            }
        }

        private static IEnumerable<ObjectId> ActiveReferenceIds(BlockTableRecord definition)
        {
            // Direct references here include inserts owned by other definitions, but never their ancestors.
            using var ids = definition.GetBlockReferenceIds(directOnly: true, forceValidity: false);
            foreach (ObjectId id in ids)
                if (!id.IsEffectivelyErased) yield return id;
        }
    }
}
