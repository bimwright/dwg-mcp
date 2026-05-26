using Autodesk.AutoCAD.ApplicationServices;
using Bimwright.Dwg.Pid;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers.Pid
{
    public class PidListCategoriesHandler : IAcadCommand
    {
        public string Name => "pid_list_categories";
        public string Description => "List standard P&ID symbol categories.";
        public CommandSchema Schema => CommandSchemas.ListCategories;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            var categories = PidCatalog.GetCategories();
            return CommandResult.Success(new
            {
                categories
            });
        }
    }
}
