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
- optional toolbaker, when enabled: dwg_list_baked_tools, dwg_run_baked_tool, dwg_list_bake_suggestions, dwg_accept_bake_suggestion, dwg_dismiss_bake_suggestion, dwg_create_bake_issue_draft.
- code: dwg_send_code is disabled unless --enable-send-code or BIMWRIGHT_DWG_ENABLE_SEND_CODE=1.

General CAD tools operate on the current AutoCAD active document. Entity arguments use AutoCAD hex handles returned by selection, creation, or property tools.
Call dwg_get_selected_texts before text writeback. In read-only mode only query/routing/list tools are exposed.";

        private static IMcpServerBuilder RegisterToolsets(IMcpServerBuilder mcp, HashSet<string> enabled)
        {
            foreach (var toolType in ResolveToolTypesForRegistration(enabled, ServerState.IsReadOnly))
            {
                mcp = RegisterToolType(mcp, toolType);
            }

            return mcp;
        }

        private static IEnumerable<Type> ResolveToolTypesForRegistration(HashSet<string> enabled, bool readOnly)
        {
            enabled = enabled ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var toolTypes = new List<Type>();
            if (enabled.Contains("query")) toolTypes.Add(typeof(QueryTools));
            if (enabled.Contains("modify") && !readOnly) toolTypes.Add(typeof(ModifyTools));
            if (enabled.Contains("meta"))
            {
                toolTypes.Add(typeof(MetaTools));
                if (!readOnly) toolTypes.Add(typeof(BatchTools));
            }
            if (enabled.Contains("toolbaker"))
            {
                toolTypes.Add(typeof(ToolBakerTools));
                if (!readOnly) toolTypes.Add(typeof(ToolBakerWriteTools));
            }
            if (enabled.Contains("code") && !readOnly) toolTypes.Add(typeof(CodeTools));
            if (enabled.Contains("annotation") && !readOnly) toolTypes.Add(typeof(AnnotationTools));
            if (enabled.Contains("block"))
            {
                toolTypes.Add(typeof(BlockTools));
                if (!readOnly) toolTypes.Add(typeof(BlockWriteTools));
            }
            if (enabled.Contains("dimension") && !readOnly) toolTypes.Add(typeof(DimensionTools));

            if (enabled.Contains("view"))
            {
                toolTypes.Add(typeof(ViewTools));
            }
            if (enabled.Contains("export") && !readOnly)
            {
                toolTypes.Add(typeof(ExportTools));
            }
            if (enabled.Contains("drawing"))
            {
                toolTypes.Add(typeof(DrawingTools));
                if (!readOnly) toolTypes.Add(typeof(DrawingWriteTools));
            }
            return toolTypes;
        }

        private static IMcpServerBuilder RegisterToolType(IMcpServerBuilder mcp, Type toolType)
        {
            if (toolType == typeof(QueryTools)) return mcp.WithTools<QueryTools>();
            if (toolType == typeof(ModifyTools)) return mcp.WithTools<ModifyTools>();
            if (toolType == typeof(MetaTools)) return mcp.WithTools<MetaTools>();
            if (toolType == typeof(BatchTools)) return mcp.WithTools<BatchTools>();
            if (toolType == typeof(ToolBakerTools)) return mcp.WithTools<ToolBakerTools>();
            if (toolType == typeof(ToolBakerWriteTools)) return mcp.WithTools<ToolBakerWriteTools>();
            if (toolType == typeof(CodeTools)) return mcp.WithTools<CodeTools>();
            if (toolType == typeof(AnnotationTools)) return mcp.WithTools<AnnotationTools>();
            if (toolType == typeof(BlockTools)) return mcp.WithTools<BlockTools>();
            if (toolType == typeof(BlockWriteTools)) return mcp.WithTools<BlockWriteTools>();
            if (toolType == typeof(DimensionTools)) return mcp.WithTools<DimensionTools>();
            if (toolType == typeof(ViewTools)) return mcp.WithTools<ViewTools>();
            if (toolType == typeof(ExportTools)) return mcp.WithTools<ExportTools>();
            if (toolType == typeof(DrawingTools)) return mcp.WithTools<DrawingTools>();
            if (toolType == typeof(DrawingWriteTools)) return mcp.WithTools<DrawingWriteTools>();

            throw new InvalidOperationException("Unsupported MCP tool type: " + toolType.FullName);
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
