using Autodesk.AutoCAD.ApplicationServices;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    /// <summary>
    /// Compatibility refusal for older servers and direct wire callers. Do not
    /// load files, queue command-line work or consult scanner verdicts here.
    /// </summary>
    public class RunLispHandler : IAcadCommand
    {
        public string Name => "run_lisp";
        public string Description => "LISP execution is blocked pending an enforceable isolation/trust policy.";
        public CommandSchema Schema => CommandSchemas.RunLisp;

        public CommandResult Execute(Document doc, JToken parameters)
            => CommandResult.Fail(LispExecutionPolicy.Refusal);
    }
}
