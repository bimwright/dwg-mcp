using Newtonsoft.Json;

namespace Bimwright.Dwg.Plugin.Reports.Model
{
    public sealed class ReportDocument
    {
        [JsonProperty("ref")]
        public string Ref { get; set; }
        /// <summary>The plugin's per-open document_id; a reopened file gets a new one.</summary>
        [JsonProperty("token")]
        public string Token { get; set; }
        [JsonProperty("title")]
        public string Title { get; set; }
        /// <summary>Full DWG path when the drawing has been saved; null for an unsaved drawing.</summary>
        [JsonProperty("path")]
        public string Path { get; set; }
    }
}
