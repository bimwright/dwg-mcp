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

                if (!result.Ok)
                {
                    partialFailure = true;
                }

                results.Add(new
                {
                    cmd,
                    ok = result.Ok,
                    result = result.Ok ? result.Result : null,
                    error = result.Ok ? null : ErrorSanitizer.Sanitize(result.Error)
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

                if (string.Equals(cmd, "run_baked_tool", StringComparison.Ordinal))
                {
                    return CommandResult.Fail("run_baked_tool is not allowed inside batch_execute");
                }
            }

            return CommandResult.Success(null);
        }
    }
}
