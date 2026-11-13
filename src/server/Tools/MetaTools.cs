using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Bimwright.Dwg.Plugin;
using Newtonsoft.Json;

namespace Bimwright.Dwg.Server.Tools
{
    [McpServerToolType]
    public class MetaTools
    {
        [McpServerTool(Name = "dwg_list_available_targets", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description(
            "List running AutoCAD instances with target_id, PID and process start time. In full mode select the intended instance using dwg_switch_target(target='pid:<PID>'). " +
            "In --read-only, start or restart the gateway with --target pid:<PID> or id:<target_id>; dwg_switch_target is unavailable. " +
            "Supports per-instance discovery and legacy AutoCAD 2022 through 2027 discovery files.")]
        public static Task<string> ListAvailableTargets()
        {
            var targets = AuthToken.ListAvailable()
                .Select(info => new
                {
                    target = info.Target,
                    target_id = info.TargetId,
                    process_start_utc = info.ProcessStartUtc,
                    transport = info.Transport,
                    host = info.Host,
                    port = info.Port,
                    pipe_name = info.PipeName,
                    pid = info.Pid,
                    discovery_file = info.DiscoveryFile
                })
                .ToArray();

            return Ok(new { targets });
        }

        [McpServerTool(Name = "dwg_get_current_target", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description(
            "Return this gateway's binding state, PID, process start, generation, selection confirmation and drawing identity. No discovery or drawing changes.")]
        public static Task<string> GetCurrentTarget()
        {
            return Ok(ToolGateway.Client.CurrentTarget);
        }

        [McpServerTool(Name = "dwg_switch_target", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false), Description(
            "Bind this gateway to one AutoCAD instance. A different bound instance requires pid:<PID> or id:acad-<year>-<PID>; an initial year must be unambiguous. " +
            "verify=true (default) authenticates health and confirms the current drawing before allowing writes. verify=false permits reads only. " +
            "Without rebind=true, selecting the same process preserves its drawing lifetime pin, including with verify=false. expected_document_id explicitly sets that pin, even without verification. " +
            "Leaving a live confirmed instance requires rebind=true. A changed drawing must match an explicit expected_document_id or be intentionally accepted with rebind=true. " +
            "Failed verification preserves the previous binding. Inspect health after opening/switching drawings before accepting a new pin. " +
            "Unavailable in --read-only. Changes routing only; never activates, saves or closes drawings. Calls pending under an older binding are refused before sending.")]
        public static async Task<string> SwitchTarget(
            [Description("pid:<PID> or id:acad-<year>-<PID> for a different bound instance; an unambiguous AutoCAD year (2022-2027) is accepted for initial selection.")] string target,
            [Description("Verify host and active drawing before allowing writes; default true.")] bool verify = true,
            [Description("Optional drawing lifetime pin observed from health. Sets the pin even with verify=false; verification must match it.")] string expected_document_id = null,
            [Description("Explicitly accept an intended instance or drawing change; default false. Does not bypass an expected_document_id mismatch.")] bool rebind = false)
        {
            if (ServerState.IsReadOnly) return ServerState.ReadOnlyError("dwg_switch_target");
            try
            {
                var selected = await ToolGateway.Client.SwitchTargetAsync(target, verify, expected_document_id, rebind);
                return await Ok(selected);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException)
            { return await Refuse(ex.Message); }
        }

        [McpServerTool(Name = "dwg_list_recent_drawings", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description(
            "List recent DWG files for the connected AutoCAD product from its user registry Recent File List. Works with no drawing open; never opens a file. " +
            "Returns up to 50 entries in MRU index order: index, path, name, exists and pinned. Optional name is a case-insensitive filename substring, filtered before max_items (1-50, default 50). " +
            "Ask the user which path to open, then call dwg_open_drawing with that absolute path. The registry layout is product-specific; a missing list returns registry_found=false, not a guessed list.")]
        public static Task<string> ListRecentDrawings(
            [Description("Optional case-insensitive filename substring.")] string name = null,
            [Description("Maximum matching files, 1-50; default 50.")] int max_items = 50)
        {
            try { DrawingSessionContract.ValidateMaxItems(max_items); }
            catch (ArgumentException ex) { return Refuse(ex.Message); }
            return ToolGateway.LoggedCall("list_recent_drawings", new { name, max_items }, new { name, max_items });
        }

        [McpServerTool(Name = "dwg_open_drawing", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false), Description(
            "Open an existing .dwg from an absolute drive/UNC path on the AutoCAD machine, not the MCP client. This is the tool to use when no drawing is open. " +
            "Ask the user which recent path to open first. activate=true (default) makes it active; false requires an existing active drawing and restores that drawing after opening (not a viewless background database). " +
            "read_only=false (default) requests writable access; true requests read-only access. Reuses an already-open drawing without reopening it; an access-mode mismatch is refused rather than closing or changing it. " +
            "Never saves, closes or overwrites drawings. Runs in application context without a document lock, requires idle multi-document mode (SDI=0), and is not supported inside batch/baked drawing workflows. " +
            "DWG only: no DXF, templates, cloud URLs, audit or recovery. Native AutoCAD dialogs may require the user. timeout_seconds: 1-900, default 600; timing out does not cancel an Open already running, so inspect AutoCAD before retrying. " +
            "Returns opened, was_already_open, title, path, actual read_only, activated and active_document. Opening an older DWG does not write upgraded bytes unless subsequently saved.")]
        public static Task<string> OpenDrawing(
            [Description("Absolute .dwg path reachable by the AutoCAD process.")] string path,
            [Description("Make the drawing active; false keeps an existing drawing active.")] bool activate = true,
            [Description("Request read-only access to the file (not the gateway --read-only mode).")] bool read_only = false,
            [Description("Plugin response wait in seconds, 1-900; default 600. Does not cancel host work.")] int timeout_seconds = 600)
        {
            try { DrawingSessionContract.ValidateTimeout(timeout_seconds); }
            catch (ArgumentException ex) { return Refuse(ex.Message); }
            var request = new { path, activate, read_only, timeout_seconds };
            return ToolGateway.LoggedCall("open_drawing", request, request);
        }

        [McpServerTool(Name = "dwg_list_open_documents", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description(
            "List open drawings in the bound AutoCAD instance, including name, path, document_id, active and has_unsaved_changes. " +
            "Path is null for an unnamed drawing; has_unsaved_changes is null if AutoCAD cannot report it. " +
            "Works without an active drawing. Lists session metadata without switching drawings or reading entities in inactive drawings.")]
        public static Task<string> ListOpenDocuments()
            => ToolGateway.LoggedCall("list_open_documents", new { }, new { });

        [McpServerTool(Name = "dwg_activate_document", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false), Description(
            "Activate an already-open drawing in the bound AutoCAD instance using its current document_id from dwg_list_open_documents. " +
            "Requires a verified current target and runs as a standalone call in idle multi-document mode. Never opens, saves or closes a drawing. " +
            "After a change, inspect dwg_health and call dwg_switch_target with the same PID and the observed expected_document_id before using drawing tools. " +
            "The activation receipt does not confirm the new target. Unavailable in --read-only.")]
        public static Task<string> ActivateDocument(
            [Description("Lifetime ID of the intended open drawing from dwg_list_open_documents.")] string document_id)
        {
            if (string.IsNullOrWhiteSpace(document_id)) return Refuse("document_id is required.");
            var request = new { document_id };
            return ToolGateway.LoggedCall("activate_document", request, request);
        }

        private static Task<string> Refuse(string error)
            => Task.FromResult(JsonConvert.SerializeObject(new McpResponse { Ok = false, Error = error }));

        private static Task<string> Ok(object result)
        {
            return Task.FromResult(JsonConvert.SerializeObject(new
            {
                id = Guid.NewGuid().ToString("N"),
                ok = true,
                result,
                error = (string)null
            }));
        }
    }
}
