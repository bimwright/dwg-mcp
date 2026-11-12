using System.Text.Json;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.Handlers;
using Bimwright.Dwg.Server.Tools;
using Bimwright.Dwg.Tests.Helpers;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests;

[Collection("Script handlers")]
public sealed class ScriptTaskContractTests
{
    private static Task<(JObject Payload, Document Document)> Run(string code, string contract = "task_v1", bool compileOnly = false, string data = null)
        => Task.Run(() => {
            var doc = new Document();
            var parameters = new JObject { ["code"] = code, ["script_inputs_version"] = 1, ["compile_only"] = compileOnly };
            if (contract != null) parameters["result_contract"] = contract;
            if (data != null) parameters["data_json"] = data;
            var result = new SendCodeHandler().Execute(doc, parameters);
            Assert.True(result.Ok, result.Error);
            return (JObject.FromObject(result.Result), doc);
        });

    [Fact]
    public async Task Pass_returns_its_value_and_reports_passed()
    {
        var (payload, doc) = await Run("doc.MutationCount++; return McpTaskResult.Pass(new { count = 3 });");
        Assert.True(payload.Value<bool>("ok"), payload.Value<string>("error"));
        Assert.Equal(3, payload["result"].Value<int>("count"));
        Assert.Equal("task_v1", payload["_task"].Value<string>("contract"));
        Assert.Equal("passed", payload["_task"].Value<string>("status"));
        Assert.Null(payload["_task"]["error"]);
        Assert.Equal(1, doc.MutationCount);
    }

    [Fact]
    public async Task Fail_turns_ok_false_and_returns_details()
    {
        var (payload, _) = await Run("return McpTaskResult.Fail(\"NO_MATCH\", \"no layer matched\", new { searched = 12 });");
        Assert.False(payload.Value<bool>("ok"));
        Assert.Equal("NO_MATCH: no layer matched", payload.Value<string>("error"));
        Assert.Equal(12, payload["result"].Value<int>("searched"));
        Assert.Equal("failed", payload["_task"].Value<string>("status"));
        Assert.Equal("NO_MATCH", payload["_task"]["error"].Value<string>("code"));
        Assert.Equal("no layer matched", payload["_task"]["error"].Value<string>("message"));
    }

    [Fact]
    public async Task Blank_fail_code_becomes_task_failed_and_long_code_is_truncated()
    {
        var (blank, _) = await Run("return McpTaskResult.Fail(\" \", \"m\");");
        Assert.Equal("TASK_FAILED", blank["_task"]["error"].Value<string>("code"));
        var (longCode, _) = await Run("return McpTaskResult.Fail(new string('X', 300), \"m\");");
        Assert.Equal(128, longCode["_task"]["error"].Value<string>("code").Length);
        Assert.True(longCode["_task"]["error"].Value<bool>("code_truncated"));
    }

    [Theory]
    [InlineData("return 42;")]
    [InlineData("var x = 1;")]
    [InlineData("return new { ok = true };")]
    public async Task Missing_task_result_is_not_success(string code)
    {
        var (payload, _) = await Run(code);
        Assert.False(payload.Value<bool>("ok"));
        Assert.StartsWith("TASK_RESULT_REQUIRED", payload.Value<string>("error"));
        Assert.Equal("not_reported", payload["_task"].Value<string>("status"));
        Assert.Equal("TASK_RESULT_REQUIRED", payload["_task"]["error"].Value<string>("code"));
    }

    [Fact]
    public async Task Runtime_error_keeps_its_own_error_and_reports_not_reported()
    {
        var (payload, _) = await Run("throw new System.InvalidOperationException(\"boom\");");
        Assert.False(payload.Value<bool>("ok"));
        Assert.Contains("boom", payload.Value<string>("error"));
        Assert.Equal("not_reported", payload["_task"].Value<string>("status"));
        Assert.Null(payload["_task"]["error"]);
    }

    [Fact]
    public async Task Compile_error_and_compile_only_keep_their_ok_value()
    {
        var (broken, _) = await Run("return McpTaskResult.Pass(MissingSymbol);");
        Assert.False(broken.Value<bool>("ok"));
        Assert.Contains("CS0103", broken.Value<string>("error"));
        Assert.Equal("not_reported", broken["_task"].Value<string>("status"));

        var (compiled, doc) = await Run("doc.MutationCount++; return McpTaskResult.Pass(1);", compileOnly: true);
        Assert.True(compiled.Value<bool>("ok"), compiled.Value<string>("error"));
        Assert.False(compiled.Value<bool>("executed"));
        Assert.Equal("not_reported", compiled["_task"].Value<string>("status"));
        Assert.Equal(0, doc.MutationCount);
    }

    [Fact]
    public async Task Task_v1_works_with_data()
    {
        var (payload, _) = await Run("return McpTaskResult.Pass(data[\"n\"].Value<int>() * 2);", data: "{\"n\":21}");
        Assert.True(payload.Value<bool>("ok"), payload.Value<string>("error"));
        Assert.Equal(42, payload.Value<int>("result"));
    }

    [Fact]
    public async Task Legacy_payload_has_no_task_field_and_does_not_import_the_type()
    {
        var (payload, _) = await Run("return 42;", contract: null);
        Assert.True(payload.Value<bool>("ok"));
        Assert.Null(payload["_task"]);
        var (explicitLegacy, _) = await Run("return McpTaskResult.Pass(1);", contract: "legacy");
        Assert.False(explicitLegacy.Value<bool>("ok"));
        Assert.Null(explicitLegacy["_task"]);
    }

    [Fact]
    public async Task Unknown_contract_is_refused_before_code()
    {
        var doc = new Document();
        var result = await Task.Run(() => new SendCodeHandler().Execute(doc, new JObject {
            ["code"] = "doc.MutationCount++; return 1;", ["script_inputs_version"] = 1, ["result_contract"] = "task_v2" }));
        Assert.False(result.Ok);
        Assert.Contains("result_contract", result.Error);
        Assert.Equal(0, doc.MutationCount);
    }

    [Fact]
    public void Task_v1_send_code_is_direct_call_only()
    {
        Assert.True(ScriptInputContract.RequiresDirectCall("send_code", new JObject { ["code"] = "x", ["result_contract"] = "task_v1" }));
    }

    [Fact]
    public async Task Gateway_sends_task_v1_and_keeps_usage_context_off_the_wire()
    {
        var sent = await Capture.Send(() => CodeTools.SendCode("return McpTaskResult.Pass(1);", result_contract: "task_v1", usage_context: "project"));
        var wire = sent.Json();
        Assert.Equal("task_v1", wire.Value<string>("result_contract"));
        Assert.Equal(1, wire.Value<int>("script_inputs_version"));
        Assert.Null(wire["usage_context"]);

        var legacy = await Capture.Send(() => CodeTools.SendCode("return 42;", usage_context: "test"));
        Assert.Equal(new[] { "code" }, System.Linq.Enumerable.Select(legacy.Json().Properties(), p => p.Name));
    }

    [Theory]
    [InlineData("task_v2", "unknown", "result_contract")]
    [InlineData("legacy", "prod", "usage_context")]
    public async Task Gateway_refuses_unknown_modes_without_sending(string contract, string usage, string field)
    {
        // Refused before ToolGateway is reached, so no plugin or override is involved.
        var response = JObject.Parse(await CodeTools.SendCode("return 1;", result_contract: contract, usage_context: usage));
        Assert.False(response.Value<bool>("ok"));
        Assert.Contains(field, response.Value<string>("error"));
    }
}
