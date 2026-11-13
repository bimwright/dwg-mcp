using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin
{
    /// <summary>Outer drawing transaction for an atomic batch; disposing without Commit aborts it.</summary>
    public interface IBatchTransaction : IDisposable
    {
        void Commit();
        void Abort();
    }

    public static class BatchExecutor
    {
        public static CommandResult Run(JToken parameters, Func<string, JToken, CommandResult> execute,
            Func<IBatchTransaction> beginTransaction = null)
        {
            if (execute == null)
            {
                throw new ArgumentNullException(nameof(execute));
            }

            var validation = SchemaValidator.Validate("batch_execute", parameters, CommandSchemas.BatchExecute);
            if (!validation.Ok)
            {
                return CommandResult.Fail(validation.Error);
            }

            var commands = (JArray)parameters["commands"];
            var preflight = PreflightCommands(commands);
            if (!preflight.Ok)
            {
                return preflight;
            }

            var atomic = BatchContract.IsAtomic(parameters);
            if (!atomic)
                return RunSteps(commands, execute, false, out _);

            var unsupported = new List<string>();
            foreach (var item in commands)
            {
                var cmd = item?["cmd"]?.Value<string>();
                if (!BatchContract.SupportsAtomic(cmd) && !unsupported.Contains(cmd)) unsupported.Add(cmd);
            }
            if (unsupported.Count > 0)
                return CommandResult.Fail("ATOMIC_UNSUPPORTED_COMMANDS: " + string.Join(", ", unsupported) +
                    " cannot run inside one AutoCAD transaction. Use atomic=false or call them separately. The batch was not started.");
            if (beginTransaction == null)
                return CommandResult.Fail("ATOMIC_BATCH_UNAVAILABLE: this host cannot open an outer transaction. The batch was not started.");

            IBatchTransaction transaction;
            try { transaction = beginTransaction() ?? throw new InvalidOperationException("no transaction returned"); }
            catch (Exception ex)
            {
                return CommandResult.Fail("ATOMIC_BATCH_UNAVAILABLE: " + ErrorSanitizer.Sanitize(ex.Message) + ". The batch was not started.");
            }

            using (transaction)
            {
                var steps = RunSteps(commands, execute, true, out var failed);
                try
                {
                    if (failed) transaction.Abort();
                    else transaction.Commit();
                }
                catch (Exception ex)
                {
                    return CommandResult.Fail((failed ? "ATOMIC_BATCH_ABORT_FAILED: " : "ATOMIC_BATCH_COMMIT_FAILED: ") +
                        ErrorSanitizer.Sanitize(ex.Message) + ". Drawing state is unknown; inspect it before retrying.");
                }
                return failed ? MarkRolledBack(steps) : steps;
            }
        }

        private static CommandResult RunSteps(JArray commands, Func<string, JToken, CommandResult> execute, bool atomic, out bool partialFailureOut)
        {
            var results = new List<object>();
            var partialFailure = false;
            var index = 0;

            foreach (var item in commands)
            {
                var cmd = item?["cmd"]?.Value<string>();
                var itemParams = item?["params"] ?? new JObject();
                if (atomic && partialFailure)
                {
                    results.Add(new
                    {
                        index = index++, cmd, command_ok = false, ok = false, outcome = "skipped",
                        output_verified = (bool?)null, mutation_applied = (bool?)false, result = (object)null,
                        error = "not run: an earlier step failed in this atomic batch"
                    });
                    continue;
                }

                CommandResult result;
                try
                {
                    result = execute(cmd, itemParams) ?? CommandResult.Fail("command returned null result");
                }
                catch (Exception ex)
                {
                    result = CommandResult.Fail(ErrorSanitizer.Sanitize(ex.Message));
                }

                var itemOk = result.Ok;
                var itemError = result.Error;
                var outcome = itemOk ? "succeeded" : "failed";
                bool? outputVerified = null;
                if (itemOk && (cmd == "export_dxf" || cmd == "save_drawing" || cmd == "capture_view_image" || cmd == "inspect_view_region" || cmd == "restore_view"))
                {
                    var output = result.Result == null ? null : JToken.FromObject(result.Result) as JObject;
                    outputVerified = (output?["file_receipt"] as JObject)?.Value<bool?>("verified");
                    if (outputVerified != true) outcome = "completed_output_unverified";
                }
                // send_code keeps execution errors and stdout inside its payload.
                // Its successful command wrapper does not mean the C# succeeded.
                if (itemOk && string.Equals(cmd, "send_code", StringComparison.Ordinal))
                {
                    var script = result.Result == null ? null : JToken.FromObject(result.Result) as JObject;
                    if (script?["ok"]?.Type != JTokenType.Boolean)
                    {
                        itemOk = false; outcome = "unknown";
                        itemError = "send_code did not report a boolean execution status; inspect effects before retrying";
                    }
                    else if (script.Value<bool>("ok") == false)
                    {
                        itemOk = false;
                        outcome = "failed";
                        itemError = script.Value<string>("error") ?? "send_code execution failed";
                    }
                }

                if (atomic && itemOk && AtomicItemError(cmd, result.Result) is string entityError)
                {
                    itemOk = false;
                    outcome = "failed";
                    itemError = entityError;
                }

                if (!itemOk)
                {
                    partialFailure = true;
                }

                results.Add(new
                {
                    index = index++,
                    cmd,
                    command_ok = result.Ok,
                    ok = itemOk,
                    outcome,
                    output_verified = outputVerified,
                    mutation_applied = (bool?)null, // A handler error does not prove rollback of earlier effects.
                    result = result.Ok ? result.Result : null,
                    error = itemOk ? null : ErrorSanitizer.Sanitize(itemError)
                });
            }

            partialFailureOut = partialFailure;
            if (atomic)
                return CommandResult.Success(new
                {
                    transactional = true,
                    atomic = true,
                    rolled_back = false,
                    rollback = "one outer AutoCAD transaction; a failed step aborts it and later steps are skipped",
                    partial_failure = partialFailure,
                    results
                });
            return CommandResult.Success(new
            {
                transactional = false,
                rollback = "logical batch only; atomic=false, earlier effects remain after a later failure",
                partial_failure = partialFailure,
                results
            });
        }

        // These handlers report per-item status inside a successful command wrapper.
        // Inspect only their status fields, since other drawing data can also contain "ok".
        private static string AtomicItemError(string cmd, object result)
        {
            if (result == null) return null;

            JArray items;
            switch (cmd)
            {
                case "change_layer":
                case "change_color":
                case "move_entities":
                case "rotate_entities":
                case "scale_entities":
                case "copy_entities":
                case "erase_entities":
                case "offset_entities":
                    items = (JToken.FromObject(result) as JObject)?["results"] as JArray;
                    break;
                case "update_texts":
                    items = JToken.FromObject(result) as JArray;
                    break;
                case "get_entity_properties":
                    items = (JToken.FromObject(result) as JObject)?["entities"] as JArray;
                    break;
                case "set_block_attributes":
                    var update = (JToken.FromObject(result) as JObject)?["attribute_update"] as JObject;
                    // Missing tags are optional unless the caller requested strict tags.
                    if (update?.Value<bool?>("strict_tags") != true) return null;
                    items = update["updates"] as JArray;
                    break;
                default:
                    return null;
            }

            if (items == null) return null;
            foreach (var item in items)
            {
                if (item is JObject entry && entry["ok"]?.Type == JTokenType.Boolean && !entry.Value<bool>("ok"))
                    return entry.Value<string>("error") ?? "an item reported failure";
            }
            return null;
        }

        // After Abort every executed step's drawing effect is undone, including steps that succeeded.
        private static CommandResult MarkRolledBack(CommandResult steps)
        {
            var batch = JObject.FromObject(steps.Result);
            batch["rolled_back"] = true;
            foreach (var step in (JArray)batch["results"])
            {
                if (step.Value<string>("outcome") == "skipped") continue;
                step["mutation_applied"] = false;
                if (step.Value<bool>("ok"))
                {
                    step["ok"] = false;
                    step["outcome"] = "rolled_back";
                    step["error"] = "rolled back: a later step failed in this atomic batch";
                }
            }
            return CommandResult.Success(batch);
        }

        private static CommandResult PreflightCommands(JArray commands)
        {
            foreach (var item in commands)
            {
                var cmd = item?["cmd"]?.Value<string>();
                if (ScriptInputContract.RequiresDirectCall(cmd, item?["params"]))
                    return CommandResult.Fail("SCRIPT_INPUTS_DIRECT_TOOL_ONLY: call enhanced send_code directly. The batch was not started.");
                if (string.IsNullOrWhiteSpace(cmd))
                {
                    return CommandResult.Fail("batch command missing required field 'cmd'");
                }

                if (string.Equals(cmd, "batch_execute", StringComparison.Ordinal))
                {
                    return CommandResult.Fail("nested batch_execute is not allowed");
                }

                if (DrawingSessionContract.IsStandaloneDrawingCommand(cmd))
                {
                    return CommandResult.Fail(DrawingSessionContract.StandaloneError(cmd));
                }

                if (string.Equals(cmd, "run_baked_tool", StringComparison.Ordinal))
                {
                    return CommandResult.Fail("run_baked_tool is not allowed inside batch_execute");
                }

                if (EntityQueryOptions.RequiresDirectCall(cmd, item?["params"]))
                    return CommandResult.Fail("PAGED_QUERY_DIRECT_TOOL_ONLY: call spatial/scoped/paged read tools directly; verified final MCP delivery is required. The batch was not started.");

                // Reject the entire batch before any earlier item can mutate the
                // drawing. Direct run_lisp is also blocked; do not suggest a bypass.
                if (string.Equals(cmd, "run_lisp", StringComparison.Ordinal))
                {
                    return CommandResult.Fail("run_lisp is not allowed inside batch_execute. LISP execution is blocked in this build; use dwg_inspect_lisp for analysis only.");
                }
            }

            return CommandResult.Success(null);
        }
    }
}
