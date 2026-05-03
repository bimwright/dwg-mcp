using System.ComponentModel;
using System.Threading.Tasks;
using ModelContextProtocol.Server;

namespace Bimwright.Dwg.Server
{
    [McpServerToolType]
    public class CodeTools
    {
        [McpServerTool, Description(
            "OPT-IN ONLY. Execute a C# snippet against the AutoCAD .NET API as an escape hatch. " +
            "Requires starting the server with --enable-send-code or BIMWRIGHT_DWG_ENABLE_SEND_CODE=1, " +
            "and enabling code execution inside AutoCAD with MCPENABLECODE. " +
            "WARNING: send_code runs arbitrary code with full access to the AutoCAD process " +
            "and local filesystem. Only use with trusted agents. " +
            "Globals available: Document doc, Database db, Editor ed. " +
            "Use System.Console.WriteLine for output. Execution has cooperative 30s cancellation.")]
        public static Task<string> SendCode(
            [Description("C# code to execute")] string code)
            => Tools.LoggedCall("send_code", new { code }, new { code });
    }
}
