using Bimwright.Dwg.Plugin;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class BatchExecutorTests
    {
        [Theory]
        [InlineData(true, "succeeded")]
        [InlineData(false, "completed_output_unverified")]
        [InlineData(null, "completed_output_unverified")]
        public void File_output_status_does_not_claim_verified_artifacts_from_command_success_alone(bool? verified, string outcome)
        {
            var result = BatchExecutor.Run(JObject.Parse("{\"commands\":[{\"cmd\":\"export_dxf\"}]}"),
                (_, _) => CommandResult.Success(new { output_path = "file.dxf", file_receipt = new { verified } }));
            var step = JObject.FromObject(result.Result)["results"][0];
            Assert.True(step.Value<bool>("command_ok")); Assert.Equal(outcome, step.Value<string>("outcome"));
            Assert.Equal(verified, step.Value<bool?>("output_verified"));
        }
        [Fact]
        public void Per_step_status_preserves_prior_effects_after_errors_without_claiming_atomic_rollback()
        {
            var effects = new System.Collections.Generic.List<string>();
            var result = BatchExecutor.Run(JObject.Parse("{\"commands\":[{\"cmd\":\"first\"},{\"cmd\":\"throw\"},{\"cmd\":\"last\"}]}"),
                (cmd, _) => { effects.Add(cmd); if (cmd == "throw") throw new System.Exception("after committed effect"); return CommandResult.Success(new { handle = cmd }); });
            Assert.Equal(new[] { "first", "throw", "last" }, effects);
            var batch = JObject.FromObject(result.Result); Assert.True(batch.Value<bool>("partial_failure")); Assert.False(batch.Value<bool>("transactional"));
            Assert.Equal("succeeded", batch["results"][0].Value<string>("outcome")); Assert.Equal("failed", batch["results"][1].Value<string>("outcome"));
            Assert.Equal("succeeded", batch["results"][2].Value<string>("outcome")); Assert.Equal(2, batch["results"][2].Value<int>("index"));
            Assert.Equal(JTokenType.Null, batch["results"][1]["mutation_applied"].Type);
        }

        [Theory]
        [InlineData("{\"ok\":false,\"error\":\"script failed\"}", "failed")]
        [InlineData("{\"ok\":true,\"result\":{\"ok\":false}}", "succeeded")]
        [InlineData("{\"ok\":\"true\"}", "unknown")]
        [InlineData("null", "unknown")]
        public void Script_wrapper_and_execution_status_are_separate_and_unknown_status_stays_unknown(string payload, string expected)
        {
            var batch = JObject.FromObject(BatchExecutor.Run(JObject.Parse("{\"commands\":[{\"cmd\":\"send_code\"}]}"),
                (_, _) => CommandResult.Success(JToken.Parse(payload))).Result);
            var step = batch["results"][0]; Assert.True(step.Value<bool>("command_ok")); Assert.Equal(expected, step.Value<string>("outcome"));
            Assert.Equal(expected == "succeeded", step.Value<bool>("ok"));
        }
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

        private sealed class FakeTransaction : IBatchTransaction
        {
            internal int Commits, Aborts, Disposes;
            internal bool ThrowOnCommit;
            public void Commit() { if (ThrowOnCommit) throw new System.Exception("eLockViolation"); Commits++; }
            public void Abort() => Aborts++;
            public void Dispose() => Disposes++;
        }

        private static JObject Atomic(params string[] cmds)
        {
            var commands = new JArray();
            foreach (var cmd in cmds) commands.Add(new JObject { ["cmd"] = cmd, ["params"] = new JObject() });
            return new JObject { ["commands"] = commands, ["atomic"] = true };
        }

        [Fact]
        public void Atomic_failure_aborts_once_marks_earlier_steps_rolled_back_and_skips_later_steps()
        {
            var tx = new FakeTransaction();
            var effects = new System.Collections.Generic.List<string>();
            var result = BatchExecutor.Run(Atomic("create_line", "create_circle", "erase_entities"),
                (cmd, _) => { effects.Add(cmd); return cmd == "create_circle" ? CommandResult.Fail("radius must be positive") : CommandResult.Success(new { handle = "2A" }); },
                () => tx);

            Assert.True(result.Ok);
            Assert.Equal(new[] { "create_line", "create_circle" }, effects);
            Assert.Equal(1, tx.Aborts); Assert.Equal(0, tx.Commits); Assert.Equal(1, tx.Disposes);
            var batch = JObject.FromObject(result.Result);
            Assert.True(batch.Value<bool>("transactional")); Assert.True(batch.Value<bool>("rolled_back")); Assert.True(batch.Value<bool>("partial_failure"));
            Assert.Equal("rolled_back", batch["results"][0].Value<string>("outcome"));
            Assert.False(batch["results"][0].Value<bool>("ok")); Assert.True(batch["results"][0].Value<bool>("command_ok"));
            Assert.Equal("failed", batch["results"][1].Value<string>("outcome"));
            Assert.Equal("radius must be positive", batch["results"][1].Value<string>("error"));
            Assert.Equal("skipped", batch["results"][2].Value<string>("outcome"));
            Assert.Equal(2, batch["results"][2].Value<int>("index"));
            foreach (var step in batch["results"]) Assert.False(step.Value<bool>("mutation_applied"));
        }

        [Fact]
        public void Atomic_exception_in_a_step_also_aborts()
        {
            var tx = new FakeTransaction();
            var result = BatchExecutor.Run(Atomic("create_line", "move_entities"),
                (cmd, _) => cmd == "move_entities" ? throw new System.InvalidOperationException("eWasErased") : CommandResult.Success(null), () => tx);
            Assert.Equal(1, tx.Aborts); Assert.Equal(0, tx.Commits);
            Assert.True(JObject.FromObject(result.Result).Value<bool>("rolled_back"));
        }

        [Fact]
        public void Atomic_success_commits_once_and_keeps_step_results()
        {
            var tx = new FakeTransaction();
            var result = BatchExecutor.Run(Atomic("create_layer", "create_line", "query_entities"),
                (cmd, _) => CommandResult.Success(new { handle = cmd }), () => tx);
            Assert.Equal(1, tx.Commits); Assert.Equal(0, tx.Aborts); Assert.Equal(1, tx.Disposes);
            var batch = JObject.FromObject(result.Result);
            Assert.True(batch.Value<bool>("transactional")); Assert.False(batch.Value<bool>("rolled_back")); Assert.False(batch.Value<bool>("partial_failure"));
            Assert.All(batch["results"], step => Assert.Equal("succeeded", step.Value<string>("outcome")));
            Assert.Equal("create_line", batch["results"][1]["result"].Value<string>("handle"));
        }

        [Theory]
        [InlineData("zoom_extents")]
        [InlineData("send_code")]
        [InlineData("save_drawing")]
        [InlineData("export_dxf")]
        [InlineData("set_system_variable")]
        [InlineData("purge_drawing")]
        [InlineData("translate_and_rewrite")]
        [InlineData("get_selected_texts")]
        public void Atomic_refuses_unsupported_commands_before_any_step_or_transaction(string unsupported)
        {
            var executed = 0; var begun = 0;
            var result = BatchExecutor.Run(Atomic("create_line", unsupported),
                (_, _) => { executed++; return CommandResult.Success(null); }, () => { begun++; return new FakeTransaction(); });
            Assert.False(result.Ok); Assert.Equal(0, executed); Assert.Equal(0, begun);
            Assert.Contains("ATOMIC_UNSUPPORTED_COMMANDS: " + unsupported, result.Error);
        }

        [Fact]
        public void Atomic_without_host_transaction_is_refused_before_any_step()
        {
            var executed = 0;
            var result = BatchExecutor.Run(Atomic("create_line"), (_, _) => { executed++; return CommandResult.Success(null); });
            Assert.False(result.Ok); Assert.Equal(0, executed); Assert.Contains("ATOMIC_BATCH_UNAVAILABLE", result.Error);
        }

        [Fact]
        public void Atomic_commit_failure_reports_unknown_state()
        {
            var tx = new FakeTransaction { ThrowOnCommit = true };
            var result = BatchExecutor.Run(Atomic("create_line"), (_, _) => CommandResult.Success(null), () => tx);
            Assert.False(result.Ok); Assert.Contains("ATOMIC_BATCH_COMMIT_FAILED", result.Error); Assert.Contains("unknown", result.Error);
            Assert.Equal(1, tx.Disposes);
        }

        [Fact]
        public void Atomic_must_be_boolean_and_false_keeps_legacy_behaviour()
        {
            var bad = BatchExecutor.Run(JObject.Parse("{\"commands\":[{\"cmd\":\"create_line\"}],\"atomic\":\"yes\"}"), (_, _) => CommandResult.Success(null));
            Assert.False(bad.Ok);
            var begun = 0;
            var legacy = BatchExecutor.Run(JObject.Parse("{\"commands\":[{\"cmd\":\"zoom_extents\"}],\"atomic\":false}"),
                (_, _) => CommandResult.Success(null), () => { begun++; return new FakeTransaction(); });
            Assert.True(legacy.Ok); Assert.Equal(0, begun);
            Assert.False(JObject.FromObject(legacy.Result).Value<bool>("transactional"));
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
