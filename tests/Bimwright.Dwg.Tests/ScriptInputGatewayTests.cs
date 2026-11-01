using System.Text.Json;
using System.Threading.Tasks;
using Bimwright.Dwg.Server.Tools;
using Bimwright.Dwg.Tests.Helpers;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests;

public sealed class ScriptInputGatewayTests
{
    [Fact]
    public async Task Gateway_preserves_json_text_and_marks_enhanced_calls()
    {
        using var data = JsonDocument.Parse("{\"date\":\"2026-11-01T00:00:00Z\",\"items\":[1,2.5]}");
        var sent = await Capture.Send(() => CodeTools.SendCode("return data;", data.RootElement, true));
        var wire = sent.Json();
        Assert.Equal(data.RootElement.GetRawText(), wire.Value<string>("data_json"));
        Assert.Equal(1, wire.Value<int>("script_inputs_version"));
        Assert.True(wire.Value<bool>("compile_only"));
    }

    [Fact]
    public async Task Code_only_keeps_the_original_wire_shape()
    {
        var sent = await Capture.Send(() => CodeTools.SendCode("return 42;"));
        Assert.Equal(new[] { "code" }, System.Linq.Enumerable.Select(sent.Json().Properties(), p => p.Name));
    }

    [Fact]
    public void History_displays_data_metadata_without_retaining_input_values()
    {
        var display = Bimwright.Dwg.Plugin.Views.Toast.ActivityHistoryDetails.Json(
            new JObject { ["code"] = "return data;", ["data_json"] = "{\"private_member\":\"private-input-value\"}" });
        Assert.DoesNotContain("private-input-value", display);
        Assert.Contains("sha256", display);
        Assert.Equal(JTokenType.Object, JObject.Parse(display)["data_json"].Type);
    }
}
