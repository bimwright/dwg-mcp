using Autodesk.AutoCAD.ApplicationServices;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class ListBakedToolsHandler : IAcadCommand
    {
        public string Name => "list_baked_tools";
        public string Description => "List baked DWG tools installed in the plugin registry.";
        public CommandSchema Schema => CommandSchema.Empty;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            return CommandResult.Fail("baked tools are listed from the server registry; use dwg_list_baked_tools");
        }
    }
}
