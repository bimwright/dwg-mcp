using System.Collections.Generic;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.Handlers;
using Bimwright.Dwg.Plugin.ToolBaker;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests;

public class ExplicitReadGuardTests
{
    private static BakedToolRecord Preset() => new()
    {
        Name = "read_selection", Source = "preset", HandlerTool = "get_selected_texts", ParamsSchema = "{}", FixedArgs = "{}",
        SourceCode = BakedToolRuntimeSource.BuildPreset("get_selected_texts", new JObject())
    };

    [Fact]
    public void Runtime_source_parameters_cannot_turn_a_legacy_baked_read_into_a_paged_read()
    {
        var record = Preset(); var args = new JObject { ["handles"] = new JArray() };
        Assert.True(BakedToolParameterDefaults.ContainsStandaloneRead(record, args));
        var executed = 0; var handler = new RunBakedToolHandler((_, _, _) => { executed++; return CommandResult.Success(null); });
        var result = handler.Execute(null, new JObject { ["name"] = record.Name, ["params"] = args, ["tool_record"] = JObject.FromObject(record) });
        Assert.False(result.Ok); Assert.Contains("PAGED_QUERY_DIRECT_TOOL_ONLY", result.Error); Assert.Equal(0, executed);
    }

    [Fact]
    public void Baked_macro_with_an_explicit_read_is_refused_before_earlier_write_steps()
    {
        var sequence = new JArray(new JObject { ["cmd"] = "update_texts", ["params"] = new JObject() },
            new JObject { ["cmd"] = "get_selected_texts", ["params"] = new JObject { ["handles"] = new JArray() } });
        var record = new BakedToolRecord { Name = "mixed_macro", Source = "macro", ParamsSchema = "{}", Sequence = sequence.ToString(),
            SourceCode = BakedToolRuntimeSource.BuildMacro(new[] { "update_texts", "get_selected_texts" }) };
        Assert.True(BakedToolParameterDefaults.ContainsStandaloneRead(record, new JObject()));
        var executed = 0; var handler = new RunBakedToolHandler((_, _, _) => { executed++; return CommandResult.Success(null); });
        var result = handler.Execute(null, new JObject { ["name"] = record.Name, ["tool_record"] = JObject.FromObject(record) });
        Assert.False(result.Ok); Assert.Equal(0, executed);
    }

    [Fact]
    public void Legacy_baked_selection_with_only_original_parameters_keeps_dispatching()
    {
        var record = Preset(); var args = new JObject { ["grouping_strength"] = "normal", ["include_entities"] = false };
        Assert.False(BakedToolParameterDefaults.ContainsStandaloneRead(record, args));
        var executed = new List<string>(); var handler = new RunBakedToolHandler((_, command, _) => { executed.Add(command); return CommandResult.Success(null); });
        var result = handler.Execute(null, new JObject { ["name"] = record.Name, ["params"] = args, ["tool_record"] = JObject.FromObject(record) });
        Assert.True(result.Ok, result.Error); Assert.Equal(new[] { "get_selected_texts" }, executed);
    }
}
