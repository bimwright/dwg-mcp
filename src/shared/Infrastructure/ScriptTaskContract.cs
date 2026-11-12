using System;
using System.Collections.Generic;
using Bimwright.Dwg.Plugin.Tasks;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Tasks
{
    /// <summary>Explicit script task result for result_contract=task_v1 (same API as rvt-mcp).</summary>
    public sealed class McpTaskResult
    {
        public bool IsPassed { get; }
        public object Value { get; }
        public string ErrorCode { get; }
        public string ErrorMessage { get; }
        public object Details { get; }

        private McpTaskResult(bool passed, object value, string code, string message, object details)
        {
            IsPassed = passed; Value = value; ErrorCode = code; ErrorMessage = message; Details = details;
        }

        public static McpTaskResult Pass(object value = null) => new McpTaskResult(true, value, null, null, null);

        public static McpTaskResult Fail(string code, string message, object details = null)
            => new McpTaskResult(false, null, string.IsNullOrWhiteSpace(code) ? ScriptTaskContract.TaskFailed : code, message, details);
    }
}

namespace Bimwright.Dwg.Plugin
{
    /// <summary>Host-free task_v1 normalization of a send_code payload.</summary>
    public static class ScriptTaskContract
    {
        public const int Version = 1;
        public const string Legacy = "legacy";
        public const string TaskV1 = "task_v1";
        public const string Import = "Bimwright.Dwg.Plugin.Tasks";
        public const string TaskFailed = "TASK_FAILED";
        public const string TaskResultRequired = "TASK_RESULT_REQUIRED";
        private const int CodeLimit = 128, MessageLimit = 512;

        public static bool IsTaskV1(JToken parameters)
            => parameters is JObject obj && obj["result_contract"]?.Type == JTokenType.String &&
               obj.Value<string>("result_contract") == TaskV1;

        /// <summary>
        /// Legacy payloads are unchanged. Under task_v1, ok is true only when the script ran without
        /// error and returned McpTaskResult.Pass; result becomes Pass value or Fail details.
        /// </summary>
        public static void Apply(Dictionary<string, object> payload, bool taskV1, Func<string, string> sanitize)
        {
            if (!taskV1) return;
            payload.TryGetValue("result", out var returned);
            var ok = payload.TryGetValue("ok", out var rawOk) && rawOk is bool b && b;
            var executed = payload.TryGetValue("executed", out var rawExecuted) && rawExecuted is bool e && e;
            var task = new Dictionary<string, object> { ["contract"] = TaskV1 };
            if (returned is McpTaskResult result)
            {
                payload["result"] = result.IsPassed ? result.Value : result.Details;
                task["status"] = result.IsPassed ? "passed" : "failed";
                if (!result.IsPassed)
                {
                    var code = result.ErrorCode;
                    var message = sanitize(result.ErrorMessage ?? "");
                    if (message.Length > MessageLimit) message = message.Substring(0, MessageLimit);
                    var error = new Dictionary<string, object> { ["code"] = code.Length > CodeLimit ? code.Substring(0, CodeLimit) : code, ["message"] = message };
                    if (code.Length > CodeLimit) error["code_truncated"] = true;
                    task["error"] = error;
                    if (ok) payload["error"] = error["code"] + ": " + message;
                    payload["ok"] = false;
                }
            }
            else
            {
                task["status"] = "not_reported";
                // Compile failures, compile_only and runtime errors keep their own ok/error.
                if (ok && executed)
                {
                    var message = "Return McpTaskResult.Pass(value) or McpTaskResult.Fail(code, message) under result_contract=task_v1.";
                    task["error"] = new Dictionary<string, object> { ["code"] = TaskResultRequired, ["message"] = message };
                    payload["ok"] = false;
                    payload["error"] = TaskResultRequired + ": " + message;
                }
            }
            payload["_task"] = task;
        }
    }
}
