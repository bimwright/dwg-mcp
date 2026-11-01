using System;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Bimwright.Dwg.Plugin.Handlers;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests;

[Collection("Script handlers")]
public sealed class ScriptInputTests
{
    private static Task<(JObject Payload, Document Document)> Run(string code, string data = null, bool compileOnly = false)
        => Task.Run(() => {
            var doc = new Document();
            var parameters = new JObject { ["code"] = code, ["script_inputs_version"] = 1, ["compile_only"] = compileOnly };
            if (data != null) parameters["data_json"] = data;
            var result = new SendCodeHandler().Execute(doc, parameters);
            Assert.True(result.Ok, result.Error);
            return (JObject.FromObject(result.Result), doc);
        });

    [Fact]
    public async Task Data_is_separate_from_code_and_date_like_values_remain_strings()
    {
        var (payload, _) = await Run("return new { count = (int)data[\"count\"], date = (string)data[\"date\"] };",
            "{\"count\":42,\"date\":\"2026-11-01T00:00:00Z\"}");
        Assert.True(payload.Value<bool>("ok"), payload.Value<string>("error"));
        Assert.Equal(42, payload["result"].Value<int>("count"));
        Assert.Equal("2026-11-01T00:00:00Z", payload["result"].Value<string>("date"));
    }

    [Theory]
    [InlineData("[1,2,3]")]
    [InlineData("\"plain string\"")]
    [InlineData("3.5")]
    [InlineData("true")]
    public async Task Data_can_be_any_json_value(string json)
    {
        var (payload, _) = await Run("return data;", json);
        Assert.True(payload.Value<bool>("ok"), payload.Value<string>("error"));
        Assert.True(JToken.DeepEquals(JToken.Parse(json), payload["result"]));
    }

    [Fact]
    public async Task Compile_only_does_not_run_mutation_stdout_or_throw()
    {
        var (payload, doc) = await Run("doc.MutationCount++; Console.WriteLine(\"not-run\"); throw new InvalidOperationException(\"not-run\");", compileOnly: true);
        Assert.True(payload.Value<bool>("ok"), payload.Value<string>("error"));
        Assert.True(payload.Value<bool>("compiled"));
        Assert.False(payload.Value<bool>("executed"));
        Assert.True(payload.Value<bool>("compile_only"));
        Assert.Equal(0, doc.MutationCount);
        Assert.Equal("", payload.Value<string>("stdout"));
    }

    [Fact]
    public async Task Compile_diagnostics_use_original_script_lines_with_data()
    {
        var (payload, _) = await Run("var n = (int)data[\"n\"];\nreturn MissingInputSymbol;", "{\"n\":1}", true);
        Assert.False(payload.Value<bool>("ok"));
        var diagnostic = Assert.Single((JArray)payload["diagnostics"]);
        Assert.Equal("script", diagnostic.Value<string>("source"));
        Assert.Equal(2, diagnostic.Value<int>("line"));
        Assert.Equal("CS0103", diagnostic.Value<string>("code"));
        Assert.False(payload.Value<bool>("executed"));
    }

    [Fact]
    public async Task Runtime_error_preserves_stdout_and_reports_script_location()
    {
        var (payload, _) = await Run("Console.WriteLine(\"before-error\");\nthrow new InvalidOperationException(\"input-runtime-error\");");
        Assert.False(payload.Value<bool>("ok"));
        Assert.Contains("before-error", payload.Value<string>("stdout"));
        Assert.Equal(2, payload["location"].Value<int>("line"));
        Assert.Equal("script", payload["location"].Value<string>("source"));
    }

    [Fact]
    public async Task Invalid_or_oversized_data_is_refused_before_code()
    {
        foreach (var data in new[] { "{bad json", "\"" + new string('中', 180000) + "\"" })
        {
            var doc = new Document();
            var result = await Task.Run(() => new SendCodeHandler().Execute(doc,
                new JObject { ["code"] = "doc.MutationCount++;", ["data_json"] = data, ["script_inputs_version"] = 1 }));
            Assert.False(result.Ok);
            Assert.Equal(0, doc.MutationCount);
        }
    }

    [Theory]
    [InlineData("send_code")]
    [InlineData("Send_Code")]
    public void Enhanced_batch_is_refused_before_earlier_items_can_mutate(string command)
    {
        var invoked = 0;
        var request = JObject.Parse("{\"commands\":[{\"cmd\":\"create_line\",\"params\":{}},{\"cmd\":\"" + command + "\",\"params\":{\"code\":\"return 1;\",\"compile_only\":true}}]}");
        var result = Bimwright.Dwg.Plugin.BatchExecutor.Run(request, (_, _) => { invoked++; return Bimwright.Dwg.Plugin.CommandResult.Success(null); });
        Assert.False(result.Ok);
        Assert.Contains("SCRIPT_INPUTS_DIRECT_TOOL_ONLY", result.Error);
        Assert.Equal(0, invoked);
    }


    [Fact]
    public async Task Enhanced_requests_require_the_protocol_marker_before_code()
    {
        var doc = new Document();
        var result = await Task.Run(() => new SendCodeHandler().Execute(doc,
            new JObject { ["code"] = "doc.MutationCount++;", ["compile_only"] = true }));
        Assert.False(result.Ok);
        Assert.Equal(0, doc.MutationCount);
    }
}
