using System;
using Autodesk.AutoCAD.DatabaseServices;

namespace Bimwright.Dwg.Plugin.Cad
{
    internal static class CadHandleResolver
    {
        internal static bool TryResolve(Database db, string handle, out ObjectId objectId, out string error)
        {
            objectId = ObjectId.Null;
            error = null;

            if (db == null)
            {
                error = "database is required";
                return false;
            }

            if (!CadWire.TryParseHandleValue(handle, out var value, out error))
            {
                return false;
            }

            try
            {
                objectId = db.GetObjectId(false, new Handle(value), 0);
            }
            catch (Exception)
            {
                objectId = ObjectId.Null;
                error = "handle not found";
                return false;
            }

            if (objectId.IsNull)
            {
                error = "handle not found";
                return false;
            }

            return true;
        }
    }
}
