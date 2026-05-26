using Autodesk.AutoCAD.DatabaseServices;
using Bimwright.Dwg.Plugin.Cad;

namespace Bimwright.Dwg.Plugin.Blocks
{
    internal static class BlockReferenceResolver
    {
        internal static bool TryOpen(
            Database db,
            Transaction tx,
            string handle,
            OpenMode openMode,
            out BlockReference blockReference,
            out string error)
        {
            blockReference = null;
            error = null;

            if (!CadHandleResolver.TryResolve(db, handle, out var objectId, out error))
            {
                return false;
            }

            var dbObject = tx.GetObject(objectId, openMode);
            blockReference = dbObject as BlockReference;
            if (blockReference == null)
            {
                error = "object is not a block reference: " + dbObject.GetType().Name;
                return false;
            }

            return true;
        }
    }
}
