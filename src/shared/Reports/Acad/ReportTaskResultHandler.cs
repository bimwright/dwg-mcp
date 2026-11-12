using System;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Bimwright.Dwg.Plugin.Reports.Binding;
using Bimwright.Dwg.Plugin.Reports.Model;
using Bimwright.Dwg.Plugin.Reports.Storage;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Reports.Acad
{
    internal static class ReportServices
    {
        internal static readonly ReportStore Store = new ReportStore(log: message => PluginLog.Debug(message));
    }

    /// <summary>Saves an agent task report as a local file; never changes a drawing.</summary>
    public sealed class ReportTaskResultHandler : IAcadSessionCommand
    {
        public string Name => "report_task_result";
        public string Description => "Save an agent task report without changing the drawing.";
        public CommandSchema Schema => CommandSchemas.ReportTaskResult;

        public CommandResult Execute(Document doc, JToken parameters)
            => CommandResult.Fail("report_task_result is a standalone call; it is not supported inside drawing batches or baked tools.");

        public CommandResult ExecuteSession(JToken parameters)
        {
            var validation = SchemaValidator.Validate(Name, parameters, Schema);
            if (!validation.Ok) return CommandResult.Fail(validation.Error);
            var request = (JObject)parameters;
            var taskId = request.Value<string>("task_id");
            var outcome = request.Value<string>("outcome");
            var summary = request.Value<string>("summary");
            var body = request.Value<string>("body");
            var session = request.Value<string>("client_session");
            var error = ReportLimits.Validate(taskId, outcome, summary, body);
            if (error != null) return CommandResult.Fail(error);
            if (session != null && !Guid.TryParseExact(session, "N", out _))
                return CommandResult.Fail("client_session must be a 32-character hexadecimal gateway session ID.");

            var target = DocumentTargetGuard.Capture(null);
            var source = new ReportSource
            {
                ClientSession = session,
                TargetId = target.Value<string>("target_id"),
                Pid = target.Value<int>("pid"),
                AcadYear = target.Value<int>("acad_year"),
                ProcessStartUtc = ReportJson.FormatUtc(target.Value<DateTime>("process_start_utc"))
            };
            try
            {
                var result = ReportBuilder.BuildAndSave(taskId, outcome, summary, body, new AcadReportResolver(), source, ReportServices.Store);
                return CommandResult.Success(ReportBuilder.CreateResponse(result, result.Saved));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return CommandResult.Fail("REPORT_SAVE_FAILED: " + ErrorSanitizer.Sanitize(ex.Message) + "; resend with the same task_id.");
            }
        }
    }
}
