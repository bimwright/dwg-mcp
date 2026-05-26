using Autodesk.AutoCAD.ApplicationServices;
using Bimwright.Dwg.Plugin.Annotation;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class CreateTableHandler : IAcadCommand
    {
        public string Name => "create_table";
        public string Description => "Create a simple table in the current drawing space.";
        public CommandSchema Schema => CommandSchemas.CreateTable;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!(parameters is JObject obj))
            {
                return CommandResult.Fail("params must be an object");
            }

            if (!AnnotationEntityFactory.TryCreateTable(obj, out var entity, out var error))
            {
                return CommandResult.Fail(error);
            }

            return AnnotationHandlerSupport.AppendWithLayerOption(
                doc,
                obj,
                entity,
                "failed to create table");
        }
    }
}
