using System;
using System.IO;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.Handlers;
using Newtonsoft.Json;
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
        [Fact]
        public async Task Large_real_script_stdout_is_preserved_before_plugin_truncation_without_reexecution()
        {
            using var scope = new OutputArtifactTestScope();
            var doc = new Document();
            var result = await Task.Run(() => new SendCodeHandler().Execute(doc,
                JObject.FromObject(new { code = "doc.MutationCount++; Console.Write(new string('中', 4 * 1024 * 1024)); return new { done = true };" })));
            Assert.True(result.Ok); Assert.Equal(1, doc.MutationCount);
            var delivered = JObject.FromObject(ResponseSizeGuard.ApplyResult(result.Result));
            Assert.True(delivered.Value<bool>("truncated")); Assert.True(delivered.Value<bool>("ok"));
            var artifact = delivered["output_artifact"]; Assert.True(artifact.Value<bool>("complete"));
            var full = JObject.Parse(File.ReadAllText(artifact.Value<string>("path")));
            Assert.Equal(new string('中', 4 * 1024 * 1024), full.Value<string>("stdout"));
            Assert.True(full["result"].Value<bool>("done")); Assert.Equal(1, doc.MutationCount);
            Assert.Equal(new FileInfo(artifact.Value<string>("path")).Length, artifact.Value<long>("bytes"));
        }
        [Theory]
        [InlineData("doc.MutationCount++;")]
        [InlineData("await System.Threading.Tasks.Task.Delay(10); doc.MutationCount++;")]
        public async Task Load_directives_are_rejected_before_root_or_loaded_side_effects(string loadedCode)
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csx");
            try
            {
                File.WriteAllText(path, loadedCode);
                var code = "#load " + JsonConvert.SerializeObject(path) + "\ndoc.MutationCount++;";
                var doc = new Document();
                var result = await Task.Run(() => new SendCodeHandler().Execute(doc, JObject.FromObject(new { code })));
                Assert.Equal(0, doc.MutationCount);
                Assert.False(result.Ok);
                Assert.Contains("#load", result.Error);
            }
            finally { File.Delete(path); }
        }

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
            var code = "// async, await and #load in comments/strings are valid\nConsole.WriteLine(\"hello #load\"); return new { value = 42, names = new[] { \"async\", \"await\" } };";
            var result = await Task.Run(() => new SendCodeHandler().Execute(new Document(), JObject.FromObject(new { code })));
            var payload = JObject.FromObject(result.Result);
            Assert.True(payload.Value<bool>("ok"), payload.Value<string>("error"));
            Assert.Equal(42, (int)payload["result"]["value"]);
            Assert.Equal("await", (string)payload["result"]["names"][1]);
            Assert.Contains("hello", payload.Value<string>("stdout"));
        }

        [Theory]
        [InlineData("Console.WriteLine(\"not executed\"); return MissingBatchSymbol;", "CS0103", "")]
        [InlineData("Console.WriteLine(\"before runtime failure\"); throw new InvalidOperationException(\"batch runtime failure\");", "batch runtime failure", "before runtime failure")]
        public async Task Batch_reports_script_failures_and_preserves_details(string code, string expectedError, string expectedStdout)
        {
            var doc = new Document();
            var continued = false;
            var commands = JObject.FromObject(new
            {
                commands = new[]
                {
                    new { cmd = "send_code", @params = JObject.FromObject(new { code }) },
                    new { cmd = "count_entities", @params = new JObject() }
                }
            });
            var result = await Task.Run(() => BatchExecutor.Run(commands, (cmd, parameters) =>
            {
                if (cmd == "send_code") return new SendCodeHandler().Execute(doc, parameters);
                continued = true;
                return CommandResult.Success(new { count = 4 });
            }));

            Assert.True(result.Ok);
            Assert.True(continued);
            var batch = JObject.FromObject(result.Result);
            Assert.True(batch.Value<bool>("partial_failure"));
            var failed = batch["results"][0];
            Assert.False(failed.Value<bool>("ok"));
            Assert.Contains(expectedError, failed.Value<string>("error"));
            Assert.False(failed["result"].Value<bool>("ok"));
            Assert.Contains(expectedError, failed["result"].Value<string>("error"));
            Assert.Equal(expectedStdout, failed["result"].Value<string>("stdout").Trim());
            Assert.True(batch["results"][1].Value<bool>("ok"));
            Assert.Equal(4, batch["results"][1]["result"].Value<int>("count"));
        }

        [Fact]
        public async Task Batch_keeps_successful_script_stdout_and_user_dto_status()
        {
            var commands = JObject.FromObject(new
            {
                commands = new[] { new { cmd = "send_code", @params = new { code = "Console.WriteLine(\"batch ok\"); return new { ok = false, value = 42 };" } } }
            });
            var result = await Task.Run(() => BatchExecutor.Run(commands,
                (cmd, parameters) => new SendCodeHandler().Execute(new Document(), parameters)));

            Assert.True(result.Ok);
            var batch = JObject.FromObject(result.Result);
            Assert.False(batch.Value<bool>("partial_failure"));
            var item = batch["results"][0];
            Assert.True(item.Value<bool>("ok"));
            Assert.True(item["result"].Value<bool>("ok"));
            Assert.False(item["result"]["result"].Value<bool>("ok"));
            Assert.Equal(42, item["result"]["result"].Value<int>("value"));
            Assert.Contains("batch ok", item["result"].Value<string>("stdout"));
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
