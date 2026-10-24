using Bimwright.Dwg.Plugin;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class BatchExecutorTests
    {
        [Fact]
        public void Paged_query_is_rejected_before_any_batch_item_but_legacy_query_is_unchanged()
        {
            var executed = 0;
            var result = BatchExecutor.Run(JObject.Parse("{\"commands\":[{\"cmd\":\"update_texts\",\"params\":{}},{\"cmd\":\"query_entities\",\"params\":{\"paged\":true}}]}"),
                (_, _) => { executed++; return CommandResult.Success(null); });
            Assert.False(result.Ok); Assert.Equal(0, executed); Assert.Contains("PAGED_QUERY_DIRECT_TOOL_ONLY", result.Error);
            var legacy = BatchExecutor.Run(JObject.Parse("{\"commands\":[{\"cmd\":\"query_entities\",\"params\":{\"limit\":2}}]}"),
                (_, _) => { executed++; return CommandResult.Success(null); });
            Assert.True(legacy.Ok); Assert.Equal(1, executed);
        }

        [Fact]
        public void Extended_count_is_also_direct_only_before_any_native_batch_item()
        {
            var executed = 0;
            var result = BatchExecutor.Run(JObject.Parse("{\"commands\":[{\"cmd\":\"update_texts\",\"params\":{}},{\"cmd\":\"count_entities\",\"params\":{\"bbox\":{\"min\":[0,0,0],\"max\":[1,1,1]}}}]}"),
                (_, _) => { executed++; return CommandResult.Success(null); });
            Assert.False(result.Ok); Assert.Equal(0, executed); Assert.Contains("PAGED_QUERY_DIRECT_TOOL_ONLY", result.Error);
        }
        [Fact]
        public void Run_RejectsLispBeforeExecutingAnyBatchItem()
        {
            var executed = 0;
            var result = BatchExecutor.Run(
                JObject.Parse("{\"commands\":[{\"cmd\":\"update_texts\",\"params\":{}},{\"cmd\":\"run_lisp\",\"params\":{\"code\":\"(princ)\"}}]}"),
                (cmd, parameters) => { executed++; return CommandResult.Success(null); });
            Assert.False(result.Ok);
            Assert.Equal(0, executed);
            Assert.Contains("run_lisp", result.Error);
        }

        [Fact]
        public void Run_RejectsNestedBatch()
        {
            var result = BatchExecutor.Run(
                JObject.Parse("{\"commands\":[{\"cmd\":\"batch_execute\",\"params\":{}}]}"),
                (cmd, parameters) => CommandResult.Success(new { }));

            Assert.False(result.Ok);
            Assert.Contains("nested", result.Error);
        }

        [Fact]
        public void Run_RejectsRunBakedToolInsideBatch()
        {
            var result = BatchExecutor.Run(
                JObject.Parse("{\"commands\":[{\"cmd\":\"run_baked_tool\",\"params\":{}}]}"),
                (cmd, parameters) => CommandResult.Success(new { }));

            Assert.False(result.Ok);
            Assert.Contains("run_baked_tool", result.Error);
        }

        [Fact]
        public void Run_PreflightsForbiddenCommandsBeforeExecutingAnyItem()
        {
            var executed = 0;

            var result = BatchExecutor.Run(
                JObject.Parse("{\"commands\":[{\"cmd\":\"update_texts\",\"params\":{}},{\"cmd\":\"run_baked_tool\",\"params\":{}}]}"),
                (cmd, parameters) =>
                {
                    executed++;
                    return CommandResult.Success(new { });
                });

            Assert.False(result.Ok);
            Assert.Equal(0, executed);
            Assert.Contains("run_baked_tool", result.Error);
        }

        [Fact]
        public void Run_DetectsPartialFailureAndKeepsPerItemResults()
        {
            var result = BatchExecutor.Run(
                JObject.Parse("{\"commands\":[{\"cmd\":\"ok\",\"params\":{}},{\"cmd\":\"fail\",\"params\":{}}]}"),
                (cmd, parameters) => cmd == "fail"
                    ? CommandResult.Fail("bad")
                    : CommandResult.Success(new { value = 1 }));

            Assert.True(result.Ok);
            var json = JObject.FromObject(result.Result);
            Assert.True(json.Value<bool>("partial_failure"));
            Assert.False(json.Value<bool>("transactional"));
            Assert.Equal(2, ((JArray)json["results"]).Count);
            Assert.Equal("bad", json["results"][1].Value<string>("error"));
        }

        [Fact]
        public void Run_DoesNotTreatOtherCommandPayloadStatusAsExecutionFailure()
        {
            var result = BatchExecutor.Run(
                JObject.Parse("{\"commands\":[{\"cmd\":\"query\",\"params\":{}}]}"),
                (cmd, parameters) => CommandResult.Success(new { ok = false, value = 42 }));

            Assert.True(result.Ok);
            var batch = JObject.FromObject(result.Result);
            Assert.False(batch.Value<bool>("partial_failure"));
            Assert.True(batch["results"][0].Value<bool>("ok"));
            Assert.False(batch["results"][0]["result"].Value<bool>("ok"));
        }
    }
}
