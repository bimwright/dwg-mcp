using Autodesk.AutoCAD.DatabaseServices;

namespace Bimwright.Dwg.Plugin.Dimensions
{
    internal static class DimensionStyleResolver
    {
        internal static bool TryResolve(
            Database db,
            Transaction tx,
            string styleName,
            bool hasStyleName,
            out ObjectId dimensionStyleId,
            out string error)
        {
            dimensionStyleId = ObjectId.Null;
            error = null;

            if (!hasStyleName)
            {
                dimensionStyleId = db.Dimstyle;
                return true;
            }

            if (string.IsNullOrWhiteSpace(styleName))
            {
                error = "style_name must be a non-empty dimension style name";
                return false;
            }

            var dimensionStyleTable = (DimStyleTable)tx.GetObject(db.DimStyleTableId, OpenMode.ForRead);
            if (!dimensionStyleTable.Has(styleName))
            {
                error = "dimension style not found: " + styleName;
                return false;
            }

            dimensionStyleId = dimensionStyleTable[styleName];
            return true;
        }
    }
}
