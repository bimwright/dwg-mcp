using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Bimwright.Dwg.Plugin.Views.Toast;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;


namespace Bimwright.Dwg.Plugin
{
    public partial class CommandDispatcher
    {
        private Dictionary<string, IAcadCommand> _commands;
        private readonly string _authToken;
        private readonly Action<McpToastViewModel> _notify;

        public static bool SendCodeEnabled { get; private set; }

        public static void SetSendCodeEnabled(bool enabled)
        {
            SendCodeEnabled = enabled;
        }

        public CommandDispatcher(string authToken, Action<McpToastViewModel> notify = null)
        {
            _authToken = authToken;
            _notify = notify;
            RegisterCommands();
        }

        public string Dispatch(string requestLine)
        {
            string id = null;
            string cmd = null, drawing = null;
            bool notify = false;
            var elapsed = Stopwatch.StartNew();
            try
            {
                var request = JObject.Parse(requestLine);
                id = (string)request["id"];

                var auth = (string)request["auth"];
                if (!string.Equals(auth, _authToken, StringComparison.Ordinal))
                    return ErrorJson(id, "unauthorized");

                cmd = request["cmd"]?.Type == JTokenType.String ? (string)request["cmd"] : null;
                if (string.IsNullOrWhiteSpace(cmd)) return ErrorJson(id, "cmd must be a non-empty string");
                notify = _commands.ContainsKey(cmd);
                var parameters = request["params"];

                var preflight = ValidateCommand(cmd, parameters, out _);
                if (!preflight.Ok)
                    return Complete(SerializeResponse(id, cmd, preflight));

                var result = DocumentInvoker.Invoke(doc =>
                {
                    try { drawing = Path.GetFileName(doc.Name); } catch { /* Optional UI context cannot fail a command. */ }
                    return ExecuteCommand(doc, cmd, parameters);
                });

                return Complete(SerializeResponse(id, cmd, result));
            }
            catch (Exception ex)
            {
                return Complete(ErrorJson(id, ex.Message));
            }

            // DocumentInvoker has released its document lock before publishing a DTO.
            string Complete(string response) => ToastCompletion.Publish(response, cmd, elapsed.ElapsedMilliseconds, drawing, notify ? _notify : null);
        }

        private CommandResult ExecuteCommand(Autodesk.AutoCAD.ApplicationServices.Document doc, string cmd, JToken parameters)
        {
            var preflight = ValidateCommand(cmd, parameters, out var handler);
            if (!preflight.Ok)
                return preflight;
            return handler.Execute(doc, parameters);
        }

        private CommandResult ValidateCommand(string cmd, JToken parameters, out IAcadCommand handler)
        {
            handler = null;
            // Reject before DocumentInvoker/LockDocument, including direct wire
            // callers and older servers. MCPENABLECODE cannot authorize LISP.
            if (string.Equals(cmd, "run_lisp", StringComparison.Ordinal))
                return CommandResult.Fail(LispExecutionPolicy.Refusal);

            if (string.Equals(cmd, "send_code", StringComparison.Ordinal) && !SendCodeEnabled)
                return CommandResult.Fail("code execution is disabled for this AutoCAD session. Run MCPENABLECODE in AutoCAD to re-enable.");

            if (!_commands.TryGetValue(cmd, out handler))
                return CommandResult.Fail($"unknown command: {cmd}");

            var validation = SchemaValidator.Validate(cmd, parameters, handler.Schema);
            if (!validation.Ok)
                return CommandResult.Fail(validation.Error);

            return CommandResult.Success(null);
        }

        private static string SerializeResponse(string id, string cmd, CommandResult result)
        {
            return JsonConvert.SerializeObject(new
            {
                id,
                ok = result.Ok,
                result = result.Ok ? McpResponsePrivacy.FilterResult(cmd, result.Result) : null,
                error = result.Ok ? null : McpResponsePrivacy.SanitizeError(result.Error)
            });
        }

        public static string ErrorJson(string id, string error) =>
            JsonConvert.SerializeObject(new { id, ok = false, error = McpResponsePrivacy.SanitizeError(error) });
    }
}
