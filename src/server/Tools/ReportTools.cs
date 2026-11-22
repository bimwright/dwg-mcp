using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.Reports.Model;
using ModelContextProtocol.Server;
using Newtonsoft.Json;

namespace Bimwright.Dwg.Server.Tools
{
    internal sealed class ReportTaskRequest
    {
        [JsonProperty("task_id")] public string TaskId { get; init; }
        [JsonProperty("outcome")] public string Outcome { get; init; }
        [JsonProperty("summary")] public string Summary { get; init; }
        [JsonProperty("body")] public string Body { get; init; }
        [JsonProperty("client_session")] public string ClientSession { get; init; }
        [JsonIgnore] internal string[] CallIds { get; init; }
    }

    [McpServerToolType]
    public class ReportTools
    {
        /// <summary>One gateway process: a resent task_id replaces only reports this gateway saved in the same AutoCAD.</summary>
        internal static readonly string GatewaySession = Guid.NewGuid().ToString("N");

        [McpServerTool(Name = "dwg_report_task_result", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description(
            "Report the finished task to the AutoCAD user in the Reports window. Call it once when a job that worked in AutoCAD is verified, has failed or was cancelled: " +
            "it changed entities, or it found entities the user should check. Not for a quick answer in chat or a job that stopped before changing anything; never after each tool. " +
            "task_id is unique per job (1-80; resending from the same gateway to the same AutoCAD replaces that report); outcome completed|failed|cancelled; summary one line (1-120); " +
            "body optional Markdown subset (headings, lists, **bold**, *italic*, `code`, fenced code blocks, pipe tables, --- rules; underscores are literal), max 32 KiB UTF-8. " +
            "Link entities as [label](dwg:<handle>[,<handle>...][?doc=<document_id>]) with hexadecimal handles from other dwg_ tools. " +
            "Without doc a link targets the active drawing; use doc=<_target.document_id> for another open drawing. " +
            "The response lists unresolved links (bad_syntax, limit_exceeded, no_document, document_not_open, entity_not_found): fix them and resend with the same task_id. " +
            "Optional call_ids lists up to 100 distinct _history.call_id values from this gateway and AutoCAD process. Omit on resend to keep references; send [] to clear them. " +
            "Saved as a local file only; does not change any drawing and works without an open drawing when there are no links.")]
        public static Task<string> ReportTaskResult(string task_id, string outcome, string summary, string body = null, string[] call_ids = null)
        {
            var error = ReportLimits.Validate(task_id, outcome, summary, body);
            if (error != null)
                return Task.FromResult(JsonConvert.SerializeObject(new McpResponse { Ok = false, Error = error }));
            if (call_ids != null)
            {
                if (call_ids.Length > ReportChanges.MaxCalls || call_ids.Any(id => !Guid.TryParseExact(id, "N", out _)))
                    return InvalidCallIds();
                call_ids = call_ids.Select(id => Guid.ParseExact(id, "N").ToString("N")).ToArray();
                if (call_ids.Distinct(StringComparer.Ordinal).Count() != call_ids.Length) return InvalidCallIds();
            }
            return ToolGateway.LoggedCall("report_task_result",
                new { task_id, outcome, summary_chars = summary.Length, body_bytes = body == null ? 0 : System.Text.Encoding.UTF8.GetByteCount(body) },
                new ReportTaskRequest { TaskId = task_id, Outcome = outcome, Summary = summary, Body = body, ClientSession = GatewaySession, CallIds = call_ids });
        }

        private static Task<string> InvalidCallIds() => Task.FromResult(JsonConvert.SerializeObject(new McpResponse
        {
            Ok = false, Error = "call_ids must contain at most 100 distinct server-issued call IDs."
        }));

        [McpServerTool(Name = "dwg_get_report_history", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description(
            "Read the exact local calls attached to a report, including capture status, change ID and reason. Works offline using report_id. " +
            "Pass call_id to page through that report call's before/after deltas. limit is 1–25, default 10. Follow next_cursor with the same report and call_id. " +
            "If the report changes, restart at the first page. The report outcome is agent-reported.")]
        public static string GetReportHistory(string report_id, string cursor = null, string call_id = null, int limit = 10, int? max_response_bytes = null)
        {
            try
            {
                ChangeHistoryResponse.ValidateCap(max_response_bytes);
                var result = ReportHistoryLookup.Default.Read(report_id, cursor, call_id, limit);
                return ChangeHistoryResponse.Encode(new McpResponse { Ok = true, Result = result }, max_response_bytes);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            { return JsonConvert.SerializeObject(new McpResponse { Ok = false, Error = ex.Message }); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            { return JsonConvert.SerializeObject(new McpResponse { Ok = false, Error = "REPORT_HISTORY_UNAVAILABLE: the local report could not be read." }); }
        }
    }
}
