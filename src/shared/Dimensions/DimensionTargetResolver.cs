using Autodesk.AutoCAD.DatabaseServices;
using Bimwright.Dwg.Plugin.Cad;

namespace Bimwright.Dwg.Plugin.Dimensions
{
    internal readonly struct RadialDimensionTarget
    {
        internal RadialDimensionTarget(string entityTypeName, CadPointInput center, double radius)
        {
            EntityTypeName = entityTypeName;
            Center = center;
            Radius = radius;
        }

        internal string EntityTypeName { get; }
        internal CadPointInput Center { get; }
        internal double Radius { get; }
    }

    internal static class DimensionTargetResolver
    {
        internal static bool TryResolveRadialTarget(
            Database db,
            Transaction tx,
            string handle,
            out RadialDimensionTarget target,
            out string error)
        {
            target = default;
            error = null;

            if (!CadHandleResolver.TryResolve(db, handle, out var objectId, out error))
            {
                return false;
            }

            var dbObject = tx.GetObject(objectId, OpenMode.ForRead);
            var entityTypeName = dbObject.GetType().Name;
            if (!DimensionRequestValidator.TryValidateRadialTargetType(entityTypeName, out error))
            {
                return false;
            }

            if (dbObject is Circle circle)
            {
                target = new RadialDimensionTarget(
                    entityTypeName,
                    ToCadPoint(circle.Center),
                    circle.Radius);
                return true;
            }

            if (dbObject is Arc arc)
            {
                target = new RadialDimensionTarget(
                    entityTypeName,
                    ToCadPoint(arc.Center),
                    arc.Radius);
                return true;
            }

            error = "entity_handle must resolve to a circle or arc";
            return false;
        }

        private static CadPointInput ToCadPoint(Autodesk.AutoCAD.Geometry.Point3d point)
            => new CadPointInput(point.X, point.Y, point.Z);
    }
}
