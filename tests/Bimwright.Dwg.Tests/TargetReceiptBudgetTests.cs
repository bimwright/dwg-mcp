using System;
using System.Linq;
using Bimwright.Dwg.Server;
using ModelContextProtocol.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests;

public sealed class TargetReceiptBudgetTests : OutputArtifactTestScope
{
    private static JObject Target() => new()
    {
        ["protocol"] = 1, ["target_id"] = "acad-2024-123", ["pid"] = 123,
        ["process_start_utc"] = new DateTime(2026, 10, 24, 0, 0, 0, DateTimeKind.Utc),
        ["acad_year"] = 2024, ["document_id"] = new string('a', 32), ["document_title"] = "Nhà máy.dwg"
    };
    private static CallToolResult Result(JObject data) => new()
        { Content = new[] { new TextContentBlock { Text = data.ToString(Formatting.None) } } };
    private static JObject Body(CallToolResult result) => JObject.Parse(result.Content.OfType<TextContentBlock>().First().Text);
    private static void Identity(JObject expected, CallToolResult result, int cap = 4096)
    {
        var actual = (JObject)Body(result)["_target"];
        foreach (var key in new[] { "pid", "process_start_utc", "target_id", "acad_year", "document_id" })
            Assert.True(JToken.DeepEquals(expected[key], actual[key]), key);
        Assert.True(ResponseBudget.Measure(result) <= cap);
    }

    [Fact]
    public void Final_measurement_includes_receipt_and_compaction_retains_exact_identity()
    {
        var data = new JObject { ["ok"] = true, ["result"] = new JObject { ["value"] = new string('中', 10000) } };
        var without = ResponseBudget.Measure(Result(data));
        var target = Target(); target["document_title"] = new string('\u0001', 10000);
        data["_target"] = target;
        Assert.True(ResponseBudget.Measure(Result(data)) > without);
        var compact = ResponseBudget.Apply(Result(data), new DwgMcpConfig { ResponseBudgetBytes = 4096 }, false, "dwg_send_code");
        Identity(target, compact);
        Assert.True(Body(compact).Value<bool>("ok"));
        Assert.True(Body(compact)["_target"].Value<bool>("document_title_omitted"));
        Assert.Null(Body(compact)["_target"]["document_title"]);
    }

    [Fact]
    public void Adversarial_receipt_fields_are_omitted_instead_of_shortened_and_never_exceed_budget()
    {
        var target = Target();
        foreach (var property in target.Properties().ToArray()) property.Value = new string('\u0001', 10000);
        target["unknown"] = new string('x', 10000);
        var compact = ResponseBudget.Apply(Result(new JObject { ["ok"] = false, ["_target"] = target }),
            new DwgMcpConfig { ResponseBudgetBytes = 4096 }, false, "dwg_send_code");
        var receipt = (JObject)Body(compact)["_target"];
        Assert.True(ResponseBudget.Measure(compact) <= 4096);
        Assert.Null(receipt["pid"]);
        Assert.Null(receipt["document_id"]);
        Assert.True(receipt.Value<bool>("document_id_omitted"));
        Assert.Null(receipt["unknown"]);
    }

    [Fact]
    public void Minimal_fallback_keeps_bounded_identity_with_hostile_status_values()
    {
        JObject Hostile() => new()
        {
            ["outcome"] = new string('中', 64), ["error_code"] = new string('\u0001', 64),
            ["mutation_applied"] = null, ["execution_authorized"] = false,
            ["ok"] = new JValue(System.Numerics.BigInteger.Parse(new string('9', 64))),
            ["success"] = new JValue(System.Numerics.BigInteger.Parse(new string('9', 64))),
            ["blocked"] = new JValue(System.Numerics.BigInteger.Parse(new string('9', 64))),
            ["transactional"] = new JValue(System.Numerics.BigInteger.Parse(new string('9', 64))),
            ["error"] = new string('中', 1000), ["partial_failure"] = true, ["truncated"] = true
        };
        var source = Hostile(); source["result"] = Hostile(); source["image_delivery"] = Hostile();
        var target = Target(); source["_target"] = target;
        var compact = ResponseBudget.Apply(Result(source), new DwgMcpConfig { ResponseBudgetBytes = 4096 }, false, "dwg_send_code");
        Identity(target, compact);
        Assert.True(Body(compact)["_response_budget"].Value<bool>("minimal_receipt"));
    }

    [Fact]
    public void User_result_target_is_never_promoted_to_observed_envelope_metadata()
    {
        var compact = ResponseBudget.Apply(Result(new JObject { ["ok"] = true, ["result"] = new JObject
        { ["_target"] = Target(), ["stdout"] = new string('x', 10000) } }),
            new DwgMcpConfig { ResponseBudgetBytes = 4096 }, false, "dwg_send_code");
        Assert.Null(Body(compact)["_target"]);
    }

    [Theory]
    [InlineData("query")]
    [InlineData("count")]
    [InlineData("text")]
    [InlineData("profile")]
    public void Capability_or_scope_refusal_retains_observed_identity(string kind)
    {
        var target = Target();
        var source = Result(new JObject { ["ok"] = true, ["result"] = new JObject(), ["_target"] = target });
        var fitted = kind switch
        {
            "query" => EntityQueryResponse.Fit(source, 4096),
            "count" => EntityQueryResponse.Fit(source, 4096, countOnly: true),
            "text" => TextReadResponse.Fit(source, 4096),
            _ => DrawingProfileResponse.Fit(source, 4096, "model")
        };
        Identity(target, fitted);
        Assert.False(Body(fitted).Value<bool>("ok"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Profile_normal_page_or_oversized_core_accounts_for_receipt(bool oversized)
    {
        var target = Target();
        var profile = DrawingProfileProtocolTests.FixtureProfile();
        if (oversized) profile["source"]["details"] = new string('x', 10000);
        var fitted = DrawingProfileResponse.Fit(Result(new JObject { ["ok"] = true, ["result"] = profile, ["_target"] = target }), 4096);
        Identity(target, fitted);
        if (oversized) Assert.StartsWith("PROFILE_OUTPUT_TOO_LARGE:", Body(fitted).Value<string>("error"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Query_or_text_oversized_core_retains_receipt_and_stays_bounded(bool text)
    {
        var target = Target();
        var data = new JObject { [text ? "text_page_version" : "query_page_version"] = 1,
            [text ? "clusters" : "entities"] = new JArray(), ["source"] = new string('x', 10000) };
        var source = Result(new JObject { ["ok"] = true, ["result"] = data, ["_target"] = target });
        var fitted = text ? TextReadResponse.Fit(source, 4096) : EntityQueryResponse.Fit(source, 4096);
        Identity(target, fitted);
        Assert.False(Body(fitted).Value<bool>("ok"));
    }
}
