using System;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin
{
    // Actual transport source is exercised with host-free dispatch; no Autodesk API is loaded.
    internal sealed class CommandDispatcher
    {
        private readonly string _token;
        private readonly Func<bool> _isAccepting;
        internal static Func<string, string> Override;
        internal CommandDispatcher(string token, Func<bool> isAccepting = null) { _token = token; _isAccepting = isAccepting; }
        internal string Dispatch(string line)
        {
            if (_isAccepting != null && !_isAccepting()) return ErrorJson(null, "listener stopped");
            if (Override != null) return Override(line);
            var request = JObject.Parse(line);
            return new JObject
            {
                ["id"] = request["id"], ["ok"] = (string)request["auth"] == _token,
                ["result"] = "pong"
            }.ToString(Newtonsoft.Json.Formatting.None);
        }
        internal static string ErrorJson(string id, string error) => new JObject
        { ["id"] = id, ["ok"] = false, ["error"] = error }.ToString(Newtonsoft.Json.Formatting.None);
    }
}
