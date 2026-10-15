using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin
{
    /// <summary>Standalone host operations, dispatched on the main thread without a document lock.
    /// Execute(Document, ...) must refuse document/batch/baked-tool contexts.</summary>
    public interface IAcadSessionCommand : IAcadCommand
    {
        CommandResult ExecuteSession(JToken parameters);
    }
}
