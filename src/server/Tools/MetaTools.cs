using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using Newtonsoft.Json;

namespace Bimwright.Dwg.Server.Tools
{
    [McpServerToolType]
    public class MetaTools
    {
        [McpServerTool(Name = "dwg_list_available_targets", ReadOnly = true, Idempotent = true), Description(
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

        [McpServerTool(Name = "dwg_get_current_target", ReadOnly = true, Idempotent = true), Description(
            "Return the currently pinned AutoCAD target. If unset, dwg-mcp auto-selects the newest discovered target.")]
        public static Task<string> GetCurrentTarget()
        {
            return Ok(new { target = ServerState.Config?.Target });
        }

        [McpServerTool(Name = "dwg_switch_target"), Description(
            "Pin subsequent dwg-mcp calls to an AutoCAD target year. Use 2022, 2023, 2024, 2025, 2026, or 2027; legacy R-codes are rejected.")]
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
