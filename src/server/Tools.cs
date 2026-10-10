using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Bimwright.Dwg.Server
{
    internal static class ToolGateway
    {
        private static readonly PluginClient Client = PluginClient.FromDiscoveryFile();

        private static readonly object CatalogGate = new object();
        private static string _catalogKey;
        private static string _catalogAttemptedKey;
        private static int _catalogAttempts;
        private const int MaxCatalogAttempts = 3;

        // Tests assign this to record the wire call without opening a socket.
        internal static Func<string, object, Task<McpResponse>> SendOverride;

        internal static async Task<string> LoggedCall(string toolName, object inputParams, object pluginParams)
        {
            if (ReadOnlyPolicy.IsBlocked(toolName, ServerState.Config))
                return JsonConvert.SerializeObject(new McpResponse { Ok = false, Error = ServerState.ReadOnlyMessage(toolName) });

            if (SendCodePolicy.IsBlocked(toolName, pluginParams, ServerState.Config))
                return JsonConvert.SerializeObject(new McpResponse { Ok = false, Error = SendCodePolicy.Refusal });

            var sendOverride = SendOverride;
            if (sendOverride != null)
                return JsonConvert.SerializeObject(await sendOverride(toolName, pluginParams));

            var requestId = Guid.NewGuid().ToString("N");
            var sw = Stopwatch.StartNew();
            ServerLogger.Default.LogStart(requestId, toolName, inputParams);
            try
            {
                await EnsureCatalogAsync();
                var resp = await Client.SendAsync(toolName, pluginParams, requestId);
                sw.Stop();
                ServerLogger.Default.LogFinish(requestId, toolName, resp.Ok, sw.ElapsedMilliseconds, resp.Error);
                return JsonConvert.SerializeObject(resp);
            }
            catch (Exception ex)
            {
                sw.Stop();
                ServerLogger.Default.LogFinish(requestId, toolName, false, sw.ElapsedMilliseconds, ex.Message);
                throw;
            }
        }

        internal static async Task<McpResponse> SendRaw(string toolName, object pluginParams, string requestId = null)
        {
            if (ReadOnlyPolicy.IsBlocked(toolName, ServerState.Config))
                return new McpResponse { Ok = false, Error = ServerState.ReadOnlyMessage(toolName) };

            if (SendCodePolicy.IsBlocked(toolName, pluginParams, ServerState.Config))
                return new McpResponse { Ok = false, Error = SendCodePolicy.Refusal };

            var sendOverride = SendOverride;
            if (sendOverride != null)
                return await sendOverride(toolName, pluginParams);

            await EnsureCatalogAsync();
            return await Client.SendAsync(toolName, pluginParams, requestId);
        }

        /// <summary>
        /// One catalog push per discovery token. A failed push is retried on the
        /// next call, up to MaxCatalogAttempts — older plugins that do not know
        /// set_tool_catalog would otherwise cost an extra round-trip per call.
        /// The real tool call still runs when the push fails.
        /// </summary>
        private static async Task EnsureCatalogAsync()
        {
            DiscoveryInfo discovery;
            try
            {
                discovery = AuthToken.Resolve(ServerState.Config?.Target);
            }
            catch
            {
                return;
            }

            var key = (discovery.Token ?? string.Empty)
                + "|" + (discovery.Transport ?? string.Empty)
                + "|" + (discovery.Port?.ToString() ?? string.Empty)
                + "|" + (discovery.PipeName ?? string.Empty);
            lock (CatalogGate)
            {
                if (string.Equals(_catalogKey, key, StringComparison.Ordinal))
                    return;
                if (string.Equals(_catalogAttemptedKey, key, StringComparison.Ordinal)
                    && _catalogAttempts >= MaxCatalogAttempts)
                    return;
            }

            var pushed = false;
            try
            {
                var payload = ToolCatalogBuilder.Build(ServerState.EnabledToolTypes,
                    ServerState.Config?.EnableSendCodeOrDefault ?? true, ServerState.IsReadOnly);
                var response = await Client.SendAsync("set_tool_catalog", payload);
                pushed = response != null && response.Ok;
                if (!pushed)
                    ServerLogger.Default.LogFinish("catalog", "set_tool_catalog", false, 0, response?.Error);
            }
            catch (Exception ex)
            {
                ServerLogger.Default.LogFinish("catalog", "set_tool_catalog", false, 0, ex.Message);
            }

            lock (CatalogGate)
            {
                if (pushed)
                {
                    _catalogKey = key;
                    _catalogAttemptedKey = key;
                    _catalogAttempts = 0;
                }
                else if (string.Equals(_catalogAttemptedKey, key, StringComparison.Ordinal))
                {
                    _catalogAttempts++;
                }
                else
                {
                    _catalogAttemptedKey = key;
                    _catalogAttempts = 1;
                }
            }
        }

    }
}
