using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Bimwright.Dwg.Plugin.Dimensions;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class CreateLinearDimensionHandler : IAcadCommand
    {
        public string Name => "create_linear_dimension";
        public string Description => "Create a linear dimension in the current drawing space.";
        public CommandSchema Schema => CommandSchemas.CreateLinearDimension;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!(parameters is JObject obj))
            {
                return CommandResult.Fail("params must be an object");
            }

            return DimensionHandlerSupport.CreateAndAppend(
                doc,
                obj,
                "failed to create linear dimension",
                (Database db, Transaction tx, ObjectId styleId, out Dimension entity, out string error) =>
                    DimensionEntityFactory.TryCreateLinear(obj, styleId, out entity, out error));
        }
    }

    public class CreateAlignedDimensionHandler : IAcadCommand
    {
        public string Name => "create_aligned_dimension";
        public string Description => "Create an aligned dimension in the current drawing space.";
        public CommandSchema Schema => CommandSchemas.CreateAlignedDimension;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!(parameters is JObject obj))
            {
                return CommandResult.Fail("params must be an object");
            }

            return DimensionHandlerSupport.CreateAndAppend(
                doc,
                obj,
                "failed to create aligned dimension",
                (Database db, Transaction tx, ObjectId styleId, out Dimension entity, out string error) =>
                    DimensionEntityFactory.TryCreateAligned(obj, styleId, out entity, out error));
        }
    }

    public class CreateRadialDimensionHandler : IAcadCommand
    {
        public string Name => "create_radial_dimension";
        public string Description => "Create a radial dimension for a circle or arc.";
        public CommandSchema Schema => CommandSchemas.CreateRadialDimension;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!(parameters is JObject obj))
            {
                return CommandResult.Fail("params must be an object");
            }

            return DimensionHandlerSupport.CreateAndAppend(
                doc,
                obj,
                "failed to create radial dimension",
                (Database db, Transaction tx, ObjectId styleId, out Dimension entity, out string error) =>
                {
                    var handle = obj["entity_handle"]?.Value<string>();
                    if (!DimensionTargetResolver.TryResolveRadialTarget(db, tx, handle, out var target, out error))
                    {
                        entity = null;
                        return false;
                    }

                    return DimensionEntityFactory.TryCreateRadial(obj, styleId, target, out entity, out error);
                });
        }
    }

    public class CreateDiameterDimensionHandler : IAcadCommand
    {
        public string Name => "create_diameter_dimension";
        public string Description => "Create a diameter dimension for a circle or arc.";
        public CommandSchema Schema => CommandSchemas.CreateDiameterDimension;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!(parameters is JObject obj))
            {
                return CommandResult.Fail("params must be an object");
            }

            return DimensionHandlerSupport.CreateAndAppend(
                doc,
                obj,
                "failed to create diameter dimension",
                (Database db, Transaction tx, ObjectId styleId, out Dimension entity, out string error) =>
                {
                    var handle = obj["entity_handle"]?.Value<string>();
                    if (!DimensionTargetResolver.TryResolveRadialTarget(db, tx, handle, out var target, out error))
                    {
                        entity = null;
                        return false;
                    }

                    return DimensionEntityFactory.TryCreateDiameter(obj, styleId, target, out entity, out error);
                });
        }
    }
}
