using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;

namespace Bimwright.Dwg.Plugin.Drawing
{
    internal static class PurgeDrawingService
    {
        internal static PurgeResult Purge(Database db, bool dryRun, bool? confirm, out string error)
        {
            error = null;

            if (!dryRun && confirm != true)
            {
                error = "actual drawing purge requires confirm=true";
                return null;
            }

            var idsToPurge = new ObjectIdCollection();

            using (var tx = db.TransactionManager.StartTransaction())
            {
                // 1. Blocks
                var bt = (BlockTable)tx.GetObject(db.BlockTableId, OpenMode.ForRead);
                foreach (ObjectId id in bt)
                {
                    var btr = (BlockTableRecord)tx.GetObject(id, OpenMode.ForRead);
                    if (!btr.IsLayout && !btr.IsAnonymous)
                    {
                        idsToPurge.Add(id);
                    }
                }

                // 2. Layers
                var lt = (LayerTable)tx.GetObject(db.LayerTableId, OpenMode.ForRead);
                foreach (ObjectId id in lt)
                {
                    var ltr = (LayerTableRecord)tx.GetObject(id, OpenMode.ForRead);
                    if (!ltr.Name.Equals("0", StringComparison.OrdinalIgnoreCase) &&
                        !ltr.Name.Equals("Defpoints", StringComparison.OrdinalIgnoreCase))
                    {
                        idsToPurge.Add(id);
                    }
                }

                // 3. Text Styles
                var tst = (TextStyleTable)tx.GetObject(db.TextStyleTableId, OpenMode.ForRead);
                foreach (ObjectId id in tst)
                {
                    var tstr = (TextStyleTableRecord)tx.GetObject(id, OpenMode.ForRead);
                    if (!tstr.Name.Equals("Standard", StringComparison.OrdinalIgnoreCase))
                    {
                        idsToPurge.Add(id);
                    }
                }

                tx.Commit();
            }

            if (idsToPurge.Count == 0)
            {
                return new PurgeResult(new List<PurgedItemInfo>());
            }

            db.Purge(idsToPurge);

            var items = new List<PurgedItemInfo>();

            if (idsToPurge.Count > 0)
            {
                using (var tx = db.TransactionManager.StartTransaction())
                {
                    foreach (ObjectId id in idsToPurge)
                    {
                        var obj = tx.GetObject(id, dryRun ? OpenMode.ForRead : OpenMode.ForWrite);
                        string name = "";
                        string type = obj.GetType().Name;

                        if (obj is SymbolTableRecord str)
                        {
                            name = str.Name;
                        }

                        items.Add(new PurgedItemInfo(name, type, id.Handle.ToString()));

                        if (!dryRun)
                        {
                            obj.Erase();
                        }
                    }
                    tx.Commit();
                }
            }

            return new PurgeResult(items);
        }
    }

    internal class PurgedItemInfo
    {
        public PurgedItemInfo(string name, string type, string handle)
        {
            Name = name;
            Type = type;
            Handle = handle;
        }

        public string Name { get; }
        public string Type { get; }
        public string Handle { get; }
    }

    internal class PurgeResult
    {
        public PurgeResult(List<PurgedItemInfo> items)
        {
            Items = items;
        }

        public List<PurgedItemInfo> Items { get; }
    }
}
