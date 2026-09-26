using System;
using Newtonsoft.Json.Linq;
namespace Bimwright.Dwg.Plugin.Views.Toast
{
    public static class ToastCompletion
    {
        public static string Publish(string response, string command, long duration, string drawing, Action<McpToastViewModel> notify)
        {
            if (notify == null) return response;
            try
            {
                var wire = JObject.Parse(response);
                notify(ToastContentBuilder.BuildCompleted(command, wire["result"] as JObject,
                    wire["ok"]?.Type == JTokenType.Boolean && (bool)wire["ok"],
                    wire["error"]?.Type == JTokenType.String ? (string)wire["error"] : null, duration, drawing));
            }
            catch { /* Notifications must never alter or fail the MCP response. */ }
            return response;
        }
    }
}
