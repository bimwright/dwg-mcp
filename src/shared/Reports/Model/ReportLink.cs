using System.Collections.Generic;
using Newtonsoft.Json;

namespace Bimwright.Dwg.Plugin.Reports.Model
{
    public sealed class ReportLink
    {
        [JsonProperty("label")]
        public string Label { get; set; }
        [JsonProperty("doc")]
        public string Doc { get; set; }
        [JsonProperty("entities")]
        public List<ReportEntity> Entities { get; set; } = new List<ReportEntity>();
    }

    /// <summary>An entity resolved when the report was saved; handles are stable for the life of the DWG file.</summary>
    public sealed class ReportEntity
    {
        [JsonProperty("handle")]
        public string Handle { get; set; }
        /// <summary>AutoCAD object type, for example Line, MText or BlockReference.</summary>
        [JsonProperty("type")]
        public string Type { get; set; }
        [JsonProperty("layer")]
        public string Layer { get; set; }
        /// <summary>Owning space or block, for example *Model_Space or *Paper_Space.</summary>
        [JsonProperty("space")]
        public string Space { get; set; }
    }
}
