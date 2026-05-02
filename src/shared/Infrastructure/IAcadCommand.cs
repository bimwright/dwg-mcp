using Autodesk.AutoCAD.ApplicationServices;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin
{
    public interface IAcadCommand
    {
        string Name { get; }
        CommandResult Execute(Document doc, JToken parameters);
    }
}
