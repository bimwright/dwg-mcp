using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Bimwright.Dwg.Plugin;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server.Tools
{
    [McpServerToolType]
    public sealed class HealthTools
    {
        private readonly PluginClient client;
        private readonly ReleaseUpdateCheck updates;
        private readonly JObject gateway;

        public HealthTools() : this(PluginClient.FromDiscoveryFile(), new ReleaseUpdateCheck(), GatewayRuntime.Identity) { }

        internal HealthTools(PluginClient client, ReleaseUpdateCheck updates, JObject gateway)
        {
            this.client = client;
            this.updates = updates;
            this.gateway = (JObject)gateway.DeepClone();
        }

        [McpServerTool(Name = "dwg_health", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = true), Description(
            "At session start select the intended AutoCAD target, then call dwg_health(check_updates=true) once. " +
            "Routine probes stay offline; not_checked/unknown never means up to date. Reports host/document state and " +
            "loaded server/plugin versions, build ids and alignment. A busy UI can make document state unavailable; " +
            "health does not require or lock an active drawing. No target still returns gateway identity with ok:false " +
            "and host_connected:false. Legacy plugins without health/identity remain unverified, not assumed corrupt. " +
            "Inspect both update.components and the full update.notification message, lookup_reason, protected_component_reasons " +
            "and unobserved_components. UPDATE_CHECK_UNAVAILABLE reports external lookup failure, not a runtime fault " +
            "or request for installation authorization. LOCAL_VERSION_AHEAD is informational: never downgrade or " +
            "automatically replace development builds. Only update_guidance.mode=prepare_update with " +
            "bundle_update_proposal_allowed=true permits an update proposal and needs installation authorization " +
            "(reuse existing permission); other modes contain check/review steps only. " +
            "comparison_reason=release_revision_unavailable means missing external evidence, not a broken local build. " +
            "After reconnect compare fresh initialize and tools/list metadata/schema/annotations on that connection " +
            "with client_verification; the server cannot prove client cache refresh. Matching revisions do not " +
            "verify artifact bytes, compatibility or publisher trust. Read-only; never installs, downloads packages, " +
            "saves/closes drawings, edits client configs or restarts processes.")]
        public async Task<CallToolResult> Health(bool check_updates = false, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            JObject health;
            var connected = false;
            var requestId = Guid.NewGuid().ToString("N");
            try
            {
                // Bound the total host wait below the catalog's 30-second deadline,
                // leaving the release lookup its own shared five-second budget.
                // Cancellation/timeout stops this read-only caller, never a drawing operation.
                var response = await client.SendAsync("health", new JObject(), requestId)
                    .WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
                if (response == null || response.Id != requestId)
                    health = Failure("INVALID_HEALTH_RESPONSE", "The plugin did not return the matching health response.");
                else if (!response.Ok)
                {
                    var legacy = string.Equals(response.Error, "unknown command: health", StringComparison.Ordinal);
                    connected = legacy; // Authenticated old dispatcher replied, but cannot prove its loaded identity/state.
                    health = Failure(legacy ? "HEALTH_UNSUPPORTED" : "TARGET_UNAVAILABLE",
                        legacy ? "The replying plugin does not support health. Its loaded version and document state are unverified."
                            : ErrorSanitizer.Sanitize(response.Error ?? "Health probe failed."));
                    health["health_supported"] = legacy ? false : (JToken)JValue.CreateNull();
                }
                else if (response.Result is JObject result
                    && (result["ok"] ?? result["healthy"]) is JToken status && status.Type == JTokenType.Boolean)
                {
                    connected = true;
                    health = (JObject)result.DeepClone();
                    health["ok"] = status.Value<bool>();
                    health["health_supported"] = true;
                }
                else
                    health = Failure("INVALID_HEALTH_RESPONSE", "The plugin returned invalid health data.");
            }
            catch (TimeoutException)
            {
                health = Failure("HEALTH_TIMEOUT", "The host health probe did not complete within its deadline. Verify connection/UI availability; do not infer the host is closed.");
            }
            catch (InvalidOperationException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                health = Failure("NO_TARGET", "No usable AutoCAD target was discovered. Verify the intended AutoCAD connection and loaded plugin.");
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is System.Net.Sockets.SocketException || ex is JsonException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                health = Failure("TARGET_UNAVAILABLE", "Health transport failed: " + ErrorSanitizer.Sanitize(ex.Message));
            }

            HealthRuntimeInfo.Enrich(health, gateway, connected);
            var plugin = connected ? health["setup_plugin"] as JObject : null;
            health["update"] = check_updates
                ? await updates.CheckAsync(gateway, plugin, cancellationToken)
                : ReleaseUpdateCheck.NotChecked(gateway, plugin);
            health["update_guidance"] = HealthRuntimeInfo.UpdateGuidance((JObject)health["update"]);
            var reply = new CallToolResult { IsError = health.Value<bool?>("ok") != true };
            reply.Content.Add(new TextContentBlock { Text = health.ToString(Formatting.None) });
            return reply;
        }

        private static JObject Failure(string code, string message) => new JObject
        {
            ["ok"] = false,
            ["error"] = new JObject { ["code"] = code, ["message"] = message }
        };
    }
}
