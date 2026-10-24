using System;
using System.Linq;
using Bimwright.Dwg.Plugin;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests;

public class EntityQueryOptionsTests
{
    [Fact]
    public void Legacy_arguments_do_not_activate_the_paged_contract()
    {
        var legacy = JObject.Parse("{\"entity_type\":\"Line\",\"limit\":20,\"include_geometry\":true,\"paged\":false}");
        Assert.False(EntityQueryOptions.IsExtended(legacy));
        legacy["fields"] = JValue.CreateNull(); Assert.False(EntityQueryOptions.IsExtended(legacy));
        legacy["paged"] = true; Assert.True(EntityQueryOptions.IsExtended(legacy));
    }

    [Fact]
    public void Defaults_are_small_closed_fields_and_a_separate_byte_cap()
    {
        var options = EntityQueryOptions.Create(new JObject { ["paged"] = true });
        Assert.Equal(new[] { "handle", "type", "layer" }, options.Fields);
        Assert.Equal("model", options.Space); Assert.Equal(500, options.Limit);
        Assert.Equal(16384, options.MaxOutputBytes); Assert.Null(options.Bbox);
        Assert.Equal(64, options.Fingerprint.Length);
    }

    [Fact]
    public void Cursor_query_digest_normalizes_filter_aliases_but_excludes_delivery_sizes()
    {
        var first = JObject.Parse("{\"paged\":true,\"entity_type\":\"TEXT\",\"layer\":\"A\",\"fields\":[\"text\",\"layer\"],\"limit\":5}");
        var second = JObject.Parse("{\"paged\":true,\"entity_type\":\"dbtext\",\"layer\":\"a\",\"fields\":[\"layer\",\"text\"],\"limit\":128,\"max_output_bytes\":65536}");
        Assert.Equal(EntityQueryOptions.Create(first).Fingerprint, EntityQueryOptions.Create(second).Fingerprint);
        second["fields"] = new JArray("handle", "position");
        Assert.NotEqual(EntityQueryOptions.Create(first).Fingerprint, EntityQueryOptions.Create(second).Fingerprint);
        second["fields"] = new JArray("layer", "text"); second["space"] = "layout:Sheet A";
        Assert.NotEqual(EntityQueryOptions.Create(first).Fingerprint, EntityQueryOptions.Create(second).Fingerprint);
    }

    [Theory]
    [InlineData("{\"fields\":[\"Text\"]}")]
    [InlineData("{\"fields\":[\"text\",\"text\"]}")]
    [InlineData("{\"fields\":[\"arbitrary_property\"]}")]
    [InlineData("{\"fields\":[]}")]
    [InlineData("{\"bbox\":{\"min\":[0,0],\"max\":[1,1,1]}}")]
    [InlineData("{\"bbox\":{\"min\":[2,0,0],\"max\":[1,1,1]}}")]
    [InlineData("{\"bbox_mode\":\"contains\"}")]
    [InlineData("{\"bbox_mode\":\"overlaps\",\"bbox\":{\"min\":[0,0,0],\"max\":[1,1,1]}}")]
    [InlineData("{\"space\":\"current\"}")]
    [InlineData("{\"space\":\"layout:\"}")]
    [InlineData("{\"include_geometry\":true}")]
    [InlineData("{\"max_output_bytes\":4095}")]
    [InlineData("{\"max_output_bytes\":65537}")]
    public void Invalid_new_options_are_rejected_without_arbitrary_field_access(string json)
        => Assert.Throws<ArgumentException>(() => EntityQueryOptions.Create(JObject.Parse(json)));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Bounding_coordinates_must_be_finite(double invalid)
    {
        var data = new JObject { ["bbox"] = new JObject { ["min"] = new JArray(0, 0, 0), ["max"] = new JArray(1, 1, invalid) } };
        Assert.Throws<ArgumentException>(() => EntityQueryOptions.Create(data));
    }

    [Fact]
    public void Handle_is_always_present_and_cursor_length_is_bounded()
    {
        var options = EntityQueryOptions.Create(new JObject { ["fields"] = new JArray("text") });
        Assert.Equal(new[] { "handle", "text" }, options.Fields);
        Assert.Throws<ArgumentException>(() => EntityQueryOptions.Create(new JObject { ["cursor"] = new string('x', 1025) }));
        Assert.Throws<ArgumentException>(() => EntityQueryOptions.Create(new JObject { ["layer"] = new string('x', 1025) }));
    }
}
