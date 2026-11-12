using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
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
        public string Description => "Run multiple DWG commands sequentially. atomic=true wraps supported commands in one AutoCAD transaction.";
        public CommandSchema Schema => CommandSchemas.BatchExecute;

        public CommandResult Execute(Document doc, JToken parameters)
        {
            return BatchExecutor.Run(parameters, (cmd, itemParams) => _executeCommand(doc, cmd, itemParams),
                () => new OuterTransaction(doc.Database.TransactionManager.StartTransaction()));
        }

        // Handlers open nested db.TransactionManager transactions; their commits land in this one.
        private sealed class OuterTransaction : IBatchTransaction
        {
            private readonly Transaction _transaction;
            internal OuterTransaction(Transaction transaction) => _transaction = transaction;
            public void Commit() => _transaction.Commit();
            public void Abort() => _transaction.Abort();
            public void Dispose() => _transaction.Dispose();
        }
    }
}
