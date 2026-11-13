using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Bimwright.Dwg.Plugin.Reports.Model
{
    public sealed class ReportChanges
    {
        public const int Version = 1;
        public const int MaxCalls = 100;

        [JsonProperty("calls")]
        public List<ReportCallReference> Calls { get; set; } = new List<ReportCallReference>();

        public bool IsValid(string session) => Calls != null && Calls.Count <= MaxCalls
            && Guid.TryParseExact(session, "N", out _) && Calls.All(call => call != null && call.IsValid(session))
            && Calls.Select(call => call.CallId).Distinct(StringComparer.Ordinal).Count() == Calls.Count;
    }

    public sealed class ReportCallReference
    {
        [JsonProperty("call_id")] public string CallId { get; set; }
        [JsonProperty("client_session")] public string ClientSession { get; set; }
        [JsonProperty("drawing_key", NullValueHandling = NullValueHandling.Ignore)] public string DrawingKey { get; set; }
        [JsonProperty("tool")] public string Tool { get; set; }
        [JsonProperty("capture_status")] public string CaptureStatus { get; set; }

        internal bool IsValid(string session) => ClientSession == session && Guid.TryParseExact(CallId, "N", out var id)
            && CallId == id.ToString("N") && (DrawingKey == null ? CaptureStatus != "stored"
                : DrawingKey.Length == 64 && DrawingKey.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
            && Tool is { Length: >= 1 and <= 64 } && Tool.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_')
            && CaptureStatus is "stored" or "no_committed_changes" or "disabled" or "identity_unavailable" or "unsupported" or "outcome_unknown" or "storage_failed";
    }
}
