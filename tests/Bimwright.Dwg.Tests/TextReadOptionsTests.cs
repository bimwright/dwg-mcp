using System;
using Bimwright.Dwg.Plugin;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests;

public class TextReadOptionsTests
{
    [Fact]
    public void Legacy_two_argument_calls_do_not_enter_the_explicit_source_mode()
    {
        var data = JObject.Parse("{\"grouping_strength\":\"strong\",\"include_entities\":true,\"handles\":null}");
        Assert.False(TextReadOptions.IsExtended(data));
        data["handles"] = new JArray(); Assert.True(TextReadOptions.IsExtended(data));
        var options = TextReadOptions.Create(data);
        Assert.Empty(options.Handles); Assert.Equal("model", options.Query.Space); Assert.Equal(16384, options.MaxOutputBytes);
    }

    [Fact]
    public void Handle_sets_are_canonical_and_cursor_hash_binds_the_read_context()
    {
        var data = JObject.Parse("{\"handles\":[\"10\",\"f\",\"0F\"],\"grouping_strength\":\"normal\"}");
        var first = TextReadOptions.Create(data);
        Assert.Equal(new[] { "F", "10" }, first.Handles);
        data["handles"] = new JArray("F", "10"); data["max_output_bytes"] = 65536;
        Assert.Equal(first.Fingerprint, TextReadOptions.Create(data).Fingerprint);
        data["include_entities"] = true; Assert.NotEqual(first.Fingerprint, TextReadOptions.Create(data).Fingerprint);
        Assert.NotEqual(first.ClusterKey("snapshot:1", 1), first.ClusterKey("snapshot:2", 1));
        Assert.StartsWith("read:", first.ClusterKey("snapshot:1", 1));
        var next = first.ToArguments();
        Assert.Equal("normal", next.Value<string>("groupingStrength")); Assert.False(next.Value<bool>("includeEntities"));
        Assert.Equal("[\"F\",\"10\"]", next.Value<string>("handles"));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"handles\":[],\"bbox\":{\"min\":[0,0,0],\"max\":[1,1,1]}}")]
    [InlineData("{\"handles\":[0]}")]
    [InlineData("{\"handles\":[\"0\"]}")]
    [InlineData("{\"handles\":[\"0x10\"]}")]
    [InlineData("{\"handles\":[],\"grouping_strength\":\"unknown\"}")]
    [InlineData("{\"handles\":[],\"include_entities\":1}")]
    [InlineData("{\"handles\":[],\"space\":\"current\"}")]
    [InlineData("{\"handles\":[],\"max_output_bytes\":4095}")]
    public void Invalid_source_options_are_rejected_before_host_access(string json)
        => Assert.Throws<ArgumentException>(() => TextReadOptions.Create(JObject.Parse(json)));
}
