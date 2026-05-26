using Autodesk.AutoCAD.ApplicationServices;
using Bimwright.Dwg.Pid;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers.Pid
{
    public class PidListSymbolsHandler : IAcadCommand
    {
        public string Name => "pid_list_symbols";
        public string Description => "List P&ID symbols in a category.";
        public CommandSchema Schema => CommandSchemas.ListSymbols;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            if (!(parameters is JObject obj))
            {
                return CommandResult.Fail("params must be an object");
            }

            var category = obj["category"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(category))
            {
                return CommandResult.Fail("category must be a non-empty string");
            }

            var symbols = PidCatalog.GetSymbols(category);
            return CommandResult.Success(new
            {
                category,
                symbols
            });
        }
    }
}
