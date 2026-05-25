using System;
using Autodesk.AutoCAD.DatabaseServices;

namespace Bimwright.Dwg.Plugin.Cad
{
    internal static class CadPrimitiveWriter
    {
        internal static ObjectId AppendToCurrentSpace(Database db, Transaction tx, Entity entity)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            if (tx == null)
            {
                throw new ArgumentNullException(nameof(tx));
            }

            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            var currentSpace = (BlockTableRecord)tx.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            var objectId = currentSpace.AppendEntity(entity);
            tx.AddNewlyCreatedDBObject(entity, true);
            return objectId;
        }
    }
}
