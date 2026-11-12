using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.Reports.Model;
using ModelContextProtocol.Server;
using Newtonsoft.Json;

namespace Bimwright.Dwg.Server.Tools
{
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
            "Saved as a local file only; does not change any drawing and works without an open drawing when there are no links.")]
        public static Task<string> ReportTaskResult(string task_id, string outcome, string summary, string body = null)
        {
            var error = ReportLimits.Validate(task_id, outcome, summary, body);
            if (error != null)
                return Task.FromResult(JsonConvert.SerializeObject(new McpResponse { Ok = false, Error = error }));
            return ToolGateway.LoggedCall("report_task_result",
                new { task_id, outcome, summary_chars = summary.Length, body_bytes = body == null ? 0 : System.Text.Encoding.UTF8.GetByteCount(body) },
                new { task_id, outcome, summary, body, client_session = GatewaySession });
        }
    }
}
