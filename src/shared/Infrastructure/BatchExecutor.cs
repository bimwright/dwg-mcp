using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin
{
    public static class BatchExecutor
    {
        public static CommandResult Run(JToken parameters, Func<string, JToken, CommandResult> execute)
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

            var results = new List<object>();
            var partialFailure = false;
            var index = 0;

            foreach (var item in commands)
            {
                var cmd = item?["cmd"]?.Value<string>();
                var itemParams = item?["params"] ?? new JObject();

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

            return CommandResult.Success(new
            {
                transactional = false,
                rollback = "logical batch only; AutoCAD undo grouping is not enabled in this build",
                partial_failure = partialFailure,
                results
            });
        }

        private static CommandResult PreflightCommands(JArray commands)
        {
            foreach (var item in commands)
            {
                var cmd = item?["cmd"]?.Value<string>();
                if (string.IsNullOrWhiteSpace(cmd))
                {
                    return CommandResult.Fail("batch command missing required field 'cmd'");
                }

                if (string.Equals(cmd, "batch_execute", StringComparison.Ordinal))
                {
                    return CommandResult.Fail("nested batch_execute is not allowed");
                }

                if (string.Equals(cmd, "open_drawing", StringComparison.Ordinal) ||
                    string.Equals(cmd, "list_recent_drawings", StringComparison.Ordinal))
                {
                    return CommandResult.Fail(cmd + " is a standalone session command and is not allowed inside batch_execute.");
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
