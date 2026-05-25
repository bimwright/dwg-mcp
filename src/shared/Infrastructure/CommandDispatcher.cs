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

        public static bool SendCodeEnabled { get; private set; }

        public static void SetSendCodeEnabled(bool enabled)
        {
            SendCodeEnabled = enabled;
        }

        public CommandDispatcher(string authToken)
        {
            _authToken = authToken;
            _commands = new Dictionary<string, IAcadCommand>
            {
                { "get_drawing_info",        new GetDrawingInfoHandler() },
                { "get_entity_properties",   new GetEntityPropertiesHandler() },
                { "get_selected_texts",      new GetSelectedTextsHandler() },
                { "list_layers",             new ListLayersHandler() },
                { "create_layer",            new CreateLayerHandler() },
                { "create_line",             new CreateLineHandler() },
                { "create_circle",           new CreateCircleHandler() },
                { "change_layer",            new ChangeLayerHandler() },
                { "update_texts",            new UpdateTextsHandler() },
                { "send_code",               new SendCodeHandler() },
                { "apply_unicode_style",     new ApplyUnicodeStyleHandler() },
                { "collapse_and_rewrite",    new CollapseAndRewriteHandler() },
                { "translate_and_rewrite",   new TranslateAndRewriteHandler() },
                { "list_baked_tools",        new ListBakedToolsHandler() },
            };
            _commands.Add("apply_bake", new ApplyBakeSuggestionHandler((cmd, parameters) => ValidateCommand(cmd, parameters, out _)));
            _commands.Add("batch_execute", new BatchExecuteHandler(ExecuteCommand));
            _commands.Add("run_baked_tool", new RunBakedToolHandler(ExecuteCommand));
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

                var preflight = ValidateCommand(cmd, parameters, out _);
                if (!preflight.Ok)
                    return SerializeResponse(id, cmd, preflight);

                var result = DocumentInvoker.Invoke(doc => ExecuteCommand(doc, cmd, parameters));

                return SerializeResponse(id, cmd, result);
            }
            catch (Exception ex)
            {
                return ErrorJson(id, ex.Message);
            }
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
            if (string.Equals(cmd, "send_code", StringComparison.Ordinal) && !SendCodeEnabled)
                return CommandResult.Fail("send_code is disabled. Run MCPENABLECODE in AutoCAD and start the MCP server with --enable-send-code to opt in.");

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
