using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Bimwright.Dwg.Server
{
    internal static class ToolGateway
    {
        private static readonly PluginClient Client = PluginClient.FromDiscoveryFile();

        internal static async Task<string> LoggedCall(string toolName, object inputParams, object pluginParams)
        {
            var requestId = Guid.NewGuid().ToString("N");
            var sw = Stopwatch.StartNew();
            string paramsJson = SafeSerialize(inputParams);
            ServerLogger.LogStart(requestId, toolName, paramsJson);
            try
            {
                var resp = await Client.SendAsync(toolName, pluginParams, requestId);
                sw.Stop();
                ServerLogger.LogFinish(requestId, toolName, resp.Ok, sw.ElapsedMilliseconds, resp.Error);
                return JsonConvert.SerializeObject(resp);
            }
            catch (Exception ex)
            {
                sw.Stop();
                ServerLogger.LogFinish(requestId, toolName, false, sw.ElapsedMilliseconds, ex.Message);
                throw;
            }
        }

        internal static Task<McpResponse> SendRaw(string toolName, object pluginParams, string requestId = null)
        {
            return Client.SendAsync(toolName, pluginParams, requestId);
        }

        private static string SafeSerialize(object o)
        {
            try { return JsonConvert.SerializeObject(o); }
            catch { return "<unserializable>"; }
        }
    }
}
