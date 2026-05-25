using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;

namespace Bimwright.Dwg.Plugin.Cad
{
    internal static class CadLayerService
    {
        internal static object[] ListLayers(Database db)
        {
            if (db == null)
            {
                throw new ArgumentNullException(nameof(db));
            }

            var layers = new List<object>();
            using (var tx = db.TransactionManager.StartTransaction())
            {
                var layerTable = (LayerTable)tx.GetObject(db.LayerTableId, OpenMode.ForRead);
                foreach (ObjectId layerId in layerTable)
                {
                    var layer = (LayerTableRecord)tx.GetObject(layerId, OpenMode.ForRead);
                    layers.Add(new
                    {
                        name = layer.Name,
                        color_index = (int)layer.Color.ColorIndex,
                        is_locked = layer.IsLocked,
                        is_frozen = layer.IsFrozen,
                        is_off = layer.IsOff
                    });
                }

                tx.Commit();
            }

            return layers.ToArray();
        }

        internal static bool TryEnsureLayer(
            Database db,
            Transaction tx,
            string name,
            int colorIndex,
            out ObjectId layerId,
            out bool created,
            out string error)
        {
            layerId = ObjectId.Null;
            created = false;
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

            if (!TryValidateLayerName(name, out error))
            {
                return false;
            }

            if (colorIndex < 1 || colorIndex > 256)
            {
                error = "colorIndex must be an ACI color index between 1 and 256";
                return false;
            }

            try
            {
                var layerTable = (LayerTable)tx.GetObject(db.LayerTableId, OpenMode.ForRead);
                if (layerTable.Has(name))
                {
                    layerId = layerTable[name];
                    return true;
                }

                layerTable.UpgradeOpen();
                var layer = new LayerTableRecord
                {
                    Name = name,
                    Color = Color.FromColorIndex(ColorMethod.ByAci, (short)colorIndex)
                };

                layerId = layerTable.Add(layer);
                tx.AddNewlyCreatedDBObject(layer, true);
                created = true;
                return true;
            }
            catch (Exception ex)
            {
                layerId = ObjectId.Null;
                created = false;
                error = $"failed to ensure layer: {ex.Message}";
                return false;
            }
        }

        internal static bool TryValidateLayerName(string name, out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(name))
            {
                error = "layer name must be a non-empty AutoCAD symbol name";
                return false;
            }

            var validationMethod = FindValidateSymbolNameMethod();
            if (validationMethod == null)
            {
                error = "AutoCAD symbol name validation API is unavailable";
                return false;
            }

            try
            {
                var result = validationMethod.Invoke(null, new object[] { name, false });
                if (result is bool && !(bool)result)
                {
                    error = "layer name is not a valid AutoCAD symbol name";
                    return false;
                }
            }
            catch (TargetInvocationException ex)
            {
                error = $"layer name is not a valid AutoCAD symbol name: {GetExceptionMessage(ex.InnerException)}";
                return false;
            }
            catch (Exception ex)
            {
                error = $"AutoCAD symbol name validation failed: {ex.Message}";
                return false;
            }

            return true;
        }

        private static MethodInfo FindValidateSymbolNameMethod()
        {
            const string typeName = "Autodesk.AutoCAD.Internal.SymbolUtilityServices";
            var type = Type.GetType(typeName + ", acdbmgd", false);
            if (type == null)
            {
                type = AppDomain.CurrentDomain
                    .GetAssemblies()
                    .Select(assembly => assembly.GetType(typeName, false))
                    .FirstOrDefault(candidate => candidate != null);
            }

            return type?.GetMethod(
                "ValidateSymbolName",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { typeof(string), typeof(bool) },
                null);
        }

        private static string GetExceptionMessage(Exception ex)
        {
            return string.IsNullOrWhiteSpace(ex?.Message)
                ? "invalid name"
                : ex.Message;
        }
    }
}
