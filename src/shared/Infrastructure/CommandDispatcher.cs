using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Bimwright.Dwg.Plugin.Handlers;
using Bimwright.Dwg.Plugin.ToolCatalog;

namespace Bimwright.Dwg.Plugin
{
    public class CommandDispatcher
    {
        private readonly Dictionary<string, IAcadCommand> _commands;
        private readonly string _authToken;
        private readonly Func<bool> _isAccepting;
        private readonly JObject _healthIdentity;

        public static bool SendCodeEnabled { get; private set; }

        public static void SetSendCodeEnabled(bool enabled)
        {
            SendCodeEnabled = enabled;
        }

        public CommandDispatcher(string authToken, Func<bool> isAccepting = null)
        {
            _authToken = authToken;
            _isAccepting = isAccepting;
            _healthIdentity = RuntimeIdentity.Capture(typeof(CommandDispatcher).Assembly, "plugin",
                int.Parse(PluginTarget.AutoCadYear, System.Globalization.CultureInfo.InvariantCulture));
            _commands = new Dictionary<string, IAcadCommand>
            {
                { "get_drawing_info",        new GetDrawingInfoHandler() },
                { "history_drawing_key",     new HistoryDrawingKeyHandler() },
                { "survey_change_impact",    new SurveyChangeImpactHandler() },
                { "get_drawing_profile",     new DrawingProfileHandler() },
                { "get_entity_properties",   new GetEntityPropertiesHandler() },
                { "get_selected_texts",      new GetSelectedTextsHandler() },
                { "list_layers",             new ListLayersHandler() },
                { "query_entities",          new QueryEntitiesHandler() },
                { "cluster_bands",           new ClusterBandsHandler() },
                { "count_entities",          new CountEntitiesHandler() },
                { "select_by_layer",         new SelectByLayerHandler() },
                { "select_by_type",          new SelectByTypeHandler() },
                { "create_layer",            new CreateLayerHandler() },
                { "create_line",             new CreateLineHandler() },
                { "create_circle",           new CreateCircleHandler() },
                { "attach_image",            new AttachImageHandler() },
                { "report_task_result",      new Bimwright.Dwg.Plugin.Reports.Acad.ReportTaskResultHandler() },
                { "create_point",            new CreatePointHandler() },
                { "create_polyline",         new CreatePolylineHandler() },
                { "create_rectangle",        new CreateRectangleHandler() },
                { "create_arc",              new CreateArcHandler() },
                { "create_ellipse",          new CreateEllipseHandler() },
                { "create_text",             new CreateTextHandler() },
                { "create_mtext",            new CreateMTextHandler() },
                { "create_leader",           new CreateLeaderHandler() },
                { "create_table",            new CreateTableHandler() },
                { "list_blocks",             new ListBlocksHandler() },
                { "get_block_attributes",    new GetBlockAttributesHandler() },
                { "insert_block",            new InsertBlockHandler() },
                { "set_block_attributes",    new SetBlockAttributesHandler() },
                { "explode_block",           new ExplodeBlockHandler() },
                { "create_linear_dimension", new CreateLinearDimensionHandler() },
                { "create_aligned_dimension", new CreateAlignedDimensionHandler() },
                { "create_radial_dimension", new CreateRadialDimensionHandler() },
                { "create_diameter_dimension", new CreateDiameterDimensionHandler() },
                { "change_layer",            new ChangeLayerHandler() },
                { "change_color",            new ChangeColorHandler() },
                { "move_entities",           new MoveEntitiesHandler() },
                { "rotate_entities",         new RotateEntitiesHandler() },
                { "scale_entities",          new ScaleEntitiesHandler() },
                { "copy_entities",           new CopyEntitiesHandler() },
                { "erase_entities",          new EraseEntitiesHandler() },
                { "offset_entities",         new OffsetEntitiesHandler() },
                { "update_texts",            new UpdateTextsHandler() },
                { "send_code",               new SendCodeHandler() },
                { "run_lisp",                new RunLispHandler() },
                { "apply_unicode_style",     new ApplyUnicodeStyleHandler() },
                { "collapse_and_rewrite",    new CollapseAndRewriteHandler() },
                { "translate_and_rewrite",   new TranslateAndRewriteHandler() },
                { "list_baked_tools",        new ListBakedToolsHandler() },
                { "zoom_extents",            new ZoomExtentsHandler() },
                { "zoom_window",             new ZoomWindowHandler() },
                { "zoom_to_entity",          new ZoomToEntityHandler() },
                { "capture_view_image",      new CaptureViewImageHandler() },
                { "inspect_view_region",     new InspectViewRegionHandler() },
                { "restore_view",            new RestoreViewHandler() },
                { "export_dxf",              new ExportDxfHandler() },
                { "get_variables",           new GetVariablesHandler() },
                { "set_system_variable",     new SetSystemVariableHandler() },
                { "list_recent_drawings",    new ListRecentDrawingsHandler() },
                { "open_drawing",            new OpenDrawingHandler() },
                { "list_open_documents",     new ListOpenDocumentsHandler() },
                { "activate_document",       new ActivateDocumentHandler() },
                { "save_drawing",            new SaveDrawingHandler() },
                { "purge_drawing",           new PurgeDrawingHandler() },
            };
            _commands.Add("apply_bake", new ApplyBakeSuggestionHandler((cmd, parameters) => ValidateCommand(cmd, parameters, out _)));
            _commands.Add("batch_execute", new BatchExecuteHandler(ExecuteCommand));
            _commands.Add("run_baked_tool", new RunBakedToolHandler(ExecuteCommand));
        }

        public string Dispatch(string requestLine)
        {
            if (_isAccepting != null && !_isAccepting())
                return ErrorJson(null, "DWG listener stopped; command was not started.");
            string id = null;
            string cmd = null;
            JToken parameters = null;
            JObject actualTarget = null;
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var request = JObject.Parse(requestLine);
                id = (string)request["id"];

                var auth = (string)request["auth"];
                if (!string.Equals(auth, _authToken, StringComparison.Ordinal))
                    return ErrorJson(id, "unauthorized");

                cmd = (string)request["cmd"];
                parameters = request["params"];
                var expectedTarget = request["target"] as JObject;
                DocumentTargetGuard.CheckInstance(expectedTarget, cmd);

                if (string.Equals(cmd, "set_tool_catalog", StringComparison.Ordinal))
                {
                    var catalogError = ToolCatalogStore.AcceptJson(parameters);
                    if (catalogError != null)
                        return ErrorJson(id, catalogError);
                    return SerializeResponse(id, cmd, CommandResult.Success(new { accepted = ToolCatalogStore.Snapshot().Length }));
                }

                // Authenticate first; health is independent of drawing locks and activity toasts.
                if (string.Equals(cmd, "health", StringComparison.Ordinal))
                {
                    if (parameters != null && parameters.Type != JTokenType.Null
                        && (parameters is not JObject healthParams || healthParams.Count != 0))
                        return ErrorJson(id, "health does not accept plugin-side parameters");
                    var health = HealthHandler.Read(_healthIdentity);
                    return SerializeResponse(id, cmd, health, (health.Result as JObject)?["target"] as JObject);
                }

                var preflight = ValidateCommand(cmd, parameters, out var preflightHandler);
                if (!preflight.Ok)
                {
                    NotifyCompleted(cmd, null, false, preflight.Error, preflightHandler?.Description, elapsed.ElapsedMilliseconds, parameters);
                    return SerializeResponse(id, cmd, preflight);
                }

                var capture = request.Value<int?>("change_capture_version") == ChangeCaptureContract.Version
                    && ChangeCaptureContract.Tracks(cmd) ? new DocumentChangeCapture() : null;
                var result = preflightHandler is IAcadSessionCommand sessionCommand
                    ? DocumentInvoker.InvokeSession(() => sessionCommand.ExecuteSession(parameters), _isAccepting, target => actualTarget = target,
                        expectedTarget, cmd == "activate_document" ? cmd : null)
                    : DocumentInvoker.Invoke(doc => capture == null ? ExecuteCommand(doc, cmd, parameters)
                        : capture.Run(doc, cmd, () => ExecuteCommand(doc, cmd, parameters)),
                        _isAccepting, expectedTarget, cmd, target => actualTarget = target);

                string resultJson = null;
                if (result.Ok)
                {
                    try
                    {
                        var filtered = McpResponsePrivacy.FilterResult(cmd, result.Result);
                        result.Result = filtered; // Reuse the same spill receipt for History and wire delivery.
                        if (filtered != null)
                            resultJson = JsonConvert.SerializeObject(filtered);
                    }
                    catch { }
                }

                NotifyCompleted(cmd, resultJson, result.Ok, result.Error, preflightHandler?.Description, elapsed.ElapsedMilliseconds, parameters);
                return SerializeResponse(id, cmd, result, actualTarget, resultPrepared: true, capture: capture?.Result);
            }
            catch (Exception ex)
            {
                NotifyCompleted(cmd, null, false, ex.Message, null, elapsed.ElapsedMilliseconds, parameters);
                return ErrorJson(id, ex.Message, actualTarget);
            }
        }

        /// <summary>
        /// Fire-and-forget completion toast. Uses the privacy-filtered result
        /// (same policy as the wire response) — never raw result data.
        /// </summary>
        private static void NotifyCompleted(string cmd, string resultJson,
            bool success, string error, string description, long durationMs, JToken parameters)
        {
            var notifier = App.ToastNotifier;
            if (notifier == null)
                return;
            try
            {
                notifier.OnCompleted(
                    cmd,
                    resultJson,
                    success,
                    success ? null : McpResponsePrivacy.SanitizeError(error),
                    description,
                    durationMs,
                    parameters);
            }
            catch (Exception ex)
            {
                PluginLog.Debug("McpToastNotifier.OnCompleted failed: " + ex.Message);
            }
        }

        private CommandResult ExecuteCommand(Autodesk.AutoCAD.ApplicationServices.Document doc, string cmd, JToken parameters)
        {
            var preflight = ValidateCommand(cmd, parameters, out var handler);
            if (!preflight.Ok)
                return preflight;
            return DocumentChangeCapture.ExecuteHandler(cmd, () => handler.Execute(doc, parameters));
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

        private static string SerializeResponse(string id, string cmd, CommandResult result, JObject actualTarget = null,
            bool resultPrepared = false, JObject capture = null)
        {
            return SerializeEnvelope(new
            {
                id,
                ok = result.Ok,
                result = result.Ok ? (resultPrepared ? result.Result : McpResponsePrivacy.FilterResult(cmd, result.Result)) : null,
                error = result.Ok ? null : McpResponsePrivacy.SanitizeError(result.Error)
            }, actualTarget, capture);
        }

        public static string ErrorJson(string id, string error, JObject actualTarget = null) =>
            SerializeEnvelope(new { id, ok = false, error = McpResponsePrivacy.SanitizeError(error) }, actualTarget);

        private static string SerializeEnvelope(object response, JObject actualTarget, JObject capture = null)
        {
            var envelope = JObject.FromObject(response);
            if (actualTarget != null) envelope["_target"] = actualTarget;
            if (capture != null) envelope["_change_capture"] = capture;
            return envelope.ToString(Formatting.None);
        }
    }
}
