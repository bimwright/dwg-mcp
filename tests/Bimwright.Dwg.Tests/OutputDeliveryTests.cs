using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Server;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class OutputDeliveryTests : OutputArtifactTestScope
    {
        [Fact]
        public void Long_escaped_artifact_paths_cannot_break_the_minimum_budget_or_become_fake_shortened_paths()
        {
            var nested = ArtifactRoot;
            for (var i = 0; i < 7; i++) nested = Path.Combine(nested, new string('中', 60) + i);
            OutputArtifactStore.RootOverride = nested;
            JObject Hostile() => new JObject { ["ok"] = true, ["outcome"] = new string('中', 64),
                ["error"] = new string('中', 3000), ["partial_failure"] = true, ["mutation_applied"] = null };
            var data = Hostile(); data["result"] = Hostile();
            var compact = ResponseBudget.Apply(new CallToolResult { IsError = true, Content = new[] { new TextContentBlock { Text = data.ToString() } } }, Small, false, "dwg_send_code");
            Assert.True(ResponseBudget.Measure(compact) <= 4096);
            var receipt = Body(compact)["output_artifact"];
            Assert.True(receipt.Value<bool>("complete")); Assert.Equal(64, receipt.Value<string>("sha256").Length);
            if (receipt.Value<bool?>("path_omitted") == true) Assert.True(receipt["path"] == null || receipt["path"].Type == JTokenType.Null);
            else Assert.True(File.Exists(receipt.Value<string>("path")));
            Assert.Single(Directory.GetFiles(nested, "output-*.json"));
        }
        [Fact]
        public void Compaction_distinguishes_unknown_execution_and_unverified_files_from_known_failure()
        {
            var items = new JArray(new JObject { ["ok"] = false, ["outcome"] = "unknown" },
                new JObject { ["ok"] = false, ["outcome"] = "failed" },
                new JObject { ["ok"] = true, ["outcome"] = "completed_output_unverified", ["output_verified"] = false });
            var data = new JObject { ["ok"] = true, ["result"] = new JObject { ["results"] = items }, ["padding"] = new string('x', 10000) };
            var compact = ResponseBudget.Apply(new CallToolResult { Content = new[] { new TextContentBlock { Text = data.ToString() } } }, Small, false, "dwg_batch_execute");
            var receipt = Body(compact)["result"];
            Assert.Equal(1, receipt.Value<int>("items_reported_unknown")); Assert.Equal(1, receipt.Value<int>("items_reported_failed"));
            Assert.Equal(1, receipt.Value<int>("items_reported_ok")); Assert.Equal(1, receipt.Value<int>("items_output_unverified"));
            Assert.Equal(0, receipt.Value<int>("items_without_status"));
        }
        private static readonly DwgMcpConfig Small = new DwgMcpConfig { ResponseBudgetBytes = 4096 };
        private static string Read(JToken receipt)
        {
            Assert.True(receipt.Value<bool>("complete"));
            var bytes = File.ReadAllBytes(receipt.Value<string>("path"));
            Assert.Equal(bytes.Length, receipt.Value<long>("bytes"));
            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), receipt.Value<string>("sha256"));
            return Encoding.UTF8.GetString(bytes);
        }
        private static JObject Body(CallToolResult result) => JObject.Parse(result.Content.OfType<TextContentBlock>().First().Text);

        [Fact]
        public void Plugin_spills_before_truncation_preserving_complete_unicode_stdout_and_script_failure()
        {
            var source = new JObject { ["ok"] = false, ["stdout"] = "秘密🔐\n" + new string('x', 5000), ["error"] = "after committed effect", ["result"] = null };
            var before = source.ToString(Formatting.None);
            var receipt = JObject.FromObject(ResponseSizeGuard.ApplyResult(source, 1024));
            Assert.True(receipt.Value<bool>("truncated")); Assert.False(receipt.Value<bool>("ok"));
            Assert.Equal(before, Read(receipt["output_artifact"])); Assert.Equal(before, source.ToString(Formatting.None));
            Assert.Equal("plugin_result", receipt["output_artifact"].Value<string>("scope"));
            Assert.True(Encoding.UTF8.GetByteCount(receipt.Value<string>("preview")) <= 512);
        }

        [Fact]
        public void Complete_batch_artifact_keeps_every_step_and_late_failure_while_inline_receipt_is_bounded()
        {
            var calls = 0;
            var commands = new JArray(Enumerable.Range(0, 30).Select(i => new JObject { ["cmd"] = i == 29 ? "last-fail" : "ok" }));
            var batch = BatchExecutor.Run(new JObject { ["commands"] = commands }, (cmd, _) =>
            { calls++; return cmd == "last-fail" ? CommandResult.Fail("late failure") : CommandResult.Success(new { text = new string('x', 1000) }); });
            var original = JObject.FromObject(batch.Result);
            var receipt = JObject.FromObject(ResponseSizeGuard.ApplyResult(original, 2048));
            var full = JObject.Parse(Read(receipt["output_artifact"]));
            Assert.Equal(30, calls); Assert.Equal(30, full["results"].Count());
            Assert.False(full["results"][29].Value<bool>("ok")); Assert.Equal("failed", full["results"][29].Value<string>("outcome"));
            Assert.True(receipt.Value<bool>("partial_failure")); Assert.True(receipt.Value<bool>("item_details_omitted"));
            Assert.Equal(8, receipt["results"].Count()); Assert.False(receipt.Value<bool>("transactional"));
        }

        [Fact]
        public void Final_mcp_artifact_preserves_exact_original_content_images_structured_content_and_nullable_error()
        {
            var result = new CallToolResult { IsError = null,
                Content = new ContentBlock[] { new TextContentBlock { Text = "Nguyễn " + new string('x', 6000) }, new ImageContentBlock { MimeType = "image/png", Data = new byte[2048] } },
                StructuredContent = System.Text.Json.JsonSerializer.Deserialize<JsonElement>("{\"full\":\"秘密\"}"),
                Meta = new System.Text.Json.Nodes.JsonObject { ["keep"] = true } };
            var exact = System.Text.Json.JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions);
            var compact = ResponseBudget.Apply(result, Small, false, "fixture");
            var receipt = Body(compact);
            Assert.True(ResponseBudget.Measure(compact) <= 4096); Assert.Null(compact.IsError);
            Assert.Equal(exact, Read(receipt["output_artifact"]));
            Assert.Equal("mcp_tool_result", receipt["output_artifact"].Value<string>("scope"));
            Assert.Contains("not output already omitted upstream", receipt["_response_budget"].Value<string>("guidance"));
        }

        [Fact]
        public void Gateway_artifact_of_a_plugin_receipt_keeps_the_reference_to_the_complete_plugin_artifact()
        {
            var original = new JObject { ["ok"] = true, ["stdout"] = new string('x', 12000) };
            var plugin = JObject.FromObject(ResponseSizeGuard.ApplyResult(original, 512));
            var response = new JObject { ["ok"] = true, ["result"] = plugin, ["padding"] = new string('x', 10000) };
            var compact = ResponseBudget.Apply(new CallToolResult { Content = new[] { new TextContentBlock { Text = response.ToString(Formatting.None) } } }, Small, false, "dwg_send_code");
            var mcp = JObject.Parse(Read(Body(compact)["output_artifact"]));
            var received = JObject.Parse(mcp["content"][0].Value<string>("text"));
            Assert.True(JToken.DeepEquals(original, JObject.Parse(Read(received["result"]["output_artifact"]))));
            Assert.Equal(2, Directory.GetFiles(ArtifactRoot, "output-*.json").Length);
        }

        [Fact]
        public void Failed_artifact_storage_keeps_reported_outcome_and_null_identity_without_replay()
        {
            File.WriteAllText(ArtifactRoot, "blocked storage");
            try
            {
                var source = new JObject { ["ok"] = false, ["error"] = "failed after an edit", ["stdout"] = new string('x', 10000) };
                var plugin = JObject.FromObject(ResponseSizeGuard.ApplyResult(source, 512));
                Assert.False(plugin.Value<bool>("ok")); Assert.False(plugin["output_artifact"].Value<bool>("complete"));
                var compact = ResponseBudget.Apply(new CallToolResult { IsError = true, Content = new[] { new TextContentBlock { Text = new JObject { ["ok"] = false, ["result"] = source }.ToString() } } }, Small, false, "dwg_send_code");
                Assert.True(compact.IsError); Assert.False(Body(compact)["output_artifact"].Value<bool>("complete"));
                Assert.Equal(JTokenType.Null, Body(compact)["output_artifact"]["path"].Type);
                Assert.Contains("Do not replay", Body(compact)["_response_budget"].Value<string>("guidance"));
            }
            finally { File.Delete(ArtifactRoot); }
        }

        [Fact]
        public void Small_outputs_never_create_artifact_storage_or_change_identity()
        {
            var data = new JObject { ["ok"] = true };
            Assert.Same(data, ResponseSizeGuard.ApplyResult(data));
            var result = new CallToolResult { Content = new[] { new TextContentBlock { Text = data.ToString() } } };
            Assert.Same(result, ResponseBudget.Apply(result, new DwgMcpConfig(), true, "fixture"));
            Assert.False(Directory.Exists(ArtifactRoot));
        }
    }
}
