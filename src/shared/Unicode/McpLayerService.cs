using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;

namespace Bimwright.Dwg.Plugin
{
    internal static class McpLayerService
    {
        internal const string LayerName = "layer-bimwright-dwg";

        internal static string EnsureWhiteLayer(Database db)
        {
            using (var tx = db.TransactionManager.StartTransaction())
            {
                var layerTable = (LayerTable)tx.GetObject(db.LayerTableId, OpenMode.ForWrite);
                ObjectId layerId;
                if (layerTable.Has(LayerName))
                {
                    layerId = layerTable[LayerName];
                    var existing = (LayerTableRecord)tx.GetObject(layerId, OpenMode.ForWrite);
                    existing.Color = Color.FromColorIndex(ColorMethod.ByAci, 7);
                    existing.IsFrozen = false;
                    existing.IsLocked = false;
                }
                else
                {
                    var layer = new LayerTableRecord
                    {
                        Name = LayerName,
                        Color = Color.FromColorIndex(ColorMethod.ByAci, 7),
                        IsFrozen = false,
                        IsLocked = false
                    };
                    layerId = layerTable.Add(layer);
                    tx.AddNewlyCreatedDBObject(layer, true);
                }

                tx.Commit();
            }

            return LayerName;
        }

        internal static void ApplyToObject(DBObject obj, string layerName)
        {
            if (string.IsNullOrWhiteSpace(layerName))
            {
                return;
            }

            if (obj is Entity ent)
            {
                ent.Layer = layerName;
            }
        }
    }
}
