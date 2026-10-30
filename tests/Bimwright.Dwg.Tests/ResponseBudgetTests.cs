using System;
using System.Text;
using System.Text.Json;
using Bimwright.Dwg.Server;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests;

public class ResponseBudgetTests : OutputArtifactTestScope
{
    private static readonly DwgMcpConfig Small = new() { ResponseBudgetBytes = 4096 };
    private static CallToolResult Result(JObject data, bool? isError = null)
        => new() { IsError = isError, Content = new System.Collections.Generic.List<ContentBlock> { new TextContentBlock { Text = data.ToString(Newtonsoft.Json.Formatting.None) } } };
    private static JObject Text(CallToolResult result) => JObject.Parse(((TextContentBlock)result.Content[0]).Text);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Compacted_script_keeps_reported_execution_status_without_reading_user_DTO(bool scriptOk)
    {
        var result = Result(new JObject { ["ok"] = true, ["result"] = new JObject
        {
            ["ok"] = scriptOk, ["error"] = scriptOk ? null : "failed after a committed edit",
            ["result"] = new JObject { ["ok"] = false, ["error"] = "user DTO" }, ["stdout"] = new string('x', 10000)
        }});
        var compact = ResponseBudget.Apply(result, Small, false, "dwg_send_code");
        var data = Text(compact);
        Assert.True(data.Value<bool>("ok"));
        Assert.Equal(scriptOk, data["result"].Value<bool>("ok"));
        Assert.Null(data["result"]["result"]);
        Assert.Null(data["mutation_applied"]);
        Assert.Null(compact.IsError);
        Assert.Contains("Do not replay", data["_response_budget"].Value<string>("guidance"));
        Assert.True(ResponseBudget.Measure(compact) <= 4096);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Compaction_scans_all_reported_items_and_keeps_late_failure(bool batch)
    {
        var items = new JArray();
        for (var i = 0; i < 100; i++) items.Add(new JObject { ["ok"] = true, ["handle"] = "ABC", ["result"] = new string('x', 80) });
        items.Add(new JObject { ["ok"] = false, ["error"] = "late failure" });
        items.Add(new JObject { ["ok"] = "not a boolean" });
        var payload = batch ? (JToken)new JObject { ["partial_failure"] = true, ["transactional"] = false, ["results"] = items } : items;
        var compact = ResponseBudget.Apply(Result(new JObject { ["ok"] = true, ["result"] = payload }), Small, false, batch ? "dwg_batch_execute" : "dwg_update_texts");
        var facts = Text(compact)["result"];
        Assert.Equal(102, facts.Value<int>("items_reported"));
        Assert.Equal(100, facts.Value<int>("items_reported_ok"));
        Assert.Equal(1, facts.Value<int>("items_reported_failed"));
        Assert.Equal(1, facts.Value<int>("items_without_status"));
        Assert.Equal("late failure", facts["failed_item_sample"][0].Value<string>("error"));
        if (batch) { Assert.True(facts.Value<bool>("partial_failure")); Assert.False(facts.Value<bool>("transactional")); }
    }

    [Fact]
    public void Plugin_truncation_is_still_truncated_and_preview_does_not_establish_outcome()
    {
        var compact = ResponseBudget.Apply(Result(new JObject { ["ok"] = true, ["result"] = new JObject
        {
            ["truncated"] = true, ["original_size_bytes"] = 20000000, ["original_length"] = 15000000,
            ["preview"] = "{\"ok\":false,\"mutation_applied\":true}" + new string('x', 10000)
        }}), Small, false, "dwg_send_code");
        var facts = Text(compact)["result"];
        Assert.True(facts.Value<bool>("truncated")); Assert.Equal(20000000, facts.Value<int>("original_size_bytes"));
        Assert.Null(facts["ok"]); Assert.Null(facts["mutation_applied"]); Assert.Null(facts["preview"]);
    }

    [Fact]
    public void Reported_partial_unknown_effects_and_persistence_failure_remain_distinct()
    {
        var compact = ResponseBudget.Apply(Result(new JObject
        {
            ["ok"] = false, ["error_code"] = "registry_insert_failed", ["outcome"] = "unknown", ["partial_failure"] = true,
            ["effects"] = new JObject { ["plugin_applied"] = true, ["server_registered"] = false },
            ["message"] = new string('中', 10000)
        }, true), Small, false, "dwg_accept_bake_suggestion");
        var data = Text(compact);
        Assert.False(data.Value<bool>("ok")); Assert.Equal("registry_insert_failed", data.Value<string>("error_code"));
        Assert.Equal("unknown", data.Value<string>("outcome")); Assert.True(data.Value<bool>("partial_failure"));
        Assert.True(data["effects"].Value<bool>("plugin_applied")); Assert.False(data["effects"].Value<bool>("server_registered"));
        Assert.True(compact.IsError); Assert.True(ResponseBudget.Measure(compact) <= 4096);
    }

    [Fact]
    public void Removing_inline_image_keeps_capture_evidence_and_reports_delivery_omission()
    {
        var result = Result(new JObject { ["ok"] = true, ["result"] = new JObject { ["output_path"] = "D:\\capture.png", ["sha256"] = new string('f', 64) }, ["image_delivery"] = new JObject { ["ok"] = true } }, false);
        result.Content.Add(ImageContentBlock.FromBytes(new byte[6000], "image/png"));
        var compact = ResponseBudget.Apply(result, Small, false, "dwg_capture_view_image");
        var data = Text(compact);
        Assert.True(data.Value<bool>("ok")); Assert.Equal("D:\\capture.png", data["result"].Value<string>("output_path"));
        Assert.Equal(new string('f', 64), data["result"].Value<string>("sha256"));
        Assert.False(data["image_delivery"].Value<bool>("ok")); Assert.True(data["image_delivery"].Value<bool>("omitted"));
        Assert.Single(compact.Content); Assert.False(compact.IsError);
    }

    [Fact]
    public void Minimum_budget_sheds_long_diagnostics_without_losing_known_outcome_flags()
    {
        var data = new JObject { ["ok"] = false, ["error"] = new string('\u0001', 10000), ["message"] = new string('中', 10000),
            ["partial_failure"] = true, ["outcome"] = "unknown", ["mutation_applied"] = null,
            ["result"] = new JObject { ["ok"] = false, ["truncated"] = true, ["outcome"] = "unknown",
                ["error"] = new string('\u0001', 10000), ["message"] = new string('中', 10000),
                ["output_path"] = new string('中', 10000), ["saved_path"] = new string('中', 10000),
                ["font_path"] = new string('中', 10000), ["name"] = new string('中', 10000) } };
        var compact = ResponseBudget.Apply(Result(data, true), Small, false, "dwg_send_code");
        var facts = Text(compact);
        Assert.True(ResponseBudget.Measure(compact) <= 4096);
        Assert.True(facts.Value<bool>("partial_failure")); Assert.Equal("unknown", facts.Value<string>("outcome"));
        Assert.Equal(JTokenType.Null, facts["mutation_applied"].Type);
        Assert.True(facts["result"].Value<bool>("truncated")); Assert.False(facts["result"].Value<bool>("ok"));
        Assert.True(compact.IsError);
    }

    [Fact]
    public void Oversized_health_failure_keeps_its_structured_target_error()
    {
        var compact = ResponseBudget.Apply(Result(new JObject
        {
            ["ok"] = false, ["error"] = new JObject { ["code"] = "TARGET_LOST", ["message"] = "Restart with --target pid:1234; dwg_switch_target is unavailable in read-only mode." },
            ["update"] = new JObject { ["payload"] = new string('x', 10000) }
        }, true), Small, true, "dwg_health");
        Assert.Equal("TARGET_LOST", Text(compact)["error"].Value<string>("code"));
        Assert.Contains("--target pid:1234", Text(compact)["error"].Value<string>("message"));
        Assert.True(compact.IsError);
        Assert.True(ResponseBudget.Measure(compact) <= 4096);
    }

    [Fact]
    public void Adversarial_reported_values_still_deliver_a_minimum_budget_receipt_after_a_write()
    {
        JObject Hostile() => new()
        {
            ["outcome"] = new string('中', 64), ["error_code"] = new string('\u0001', 64),
            ["error"] = new string('中', 1000), ["partial_failure"] = true, ["mutation_applied"] = null,
            ["ok"] = new JValue(System.Numerics.BigInteger.Parse(new string('9', 64))),
            ["success"] = new JValue(System.Numerics.BigInteger.Parse(new string('9', 64))),
            ["blocked"] = new JValue(System.Numerics.BigInteger.Parse(new string('9', 64))),
            ["transactional"] = new JValue(System.Numerics.BigInteger.Parse(new string('9', 64))),
            ["truncated"] = true, ["execution_authorized"] = false
        };
        var data = Hostile(); data["result"] = Hostile(); data["image_delivery"] = Hostile();
        var compact = ResponseBudget.Apply(Result(data, true), Small, false, "dwg_send_code");
        Assert.True(ResponseBudget.Measure(compact) <= 4096);
        var receipt = Text(compact);
        Assert.True(receipt["_response_budget"].Value<bool>("minimal_receipt"));
        Assert.True(receipt.Value<bool>("partial_failure")); Assert.True(receipt["result"].Value<bool>("truncated"));
        Assert.Equal(JTokenType.Null, receipt["mutation_applied"].Type);
        Assert.Contains("Do not replay", receipt["_response_budget"].Value<string>("guidance"));
        Assert.True(compact.IsError);
    }

    [Fact]
    public void Measurement_includes_Unicode_escaping_structured_content_and_metadata()
    {
        var result = new CallToolResult
        {
            IsError = true,
            Content = new[] { new TextContentBlock { Text = "中文 Tiếng Việt 😀 \"quoted\" \\path\n+<>&" } },
            StructuredContent = JsonSerializer.Deserialize<JsonElement>("{\"seen\":\"😀\"}"),
            Meta = new System.Text.Json.Nodes.JsonObject { ["source"] = "中文" }
        };
        var serialized = JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions);
        Assert.Equal(Encoding.UTF8.GetByteCount(serialized), ResponseBudget.Measure(result));
        Assert.True(ResponseBudget.Measure(result) > Encoding.UTF8.GetByteCount(((TextContentBlock)result.Content[0]).Text));
        result.Meta["more"] = new string('x', 1000);
        Assert.True(ResponseBudget.Measure(result) > Encoding.UTF8.GetByteCount(serialized));
    }

    [Fact]
    public void Default_preserves_existing_maximum_inline_image_even_with_base64_plus_escaping()
    {
        var bytes = new byte[ViewImageResponse.MaxImageBytes];
        byte[] pattern = { 251, 239, 190 }; // Base64 is '+', escaped by the actual SDK serializer.
        for (var i = 0; i < bytes.Length; i++) bytes[i] = pattern[i % pattern.Length];
        var result = new CallToolResult
        {
            Content = new ContentBlock[]
            {
                new TextContentBlock { Text = "{\"ok\":true,\"result\":{\"output_path\":\"capture.png\",\"sha256\":\"" + new string('f', 64) + "\"}}" },
                ImageContentBlock.FromBytes(bytes, "image/png")
            }
        };
        var measured = ResponseBudget.Measure(result);
        Assert.True(measured > 64 * 1024 * 1024);
        Assert.True(measured < new DwgMcpConfig().ResponseBudgetBytesOrDefault);
        var delivered = ResponseBudget.Apply(result, new DwgMcpConfig(), false, "dwg_capture_view_image");
        Assert.Same(result, delivered);
        Assert.IsType<ImageContentBlock>(delivered.Content[1]);
        Assert.True(ResponseBudget.Measure(delivered) <= DwgMcpConfig.DefaultResponseBudgetBytes);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(0, false)]
    [InlineData(-1, true)]
    public void Warning_thresholds_use_exact_final_bytes(int offset, bool strong)
    {
        var result = Result(new JObject { ["ok"] = true, ["result"] = "small result" });
        var bytes = ResponseBudget.Measure(result);
        var config = new DwgMcpConfig { ResponseWarnBytes = bytes + offset, ResponseStrongWarnBytes = bytes + (strong ? -1 : 100) };
        var delivered = ResponseBudget.Apply(result, config, true, "dwg_get_drawing_info");
        var warning = delivered.Meta?["bimwright/dwg-mcp/response_budget"];
        if (offset > 0) Assert.Null(warning);
        else Assert.Equal(strong ? "strong_warning" : "warning", warning["level"].GetValue<string>());
    }
}
