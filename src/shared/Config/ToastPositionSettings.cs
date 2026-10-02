using System.IO;
using Newtonsoft.Json.Linq;
namespace Bimwright.Dwg.Plugin
{
    public static partial class PluginSettings
    {
        public static ToastPositionOptions LoadToastPosition()
        {
            try { return ReadToastPosition(JObject.Parse(File.ReadAllText(FilePath))); }
            catch { return new ToastPositionOptions(); }
        }
        internal static ToastPositionOptions ReadToastPosition(JObject root)
        {
            var offset = root["toastDragOffset"] as JObject;
            double? Number(JToken value) => value?.Type == JTokenType.Integer || value?.Type == JTokenType.Float ? (double?)value : null;
            return new ToastPositionOptions(
                root["toastHorizontalAlign"]?.Type == JTokenType.String && (string)root["toastHorizontalAlign"] == "right",
                root["toastVerticalAlign"]?.Type == JTokenType.String && (string)root["toastVerticalAlign"] == "bottom",
                root["toastDragEnabled"]?.Type == JTokenType.Boolean && (bool)root["toastDragEnabled"],
                Number(offset?["x"]), Number(offset?["y"]));
        }
        public static bool SaveToastPosition(ToastPositionOptions value) => TryUpdate(root =>
        {
            root["toastHorizontalAlign"] = value.Right ? "right" : "left";
            root["toastVerticalAlign"] = value.Bottom ? "bottom" : "top";
            root["toastDragEnabled"] = value.DragEnabled;
            if (value.HasOffset) root["toastDragOffset"] = new JObject { ["x"] = value.OffsetX, ["y"] = value.OffsetY };
            else root.Remove("toastDragOffset");
        }, "SaveToastPosition");
    }
}
