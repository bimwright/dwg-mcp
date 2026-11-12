using System.Collections.Generic;
using Newtonsoft.Json;

namespace Bimwright.Dwg.Plugin.Reports.Model
{
    public sealed class AgentReport
    {
        [JsonProperty("schema_version")]
        public int SchemaVersion { get; set; } = 1;
        [JsonProperty("report_id")]
        public string ReportId { get; set; }
        [JsonProperty("task_id")]
        public string TaskId { get; set; }
        [JsonProperty("outcome")]
        public string Outcome { get; set; }
        [JsonProperty("summary")]
        public string Summary { get; set; }
        [JsonProperty("body")]
        public string Body { get; set; }
        [JsonProperty("created_utc")]
        public string CreatedUtc { get; set; }
        [JsonProperty("updated_utc")]
        public string UpdatedUtc { get; set; }
        [JsonProperty("source")]
        public ReportSource Source { get; set; }
        [JsonProperty("documents")]
        public List<ReportDocument> Documents { get; set; } = new List<ReportDocument>();
        [JsonProperty("links")]
        public List<ReportLink> Links { get; set; } = new List<ReportLink>();
    }

    public sealed class ReportSource
    {
        /// <summary>One gateway process; the same task_id from another gateway never replaces this report.</summary>
        [JsonProperty("client_session", NullValueHandling = NullValueHandling.Ignore)]
        public string ClientSession { get; set; }
        public static string TaskKey(string taskId, string session) => session == null ? "process:" + taskId : "client:" + session + ":" + taskId;
        [JsonProperty("target_id")]
        public string TargetId { get; set; }
        [JsonProperty("pid")]
        public int Pid { get; set; }
        [JsonProperty("acad_year")]
        public int AcadYear { get; set; }
        [JsonProperty("process_start_utc")]
        public string ProcessStartUtc { get; set; }
    }
}
