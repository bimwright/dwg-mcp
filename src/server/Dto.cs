using Newtonsoft.Json;

namespace Bimwright.Dwg.Server
{
    public class McpRequest
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("cmd")] public string Cmd { get; set; }
        [JsonProperty("params")] public object Params { get; set; }
        [JsonProperty("auth")] public string Auth { get; set; }
    }

    public class McpResponse
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("ok")] public bool Ok { get; set; }
        [JsonProperty("result")] public object Result { get; set; }
        [JsonProperty("error")] public string Error { get; set; }
    }

    public class UpdateTextItem
    {
        [JsonProperty("handle")] public string Handle { get; set; }
        [JsonProperty("new_text")] public string NewText { get; set; }
    }

    public class UpdateTextsRequest
    {
        [JsonProperty("items")] public UpdateTextItem[] Items { get; set; }
        [JsonProperty("apply_unicode_style")] public bool ApplyUnicodeStyle { get; set; }
    }

    public class TranslationItem
    {
        [JsonProperty("id")] public int Id { get; set; }
        [JsonProperty("new_text")] public string NewText { get; set; }
        [JsonProperty("render_mode")] public string RenderMode { get; set; }
        [JsonProperty("width_policy")] public string WidthPolicy { get; set; }
    }

    public class TranslateRequest
    {
        [JsonProperty("translations")] public TranslationItem[] Translations { get; set; }
        [JsonProperty("final_scale", NullValueHandling = NullValueHandling.Ignore)]
        public double? FinalScale { get; set; }
    }
}
