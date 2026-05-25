using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bimwright.Dwg.Server.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Bimwright.Dwg.Server
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var config = DwgMcpConfig.Load(args);
            ValidateTarget(config.Target);
            ServerState.Config = config;
            WarnIfUnwiredOptions(config);
            var enabled = ToolsetFilter.Resolve(config);

            var builder = Host.CreateApplicationBuilder(args);
            // MCP stdio transport owns stdout. Route logs to stderr.
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
            var mcp = builder.Services
                .AddMcpServer(ConfigureMcpServerOptions)
                .WithStdioServerTransport();
            mcp = RegisterToolsets(mcp, enabled);

            await builder.Build().RunAsync();
        }

        public static bool IsSendCodeEnabled(string[] args)
            => DwgMcpConfig.Load(args).EnableSendCodeOrDefault;

        private static void ConfigureMcpServerOptions(ModelContextProtocol.Server.McpServerOptions opts)
        {
            opts.ServerInfo = new ModelContextProtocol.Protocol.Implementation
            {
                Name = "dwg-mcp",
                Title = "DWG MCP",
                Version = "1.0.0-dev",
                Description = "Model Context Protocol gateway for Autodesk AutoCAD DWG workflows",
                WebsiteUrl = "https://github.com/bimwright/dwg-mcp"
            };
            opts.ServerInstructions = ServerInstructionsText;
        }

        private const string ServerInstructionsText =
@"dwg-mcp - MCP gateway for Autodesk AutoCAD DWG drawings. Use for current drawing metadata, layers, entity properties, simple line/circle creation, selected AutoCAD text, DBText, MText, SHX/Unicode style repair, Vietnamese translation writeback, clustered note cleanup, and DWG text rewriting.

AutoCAD routing: versions are 4-digit years 2022..2027. If multiple AutoCAD instances run, use dwg_list_available_targets then dwg_switch_target, or start the server with --target 2022|2023|2024|2025|2026|2027.

Tools use prefix dwg_:
- query: dwg_get_drawing_info, dwg_list_layers, and dwg_get_entity_properties read the current active document; dwg_get_selected_texts reads current AutoCAD pickfirst text selection and returns clustered text groups.
- modify: dwg_create_layer, dwg_create_line, dwg_create_circle, dwg_change_layer, dwg_update_texts, dwg_translate_and_rewrite, dwg_apply_unicode_style, dwg_collapse_and_rewrite write drawing/text/style changes.
- meta: dwg_batch_execute, dwg_list_available_targets, dwg_get_current_target, dwg_switch_target.
- toolbaker: dwg_list_baked_tools, dwg_run_baked_tool, dwg_list_bake_suggestions, dwg_accept_bake_suggestion, dwg_dismiss_bake_suggestion, dwg_create_bake_issue_draft.
- code: dwg_send_code is disabled unless --enable-send-code or BIMWRIGHT_DWG_ENABLE_SEND_CODE=1.

General CAD tools operate on the current AutoCAD active document. Entity arguments use AutoCAD hex handles returned by selection, creation, or property tools.
Call dwg_get_selected_texts before text writeback. In read-only mode only query/routing/list tools are exposed.";

        private static IMcpServerBuilder RegisterToolsets(IMcpServerBuilder mcp, HashSet<string> enabled)
        {
            if (enabled.Contains("query")) mcp = mcp.WithTools<QueryTools>();
            if (enabled.Contains("modify")) mcp = mcp.WithTools<ModifyTools>();
            if (enabled.Contains("meta"))
            {
                mcp = mcp.WithTools<MetaTools>();
                if (!ServerState.IsReadOnly) mcp = mcp.WithTools<BatchTools>();
            }
            if (enabled.Contains("toolbaker"))
            {
                mcp = mcp.WithTools<ToolBakerTools>();
                if (!ServerState.IsReadOnly) mcp = mcp.WithTools<ToolBakerWriteTools>();
            }
            if (enabled.Contains("code")) mcp = mcp.WithTools<CodeTools>();
            return mcp;
        }

        private static void ValidateTarget(string target)
        {
            if (string.IsNullOrWhiteSpace(target))
            {
                return;
            }

            AuthToken.NormalizeTarget(target);
        }

        public static string UnwiredOptionWarning(DwgMcpConfig config)
        {
            if (config != null && config.AllowLanBindOrDefault)
            {
                return "warning: --allow-lan-bind / BIMWRIGHT_DWG_ALLOW_LAN_BIND is parsed but plugin-side LAN binding is not yet implemented in v1.0; the AutoCAD plugin still listens on loopback only.";
            }

            return null;
        }

        private static void WarnIfUnwiredOptions(DwgMcpConfig config)
        {
            var warning = UnwiredOptionWarning(config);
            if (!string.IsNullOrEmpty(warning))
            {
                Console.Error.WriteLine(warning);
            }
        }
    }
}
