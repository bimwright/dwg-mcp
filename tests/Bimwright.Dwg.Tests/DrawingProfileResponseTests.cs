using System.Linq;
using Bimwright.Dwg.Server;
using ModelContextProtocol.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests;

public class DrawingProfileResponseTests
{
    [Fact]
    public void Incomplete_histogram_does_not_offer_offset_continuation_but_complete_tables_still_do()
    {
        var profile = DrawingProfileProtocolTests.FixtureProfile();
        profile["histograms"]["layers"]["counts_complete"] = false;
        var data = Body(Fit(profile))["result"];
        Assert.DoesNotContain(data["output"]["next_calls"], next => next["arguments"].Value<string>("section") == "layers");
        Assert.Contains(data["output"]["blocked_sections"], blocked => blocked.Value<string>("section") == "layers" && blocked.Value<string>("reason").StartsWith("scan_incomplete"));
        Assert.Contains(data["output"]["next_calls"], next => next["arguments"].Value<string>("section") == "types");
        Assert.False(data["output"].Value<bool>("complete"));
    }
    private static JObject Body(CallToolResult result) => JObject.Parse(result.Content.OfType<TextContentBlock>().First().Text);
    private static CallToolResult Fit(JObject profile, int cap = 16384)
        => DrawingProfileResponse.FromJson(new JObject { ["ok"] = true, ["result"] = profile }.ToString(Formatting.None), cap);

    [Fact]
    public void Own_default_cap_preserves_source_scope_counts_and_exact_group_names()
    {
        var profile = DrawingProfileProtocolTests.FixtureProfile();
        var allowed = profile["histograms"]["layers"]["rows"].Select(x => x.Value<string>("name")).ToHashSet();
        var result = Fit(profile);
        var body = Body(result)["result"];
        Assert.True(ResponseBudget.Measure(result) <= 16384);
        Assert.Equal(50000, body.Value<long>("exact_total_count"));
        Assert.Equal("full_enumeration", body["source"].Value<string>("counting"));
        Assert.False(body["source"].Value<bool>("estimated"));
        Assert.False(body["scope"].Value<bool>("xref_traversal"));
        Assert.True(body["coverage"].Value<bool>("complete"));
        foreach (var row in body["histograms"]["layers"]["rows"]) Assert.Contains(row.Value<string>("name"), allowed);
        Assert.False(body["output"].Value<bool>("complete"));
        Assert.NotEmpty(body["output"].Value<string>("note"));
    }

    [Fact]
    public void Repeated_fitting_does_not_advance_group_offsets_or_fabricate_continuation()
    {
        var profile = DrawingProfileProtocolTests.FixtureProfile();
        profile["request"]["section"] = "layers";
        profile["histograms"]["layers"]["offset"] = 20;
        foreach (var section in new[] { "types", "spaces" }) profile["histograms"][section]["rows"] = new JArray();
        var once = Fit(profile);
        var twice = DrawingProfileResponse.Fit(once, 16384);
        Assert.True(JToken.DeepEquals(Body(once), Body(twice)));
        var data = Body(twice)["result"];
        var returned = data["histograms"]["layers"]["rows"].Count();
        var next = data["output"]["next_calls"].Single(x => x["arguments"].Value<string>("section") == "layers");
        Assert.Equal(20 + returned, next["arguments"].Value<int>("offset"));
        Assert.Equal("fixture:12", next["arguments"].Value<string>("expected_snapshot"));
        Assert.Equal(5000 - 20 - returned, data["output"]["omitted"].Value<int>("layers"));
        Assert.Equal(20, data["output"]["prior_groups_not_repeated"].Value<int>("layers"));
    }

    [Fact]
    public void An_undeliverable_identifier_is_explicitly_blocked_without_an_endless_next_call()
    {
        var profile = DrawingProfileProtocolTests.FixtureProfile();
        profile["request"]["section"] = "layers";
        foreach (var section in new[] { "types", "spaces" }) profile["histograms"][section]["rows"] = new JArray();
        profile["histograms"]["layers"]["rows"] = new JArray(new JObject { ["name"] = new string('界', 50000), ["count"] = 1 });
        profile["histograms"]["layers"]["total_groups"] = 1;
        var result = Fit(profile);
        Assert.True(ResponseBudget.Measure(result) <= 16384);
        var body = Body(result)["result"];
        Assert.Empty(body["histograms"]["layers"]["rows"]);
        Assert.Contains(body["output"]["blocked_sections"], row => row.Value<string>("section") == "layers" && row.Value<string>("reason").Contains("increase"));
        Assert.DoesNotContain(body["output"]["next_calls"], row => row["arguments"].Value<string>("section") == "layers");
    }

    [Fact]
    public void Omitted_argument_warning_and_metadata_are_declared_in_content()
    {
        var result = Fit(DrawingProfileProtocolTests.FixtureProfile());
        var body = Body(result);
        body["_ignored_arguments"] = new JObject { ["unknown"] = new string('x', 20000) };
        ((TextContentBlock)result.Content[0]).Text = body.ToString(Formatting.None);
        result.Meta = new System.Text.Json.Nodes.JsonObject { ["large"] = new string('x', 20000) };
        var fitted = DrawingProfileResponse.Fit(result, 16384);
        Assert.True(ResponseBudget.Measure(fitted) <= 16384);
        var output = Body(fitted)["result"]["output"];
        Assert.True(output.Value<bool>("ignored_argument_details_omitted"));
        Assert.True(output.Value<bool>("metadata_omitted"));
        Assert.True(Body(fitted)["_ignored_arguments"].Value<bool>("details_omitted"));
    }
}
