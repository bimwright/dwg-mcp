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
            "List running AutoCAD targets discovered through acad-YYYY.json and legacy AutoCAD 2024 discovery files. " +
            "Versions are 4-digit AutoCAD years 2022 through 2027.")]
        public static Task<string> ListAvailableTargets()
        {
            var targets = AuthToken.ListAvailable()
                .Select(info => new
                {
                    target = info.Target,
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
            "Return the currently pinned AutoCAD target. If unset, dwg-mcp auto-selects the newest discovered target.")]
        public static Task<string> GetCurrentTarget()
        {
            return Ok(new { target = ServerState.Config?.Target });
        }

        [McpServerTool(Name = "dwg_switch_target", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false), Description(
            "Pin subsequent dwg-mcp calls to an AutoCAD target year. Use 2022, 2023, 2024, 2025, 2026, or 2027; legacy R-codes are rejected." +
            " Changes only the in-memory routing choice; no drawing or file changes. Call again with the previous target to restore routing.")]
        public static Task<string> SwitchTarget(
            [Description("4-digit AutoCAD target year: 2022, 2023, 2024, 2025, 2026, or 2027.")] string target)
        {
            var normalized = AuthToken.NormalizeTarget(target);
            if (ServerState.Config == null)
            {
                ServerState.Config = new DwgMcpConfig();
            }

            ServerState.Config.Target = normalized;
            return Ok(new { target = normalized });
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
