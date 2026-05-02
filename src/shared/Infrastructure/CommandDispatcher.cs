using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Bimwright.Dwg.Plugin.Handlers;

namespace Bimwright.Dwg.Plugin
{
    public class CommandDispatcher
    {
        private readonly Dictionary<string, IAcadCommand> _commands;
        private readonly string _authToken;

        public CommandDispatcher(string authToken)
        {
            _authToken = authToken;
            _commands = new Dictionary<string, IAcadCommand>
            {
                { "get_selected_texts",      new GetSelectedTextsHandler() },
                { "update_texts",            new UpdateTextsHandler() },
                { "send_code",               new SendCodeHandler() },
                { "apply_unicode_style",     new ApplyUnicodeStyleHandler() },
                { "collapse_and_rewrite",    new CollapseAndRewriteHandler() },
                { "translate_and_rewrite",   new TranslateAndRewriteHandler() },
            };
        }

        public string Dispatch(string requestLine)
        {
            string id = null;
            try
            {
                var request = JObject.Parse(requestLine);
                id = (string)request["id"];

                var auth = (string)request["auth"];
                if (!string.Equals(auth, _authToken, StringComparison.Ordinal))
                    return ErrorJson(id, "unauthorized");

                var cmd = (string)request["cmd"];
                var parameters = request["params"];

                if (!_commands.TryGetValue(cmd, out var handler))
                    return ErrorJson(id, $"unknown command: {cmd}");

                var result = DocumentInvoker.Invoke(doc => handler.Execute(doc, parameters));

                return JsonConvert.SerializeObject(new
                {
                    id,
                    ok = result.Ok,
                    result = result.Ok ? result.Result : null,
                    error = result.Ok ? null : result.Error
                });
            }
            catch (Exception ex)
            {
                return ErrorJson(id, ex.Message);
            }
        }

        public static string ErrorJson(string id, string error) =>
            JsonConvert.SerializeObject(new { id, ok = false, error });
    }
}
