using System;
using Autodesk.AutoCAD.ApplicationServices;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class BatchExecuteHandler : IAcadCommand
    {
        private readonly Func<Document, string, JToken, CommandResult> _executeCommand;

        public BatchExecuteHandler(Func<Document, string, JToken, CommandResult> executeCommand)
        {
            _executeCommand = executeCommand ?? throw new ArgumentNullException(nameof(executeCommand));
        }

        public string Name => "batch_execute";
        public string Description => "Run multiple DWG commands sequentially as a logical batch. AutoCAD undo grouping is not enabled.";
        public CommandSchema Schema => CommandSchemas.BatchExecute;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            return BatchExecutor.Run(parameters, (cmd, itemParams) => _executeCommand(doc, cmd, itemParams));
        }
    }
}
