using System;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin
{
    /// <summary>Host-free health response builder; failed UI observation never hides loaded identity.</summary>
    public static class HealthSnapshot
    {
        public static JObject Capture(JObject identity, Func<JObject> readState)
        {
            var result = new JObject
            {
                ["ok"] = true, ["healthy"] = true,
                ["autocad_year"] = identity["host_year"]?.DeepClone(),
                ["process_id"] = identity["process_id"]?.DeepClone(),
                ["setup_plugin"] = identity.DeepClone(),
                ["has_active_document"] = JValue.CreateNull(),
                ["state_probe"] = "unavailable"
            };
            try
            {
                var state = readState() ?? throw new InvalidOperationException("No host state returned.");
                foreach (var name in new[] { "has_active_document", "document_title", "is_quiescent" })
                    result[name] = state[name]?.DeepClone() ?? JValue.CreateNull();
                result["state_probe"] = "available";
            }
            catch (Exception ex)
            {
                var busy = ex is TimeoutException;
                result["ok"] = false;
                result["healthy"] = false;
                result["state_probe"] = busy ? "busy" : "unavailable";
                result["error"] = new JObject
                {
                    ["code"] = busy ? "UI_BUSY" : "HOST_STATE_UNAVAILABLE",
                    ["message"] = busy
                        ? "AutoCAD main thread is busy; document state was not observed. No drawing operation was started."
                        : "AutoCAD document state could not be observed. The loaded plugin identity is retained."
                };
            }
            return result;
        }
    }
}
