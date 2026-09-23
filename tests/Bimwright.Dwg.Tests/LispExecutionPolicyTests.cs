using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Bimwright.Dwg.Plugin.Handlers;
using Bimwright.Dwg.Server;
using Bimwright.Dwg.Server.Tools;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    // Isolate ServerState from concurrent tests. An invalid target ensures an
    // accidental dispatch cannot discover or connect to a real AutoCAD process.
    [Collection("Script handlers")]
    public class LispExecutionPolicyTests
    {
        [Theory]
        [InlineData(null, "(+ 1 2)", null)]
        [InlineData(null, "(load \"helpers.lsp\")", null)]
        [InlineData(null, "(eval (read expr))", null)]
        [InlineData(null, "(vlax-create-object \"AutoCAD.Application\")", null)]
        [InlineData(null, null, "SHELL notepad.exe")]
        [InlineData(null, null, "MYCMD")]
        [InlineData("never-read.fas", null, null)]
        [InlineData("never-read.vlx", null, null)]
        [InlineData("invalid\0path.lsp", null, null)]
        [InlineData("never-read.lsp", "(+ 1 2)", "MYCMD")]
        [InlineData(null, null, null)]
        public async Task Server_refuses_every_source_without_discovery_or_file_access(string file, string code, string command)
        {
            var previous = ServerState.Config;
            try
            {
                ServerState.Config = new DwgMcpConfig { Target = "blocked-test-target" };
                var response = JObject.Parse(await CodeTools.RunLisp(file, code, command));
                Assert.False((bool)response["ok"]);
                Assert.True((bool)response["blocked"]);
                Assert.Equal("lisp_execution_blocked", (string)response["error_code"]);
                Assert.False((bool)response["execution_authorized"]);
                Assert.Contains("dwg_inspect_lisp", (string)response["error"]);
                Assert.DoesNotContain("never-read", (string)response["error"]);
            }
            finally { ServerState.Config = previous; }
        }

        [Theory]
        [InlineData("{\"code\":\"(+ 1 2)\"}")]
        [InlineData("{\"file\":\"never-read.lsp\",\"command\":\"MYCMD\"}")]
        [InlineData("{\"code\":\"(eval (read expr))\",\"trusted\":true,\"override\":true}")]
        public void Plugin_handler_never_queues_work_even_for_direct_wire_callers(string json)
        {
            var queued = 0;
            var doc = new Document { OnSendString = _ => { queued++; throw new System.InvalidOperationException("must not queue"); } };
            var response = new RunLispHandler().Execute(doc, JObject.Parse(json));
            Assert.Equal(0, queued);
            Assert.False(response.Ok);
            Assert.Null(response.Result);
            Assert.Contains("LISP execution is blocked", response.Error);
            Assert.Equal(0, doc.NativeGetterReads);
        }
    }
}
