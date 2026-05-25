using Autodesk.AutoCAD.ApplicationServices;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin
{
    public interface IAcadCommand
    {
        string Name { get; }
        string Description { get; }
        CommandSchema Schema { get; }
        CommandResult Execute(Document doc, JToken parameters);
    }
}
