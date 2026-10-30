using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.IO;
using Bimwright.Dwg.Server.Logging;
using Bimwright.Dwg.Plugin.Views.Toast;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class ActivityHistoryDetailsTests
    {
        [Theory]
        [InlineData("Response body: {\"password\":\"history-secret-marker\"}")]
        [InlineData("Log says 'access_token': 'history-secret-marker'")]
        [InlineData("Header: \"Authorization\": \"Bearer history-secret-marker\"")]
        [InlineData("Response: {\"nested\":{\"client_secret\":\"history-secret-marker with spaces\"}}")]
        [InlineData("Response: {\"password\":\"history-secret-marker with \\\"quotes\\\"\"}")]
        [InlineData("Truncated response: {\"api_key\":\"history-secret-marker with spaces")]
        public void Quoted_credentials_in_prose_are_redacted_from_history_text_and_copy_values(string text)
        {
            var history = new ActivityHistory();
            history.Record(new McpToastViewModel { CommandName = "query_entities", Title = "Query", Summary = text, Success = false },
                new JObject { ["note"] = text }, error: text, description: text);
            var entry = Assert.Single(history.Snapshot());
            Assert.DoesNotContain("history-secret-marker", entry.Summary + entry.What + entry.Input + entry.Output);
        }
        [Fact]
        public void Session_details_keep_sanitized_original_values_beyond_the_summary()
        {
            var history = new ActivityHistory();
            var parameters = new JObject { ["layer"] = "Nguyễn-秘密", ["text"] = "first\nsecond" };
            var result = new JObject { ["items"] = new JArray(Enumerable.Range(1, 30)), ["note"] = new string('a', 600) };
            var vm = ToastContentBuilder.BuildCompleted("query_entities", result.ToString(), true, null, "Read entities", 19);
            history.Record(vm, parameters, result.ToString(), description: "Read entities");
            var entry = Assert.Single(history.Snapshot());
            Assert.Equal("Nguyễn-秘密", JObject.Parse(entry.Input).Value<string>("layer"));
            Assert.Equal("first\nsecond", JObject.Parse(entry.Input).Value<string>("text"));
            Assert.Equal(new string('a', 600), JObject.Parse(entry.Output).Value<string>("note"));
            Assert.Contains("Read entities", entry.What);
            Assert.Contains("19 ms", entry.What);
            parameters["layer"] = "changed later";
            vm.Title = "changed later";
            Assert.DoesNotContain("changed later", entry.What + entry.Input);
            Assert.DoesNotContain('\u200b', entry.Input + entry.Output);
        }

        [Fact]
        public void Nested_secrets_and_code_are_redacted_without_mutating_parameters()
        {
            const string code = "// Nguyễn\nreturn \"秘密🔐\";";
            var parameters = JObject.FromObject(new { commands = new[] { new { @params = new { source_code = code, api_key = "key-marker", access_token = "token-marker", password = "password-marker" } } }, Authorization = "auth-marker" });
            var before = parameters.ToString();
            var detail = ActivityHistoryDetails.Json(parameters);
            Assert.Equal(before, parameters.ToString());
            foreach (var raw in new[] { code, "key-marker", "token-marker", "password-marker", "auth-marker" }) Assert.DoesNotContain(raw, detail);
            var metadata = JObject.Parse(detail)["commands"][0]["params"]["source_code"];
            Assert.Equal(code.Length, metadata.Value<int>("length"));
            Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))).ToLowerInvariant(), metadata.Value<string>("sha256"));
        }

        [Theory]
        [InlineData("send_code", true)]
        [InlineData("send_code", false)]
        [InlineData("batch_execute", true)]
        [InlineData("batch_execute", false)]
        [InlineData("run_baked_tool", false)]
        [InlineData("apply_bake", true)]
        public void Code_capable_output_and_error_echoes_are_not_retained(string command, bool success)
        {
            var history = new ActivityHistory();
            var vm = new McpToastViewModel { CommandName = command, Title = "Tool", Summary = "source-echo-marker", Success = success };
            history.Record(vm, new JObject { ["code"] = "source-echo-marker" }, "\"source-echo-marker\"", "source-echo-marker");
            var entry = Assert.Single(history.Snapshot());
            Assert.Equal(ActivityHistoryDetails.CodeOutputOmitted, entry.Output);
            Assert.DoesNotContain("source-echo-marker", entry.What + entry.Input + entry.Output + entry.Summary);
        }

        [Fact]
        public void Unexpected_nested_code_also_suppresses_echoes()
        {
            var history = new ActivityHistory();
            history.Record(new McpToastViewModel { CommandName = "unknown", Summary = "body-marker" },
                JObject.Parse("{\"nested\":{\"code\":\"body-marker\"}}"), error: "body-marker");
            Assert.DoesNotContain("body-marker", history.Snapshot()[0].Output + history.Snapshot()[0].Summary);
        }

        [Fact]
        public void Error_paths_stack_frames_and_embedded_secrets_are_sanitized()
        {
            var history = new ActivityHistory();
            var error = "password=secret-marker\nC:\\Users\\Private Name\\file.dwg\n   at Handler.Execute()";
            history.Record(ToastContentBuilder.BuildCompleted("list_layers", null, false, error, null), error: error);
            var entry = Assert.Single(history.Snapshot());
            foreach (var raw in new[] { "secret-marker", "Private Name", "Handler.Execute" }) Assert.DoesNotContain(raw, entry.What + entry.Summary + entry.Output);
            Assert.Contains("<path>", entry.Output);
            var nested = ActivityHistoryDetails.Result("{\"payload\":\"{\\\"password\\\":\\\"hidden-marker\\\"}\"}");
            Assert.DoesNotContain("hidden-marker", nested);
        }

        [Fact]
        public void Binary_and_invalid_results_are_explicitly_unavailable()
        {
            var output = ActivityHistoryDetails.Result("{\"image_base64\":\"binary-marker\",\"image_data\":\"data-marker\",\"payload\":\"data:image/png;base64,other-marker\"}");
            foreach (var raw in new[] { "binary-marker", "data-marker", "other-marker" }) Assert.DoesNotContain(raw, output);
            Assert.Contains("binary omitted", output);
            Assert.Contains("invalid result JSON", ActivityHistoryDetails.Result("malformed token=raw-marker"));
            Assert.DoesNotContain("raw-marker", ActivityHistoryDetails.Result("malformed token=raw-marker"));
            Assert.Empty(ActivityHistoryDetails.Result(null));
            Assert.Empty(ActivityHistoryDetails.Json(JValue.CreateNull()));
        }

        [Fact]
        public void Detail_character_node_and_depth_limits_are_explicit_and_preserve_capacity()
        {
            var output = ActivityHistoryDetails.Json(new JValue(new string('x', 20000)));
            Assert.True(output.Length <= ActivityHistoryDetails.MaxCharacters);
            Assert.Contains("session detail limit", output);
            var wide = ActivityHistoryDetails.Json(new JArray(Enumerable.Range(0, 10000)));
            Assert.Contains("session detail limit", wide);
            Assert.True(wide.Length <= ActivityHistoryDetails.MaxCharacters);
            JToken deep = new JValue("deep-marker");
            for (var i = 0; i < 30; i++) deep = new JObject { ["nested"] = deep };
            Assert.DoesNotContain("deep-marker", ActivityHistoryDetails.Json(deep));
            Assert.True(ActivityHistoryDetails.MayEchoCode("unknown", deep));
            var history = new ActivityHistory(2);
            for (var i = 0; i < 4; i++) history.Record(new McpToastViewModel { Summary = new string('s', 20000), Success = true }, new JValue(new string('x', 20000)), "\"" + new string('y', 20000) + "\"");
            Assert.Equal(2, history.Snapshot().Length);
            Assert.All(history.Snapshot(), e => { Assert.True(e.What.Length <= 4096); Assert.True(e.Input.Length <= 4096); Assert.True(e.Output.Length <= 4096); });
        }

        [Fact]
        public void Session_history_never_enables_persistent_logging_and_clear_keeps_existing_log_bytes()
        {
            var root = Path.Combine(Path.GetTempPath(), "dwg-history-log-" + Guid.NewGuid().ToString("N"));
            var path = Path.Combine(root, "calls.jsonl");
            try
            {
                var history = new ActivityHistory();
                var vm = ToastContentBuilder.BuildCompleted("list_layers", "{\"count\":2}", true, null, null);
                history.Record(vm, new JObject { ["layer"] = "A" }, "{\"count\":2}");
                new CallLogWriter(path, () => false).Append(new { tool = "list_layers" });
                Assert.False(Directory.Exists(root));
                new CallLogWriter(path, () => true).Append(new { tool = "list_layers" });
                var before = File.ReadAllBytes(path);
                history.Clear();
                new CallLogWriter(path, () => false).Append(new { tool = "another" });
                Assert.Equal(before, File.ReadAllBytes(path));
                Assert.Empty(history.Snapshot());
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
    }
}
