using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Bimwright.Dwg.Plugin.Handlers;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    // SendCodeHandler captures process-wide Console.Out; don't run it concurrently
    // with other tests. Execute via Task.Run to match the plugin's no-context thread.
    [CollectionDefinition("Script handlers", DisableParallelization = true)]
    public class ScriptHandlerCollection { }

    [Collection("Script handlers")]
    public class ScriptHandlerTests
    {
        [Theory]
        [InlineData("doc.MutationCount++; await System.Threading.Tasks.Task.Delay(1); return 1;")]
        [InlineData("doc.MutationCount++; System.Func<System.Threading.Tasks.Task> f = async () => {}; return 1;")]
        public async Task Async_scripts_are_rejected_before_any_side_effect(string code)
        {
            var doc = new Document();
            var result = await Task.Run(() => new SendCodeHandler().Execute(doc, JObject.FromObject(new { code })));
            Assert.False(result.Ok);
            Assert.Contains("async", result.Error);
            Assert.Equal(0, doc.MutationCount);
        }

        [Theory]
        [InlineData("return doc;")]
        [InlineData("return new { nested = doc };")]
        [InlineData("return new object[] { doc };")]
        public async Task Host_objects_are_rejected_without_reading_native_properties(string code)
        {
            var doc = new Document();
            var result = await Task.Run(() => new SendCodeHandler().Execute(doc, JObject.FromObject(new { code })));
            var payload = JObject.FromObject(result.Result);
            Assert.False(payload.Value<bool>("ok"));
            Assert.Contains("DTO", payload.Value<string>("error"));
            Assert.Equal(0, doc.NativeGetterReads);
        }

        [Fact]
        public async Task Synchronous_dto_result_and_stdout_are_preserved()
        {
            var code = "// async and await in comments/strings are valid\nConsole.WriteLine(\"hello\"); return new { value = 42, names = new[] { \"async\", \"await\" } };";
            var result = await Task.Run(() => new SendCodeHandler().Execute(new Document(), JObject.FromObject(new { code })));
            var payload = JObject.FromObject(result.Result);
            Assert.True(payload.Value<bool>("ok"), payload.Value<string>("error"));
            Assert.Equal(42, (int)payload["result"]["value"]);
            Assert.Equal("await", (string)payload["result"]["names"][1]);
            Assert.Contains("hello", payload.Value<string>("stdout"));
        }

        [Fact]
        public void Lisp_refusal_does_not_require_a_document_or_echo_input_secrets()
        {
            var result = new RunLispHandler().Execute(null, JObject.FromObject(new
            {
                file = @"C:\private\drawing.lsp",
                code = "password=sample-value token=sample-token"
            }));
            Assert.False(result.Ok);
            var error = result.Error;
            Assert.Contains("LISP execution is blocked", error);
            Assert.DoesNotContain("sample-value", error);
            Assert.DoesNotContain("sample-token", error);
            Assert.DoesNotContain("C:\\private", error);
        }
    }
}
