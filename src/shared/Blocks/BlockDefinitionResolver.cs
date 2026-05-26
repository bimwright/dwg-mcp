using System;
using System.IO;
using Autodesk.AutoCAD.DatabaseServices;

namespace Bimwright.Dwg.Plugin.Blocks
{
    internal static class BlockDefinitionResolver
    {
        internal static bool TryResolve(
            Database db,
            Transaction tx,
            string blockName,
            string blockPath,
            out ObjectId blockDefinitionId,
            out string resolvedName,
            out bool imported,
            out string error)
        {
            blockDefinitionId = ObjectId.Null;
            resolvedName = null;
            imported = false;
            error = null;

            if (db == null)
            {
                error = "database is required";
                return false;
            }

            if (tx == null)
            {
                error = "transaction is required";
                return false;
            }

            if (string.IsNullOrWhiteSpace(blockName))
            {
                error = "block_name must be a non-empty block definition name";
                return false;
            }

            if (TryFindExisting(db, tx, blockName, out blockDefinitionId, out resolvedName, out error))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(error))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(blockPath))
            {
                error = "block definition not found: " + blockName;
                return false;
            }

            if (!Path.IsPathRooted(blockPath))
            {
                error = "block_path must be an absolute existing DWG path";
                return false;
            }

            if (!File.Exists(blockPath))
            {
                error = "block_path does not exist";
                return false;
            }

            try
            {
                using (var sourceDatabase = new Database(false, true))
                {
                    sourceDatabase.ReadDwgFile(blockPath, FileShare.Read, true, null);
                    blockDefinitionId = db.Insert(blockName, sourceDatabase, false);
                }

                if (blockDefinitionId.IsNull)
                {
                    error = "failed to import block definition: " + blockName;
                    return false;
                }

                resolvedName = blockName;
                imported = true;
                return true;
            }
            catch (Exception ex)
            {
                blockDefinitionId = ObjectId.Null;
                resolvedName = null;
                imported = false;
                error = "failed to import block definition: " + ex.Message;
                return false;
            }
        }

        internal static bool TryFindExisting(
            Database db,
            Transaction tx,
            string blockName,
            out ObjectId blockDefinitionId,
            out string resolvedName,
            out string error)
        {
            blockDefinitionId = ObjectId.Null;
            resolvedName = null;
            error = null;

            if (db == null)
            {
                error = "database is required";
                return false;
            }

            if (tx == null)
            {
                error = "transaction is required";
                return false;
            }

            if (string.IsNullOrWhiteSpace(blockName))
            {
                error = "block_name must be a non-empty block definition name";
                return false;
            }

            var blockTable = (BlockTable)tx.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (!blockTable.Has(blockName))
            {
                return false;
            }

            var candidateId = blockTable[blockName];
            var record = (BlockTableRecord)tx.GetObject(candidateId, OpenMode.ForRead);
            if (record.IsAnonymous || record.IsLayout)
            {
                error = "block_name refers to an anonymous or layout block record";
                return false;
            }

            blockDefinitionId = candidateId;
            resolvedName = record.Name;
            return true;
        }
    }
}
