using Bimwright.Dwg.Plugin.ToolBaker;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class ToolBakerRuntimeTests
    {
        [Fact]
        public void ToolCompiler_RejectsFailedPresetSmokeTest()
        {
            var record = new BakedToolRecord
            {
                Name = "dwg_fix_note",
                Description = "Fix note",
                Source = "preset",
                HandlerTool = "update_texts",
                ParamsSchema = "{}",
                FixedArgs = new JObject { ["items"] = new JArray() }.ToString(),
                SourceCode = BakedToolRuntimeSource.BuildPreset("update_texts", new JObject { ["items"] = new JArray() })
            };

            var result = ToolCompiler.CompileAndSmokeTest(
                record,
                (cmd, parameters) => new BakePolicyResult { Ok = false, Error = "missing required field" });

            Assert.False(result.Ok);
            Assert.Contains("smoke test failed", result.Error);
        }

        [Fact]
        public void ToolCompiler_RunsPresetSmokeTestWithFixedArgs()
        {
            string capturedCommand = null;
            JObject capturedParams = null;
            var record = new BakedToolRecord
            {
                Name = "dwg_fix_note",
                Description = "Fix note",
                Source = "preset",
                HandlerTool = "update_texts",
                ParamsSchema = "{}",
                FixedArgs = new JObject { ["items"] = new JArray() }.ToString(),
                SourceCode = BakedToolRuntimeSource.BuildPreset("update_texts", new JObject { ["items"] = new JArray() })
            };

            var result = ToolCompiler.CompileAndSmokeTest(
                record,
                (cmd, parameters) =>
                {
                    capturedCommand = cmd;
                    capturedParams = (JObject)parameters;
                    return new BakePolicyResult { Ok = true };
                });

            Assert.True(result.Ok);
            Assert.Equal("update_texts", capturedCommand);
            Assert.IsType<JArray>(capturedParams["items"]);
        }

        [Theory]
        [InlineData("send_code")]
        [InlineData("batch_execute")]
        [InlineData("run_baked_tool")]
        [InlineData("apply_bake")]
        public void BakedToolDispatchAuthorizer_RejectsUnsafeTargets(string command)
        {
            Assert.False(BakedToolDispatchAuthorizer.IsAllowed(command));
        }

        [Theory]
        [InlineData("update_texts")]
        [InlineData("collapse_and_rewrite")]
        public void BakedToolDispatchAuthorizer_AllowsKnownSafeTargets(string command)
        {
            Assert.True(BakedToolDispatchAuthorizer.IsAllowed(command));
        }

        [Fact]
        public void BakedToolDispatchAuthorizer_RejectsUnknownTargets()
        {
            Assert.False(BakedToolDispatchAuthorizer.IsAllowed("future_raw_tool"));
        }

        [Fact]
        public void BakedToolRuntimeCommandFactory_BuildsPresetRecordFromApplyRequest()
        {
            var request = new JObject
            {
                ["tool_name"] = "dwg_fix_note",
                ["description"] = "Fix note",
                ["source"] = "preset",
                ["handler_tool"] = "update_texts",
                ["fixed_args"] = new JObject { ["apply_unicode_style"] = true },
                ["params_schema"] = new JObject { ["type"] = "object" },
                ["source_code"] = BakedToolRuntimeSource.BuildPreset("update_texts", new JObject { ["apply_unicode_style"] = true })
            };

            var record = BakedToolRuntimeCommandFactory.FromApplyRequest(request);

            Assert.Equal("dwg_fix_note", record.Name);
            Assert.Equal("update_texts", record.HandlerTool);
            Assert.Contains("apply_unicode_style", record.FixedArgs);
        }

        [Fact]
        public void BakeCompilerPolicy_RejectsFileSystemAccess()
        {
            var result = BakeCompilerPolicy.ValidateSource("System.IO.File.Delete(\"x\")");

            Assert.False(result.Ok);
            Assert.Contains("System.IO", result.Error);
        }

        [Theory]
        [InlineData("System.Net.Http.HttpClient")]
        [InlineData("System.Reflection.Assembly.Load")]
        [InlineData("Bimwright.Dwg.Plugin.ToolBaker.ToolCompiler")]
        public void BakeCompilerPolicy_RejectsForbiddenBakeEscapes(string source)
        {
            var result = BakeCompilerPolicy.ValidateSource(source);

            Assert.False(result.Ok);
        }

    }
}
