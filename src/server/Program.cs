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
            ServerState.Config = config;
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
@"dwg-mcp - MCP gateway for Autodesk AutoCAD DWG drawings. Use for selected AutoCAD text, DBText, MText, SHX/Unicode style repair, Vietnamese translation writeback, clustered note cleanup, and DWG text rewriting.

Current routing uses the loaded AutoCAD plugin discovery file. Multi-version --target routing is prepared in config and lands with discovery v2.

Tools use prefix dwg_:
- query: dwg_get_selected_texts reads current AutoCAD pickfirst text selection and returns clustered text groups.
- modify: dwg_update_texts, dwg_translate_and_rewrite, dwg_apply_unicode_style, dwg_collapse_and_rewrite write text/style changes.
- code: dwg_send_code is disabled unless --enable-send-code or BIMWRIGHT_DWG_ENABLE_SEND_CODE=1.

Call dwg_get_selected_texts before writeback. In read-only mode only query/routing/list tools are exposed.";

        private static IMcpServerBuilder RegisterToolsets(IMcpServerBuilder mcp, HashSet<string> enabled)
        {
            if (enabled.Contains("query")) mcp = mcp.WithTools<QueryTools>();
            if (enabled.Contains("modify")) mcp = mcp.WithTools<ModifyTools>();
            if (enabled.Contains("code")) mcp = mcp.WithTools<CodeTools>();
            return mcp;
        }
    }
}
