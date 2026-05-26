using Autodesk.AutoCAD.ApplicationServices;
using Bimwright.Dwg.Plugin.Annotation;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class CreateMTextHandler : IAcadCommand
    {
        public string Name => "create_mtext";
        public string Description => "Create multi-line text in the current drawing space.";
        public CommandSchema Schema => CommandSchemas.CreateMText;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!(parameters is JObject obj))
            {
                return CommandResult.Fail("params must be an object");
            }

            if (!AnnotationEntityFactory.TryCreateMText(obj, out var entity, out var error))
            {
                return CommandResult.Fail(error);
            }

            return AnnotationHandlerSupport.AppendWithEntityOptions(
                doc,
                obj,
                parameters,
                entity,
                "failed to create mtext");
        }
    }
}
