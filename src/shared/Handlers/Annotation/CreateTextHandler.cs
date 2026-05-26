using Autodesk.AutoCAD.ApplicationServices;
using Bimwright.Dwg.Plugin.Annotation;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class CreateTextHandler : IAcadCommand
    {
        public string Name => "create_text";
        public string Description => "Create single-line text in the current drawing space.";
        public CommandSchema Schema => CommandSchemas.CreateText;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!(parameters is JObject obj))
            {
                return CommandResult.Fail("params must be an object");
            }

            if (!AnnotationEntityFactory.TryCreateText(obj, out var entity, out var error))
            {
                return CommandResult.Fail(error);
            }

            return AnnotationHandlerSupport.AppendWithEntityOptions(
                doc,
                obj,
                parameters,
                entity,
                "failed to create text");
        }
    }
}
