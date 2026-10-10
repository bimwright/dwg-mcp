using System;
using System.Collections.Generic;
using System.Linq;
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
        internal static readonly string ServerInformationalVersion = typeof(Program).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .Cast<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "0.0.0";
        internal static readonly string ServerVersion = ServerInformationalVersion.Split('+')[0];

        public static async Task Main(string[] args)
        {
            if (args != null && args.Any(a => a == "--help" || a == "-h"))
            {
                PrintHelp();
                return;
            }

            var config = DwgMcpConfig.Load(args);
            ValidateTarget(config.Target);
            ServerState.Config = config;
            WarnIfUnwiredOptions(config);
            var enabled = ToolsetFilter.Resolve(config);
            _ = GatewayRuntime.Identity; // Capture loaded gateway identity before initialize/tools/list.

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

        private static void ConfigureMcpServerOptions(ModelContextProtocol.Server.McpServerOptions opts)
        {
            opts.ServerInfo = new ModelContextProtocol.Protocol.Implementation
            {
                Name = "dwg-mcp",
                Title = "DWG MCP",
                Version = ServerInformationalVersion,
                Description = "Model Context Protocol gateway for Autodesk AutoCAD DWG workflows",
                WebsiteUrl = "https://github.com/bimwright/dwg-mcp"
            };
            opts.ServerInstructions = ServerInstructionsText;
        }

        private const string ServerInstructionsText =
@"dwg-mcp - MCP gateway for Autodesk AutoCAD DWG drawings. Use for current drawing metadata, layers, entity properties, simple line/circle creation, selected AutoCAD text, DBText, MText, SHX/Unicode style repair, Vietnamese translation writeback, clustered note cleanup, and DWG text rewriting.

AutoCAD routing: versions are 4-digit years 2022..2027. If multiple AutoCAD instances run, use dwg_list_available_targets then dwg_switch_target, or start the server with --target 2022|2023|2024|2025|2026|2027.

At session start select the intended target, then call dwg_health(check_updates=true) once. Routine health probes stay offline; not_checked/unknown never means up to date. Health reports host/document state and loaded server/plugin identities without requiring or locking a drawing. No target preserves gateway identity; an unsupported old plugin needs runtime observation, not a corruption warning.
Read both update.components and the full update.notification message, lookup_reason, protected_components/protected_component_reasons and unobserved_components. Follow agent_action. UPDATE_CHECK_UNAVAILABLE describes external lookup failure, not a local runtime fault or installation authorization. LOCAL_VERSION_AHEAD is informational: preserve newer components and never downgrade. Missing release revision evidence stops the update workflow, not otherwise healthy AutoCAD work. Only update_guidance.mode=prepare_update with bundle_update_proposal_allowed=true permits a release ZIP proposal and needs installation authorization; reuse existing permission. Other modes contain check/report/review only. Health never downloads packages, installs, changes client configs, saves/closes drawings or restarts processes. Never automatically replace development builds. Compare fresh initialize and tools/list runtime metadata/schema/annotations on the same reconnect with client_verification; the server cannot observe client cache refresh. Matching source revisions do not verify artifact bytes, compatibility or publisher trust.

Tools use prefix dwg_:
- query: dwg_health;  dwg_get_drawing_info, dwg_list_layers, and dwg_get_entity_properties read the current active document; dwg_get_selected_texts reads current AutoCAD pickfirst text selection and returns clustered text groups; dwg_inspect_lisp statically scans .lsp files or inline AutoLISP for known suspicious patterns (verdict clean/caution/dangerous). No verdict certifies safety or authorizes execution.
- modify: dwg_create_layer, dwg_create_line, dwg_create_circle, dwg_change_layer, dwg_update_texts, dwg_translate_and_rewrite, dwg_apply_unicode_style, dwg_collapse_and_rewrite write drawing/text/style changes.
- meta: dwg_batch_execute, dwg_list_available_targets, dwg_get_current_target, dwg_switch_target, dwg_send_code, dwg_run_lisp.
- view: Start visual reading with dwg_capture_view_image (inline image plus JSON). If region_navigation.supported, choose a normalized top-left-origin rectangle on that image and call dwg_inspect_view_region with its capture_id. Inspect the new image before deciding the next region. Keep ancestor capture IDs and use dwg_restore_view to return to an overview. Use each newly returned ID for further navigation. Do not interpret a failed image_delivery as a failed zoom: take a smaller fresh capture. Region navigation is limited to one top-down, unrotated, orthographic model-space viewport. Verify inferred labels/dimensions against CAD text and entity data. Do not claim unseen regions have been reviewed.
- optional toolbaker, when enabled: dwg_list_baked_tools, dwg_run_baked_tool, dwg_list_bake_suggestions, dwg_accept_bake_suggestion, dwg_dismiss_bake_suggestion, dwg_create_bake_issue_draft.
- dwg_send_code is on by default; --disable-send-code removes it and blocks batch dispatch. --read-only also removes it. The AutoCAD MCPDISABLECODE command disables it for the session (MCPENABLECODE re-enables only the host gate, not the server setting).
- dwg_run_lisp is a compatibility refusal: all execution is blocked at server and plugin until an enforceable isolation/trust policy exists. MCPENABLECODE does not unblock LISP. Do not bypass refusal through send_code, batch, ToolBaker, or reformatted payloads. send_code remains full-trust C#, not a sandbox or machine-wide malware protection.

General CAD tools operate on the current AutoCAD active document. Entity arguments use AutoCAD hex handles returned by selection, creation, or property tools.
Call dwg_get_selected_texts before text writeback. Read-only mode permits queries, selection and standalone view zoom, but no drawing edits or file output. Capture, region inspection and view restore write image files and are unavailable in read-only mode.";

        private static IMcpServerBuilder RegisterToolsets(IMcpServerBuilder mcp, HashSet<string> enabled)
        {
            var toolTypes = new List<Type>(ResolveToolTypesForRegistration(enabled, ServerState.IsReadOnly));
            ServerState.EnabledToolTypes = toolTypes;
            foreach (var toolType in toolTypes)
            {
                mcp = RegisterToolType(mcp, toolType);
            }

            return mcp;
        }

        private static IEnumerable<Type> ResolveToolTypesForRegistration(HashSet<string> enabled, bool readOnly)
        {
            enabled = enabled ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var toolTypes = new List<Type>();
            if (enabled.Contains("query"))
            {
                toolTypes.Add(typeof(QueryTools));
                toolTypes.Add(typeof(HealthTools));
            }
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
            // dwg_send_code ships on the default surface via meta (rvt-mcp parity); the
            // legacy "code" toolset name is still honored as an explicit opt-in alias.
            if ((enabled.Contains("meta") || enabled.Contains("code")) && !readOnly) toolTypes.Add(typeof(CodeTools));
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
            if (toolType == typeof(HealthTools))
            {
                var tool = McpServerTool.Create(typeof(HealthTools).GetMethod(nameof(HealthTools.Health)), _ => new HealthTools());
                tool.ProtocolTool.Meta = new System.Text.Json.Nodes.JsonObject
                {
                    [HealthRuntimeInfo.ClientRuntimeMetadataKey] = System.Text.Json.Nodes.JsonNode.Parse(
                        HealthRuntimeInfo.ClientRuntime(GatewayRuntime.Identity).ToString())
                };
                return mcp.WithTools(new[] { tool });
            }
            if (toolType == typeof(QueryTools)) return mcp.WithTools<QueryTools>();
            if (toolType == typeof(ModifyTools)) return mcp.WithTools<ModifyTools>();
            if (toolType == typeof(MetaTools)) return mcp.WithTools<MetaTools>();
            if (toolType == typeof(BatchTools)) return mcp.WithTools<BatchTools>();
            if (toolType == typeof(ToolBakerTools)) return mcp.WithTools<ToolBakerTools>();
            if (toolType == typeof(ToolBakerWriteTools)) return mcp.WithTools<ToolBakerWriteTools>();
            if (toolType == typeof(CodeTools))
                return mcp.WithTools(SendCodePolicy.ExposedMethods(toolType,
                    ServerState.Config?.EnableSendCodeOrDefault ?? true)
                    .Select(method => McpServerTool.Create(method)));
            if (toolType == typeof(AnnotationTools)) return mcp.WithTools<AnnotationTools>();
            if (toolType == typeof(BlockTools)) return mcp.WithTools<BlockTools>();
            if (toolType == typeof(BlockWriteTools)) return mcp.WithTools<BlockWriteTools>();
            if (toolType == typeof(DimensionTools)) return mcp.WithTools<DimensionTools>();
            if (toolType == typeof(ViewTools))
                return mcp.WithTools(ReadOnlyPolicy.ExposedMethods(toolType, ServerState.IsReadOnly)
                    .Select(method => McpServerTool.Create(method)));
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

        private static void PrintHelp()
        {
            Console.WriteLine(
                "dwg-mcp - MCP stdio gateway for Autodesk AutoCAD 2022-2027.\n" +
                "\n" +
                "Usage: dwg-mcp [options]\n" +
                "\n" +
                "Options:\n" +
                "  --target <year>        Pin to AutoCAD 2022|2023|2024|2025|2026|2027.\n" +
                "  --toolsets <csv>       Toolsets to register (default: query,modify,meta,view).\n" +
                "                         'all' enables the full surface.\n" +
                "  --read-only            Strip every write/execute tool.\n" +
                "  --enable-send-code / --disable-send-code (default: enabled).\n" +
                "  --enable-call-log / --disable-call-log (default: disabled).\n" +
                "  --enable-toolbaker / --disable-toolbaker\n" +
                "  --allow-lan-bind       Parsed but not yet implemented (loopback only).\n" +
                "  --log-level <level>    trace|debug|information|warning|error.\n" +
                "  --config <path>        JSON config file. Default:\n" +
                "                         %LOCALAPPDATA%\\Bimwright\\dwg-mcp\\dwgmcp.config.json\n" +
                "\n" +
                "Environment overrides: BIMWRIGHT_DWG_TARGET, BIMWRIGHT_DWG_TOOLSETS,\n" +
                "BIMWRIGHT_DWG_READ_ONLY, BIMWRIGHT_DWG_ENABLE_TOOLBAKER,\n" +
                "BIMWRIGHT_DWG_ENABLE_SEND_CODE, BIMWRIGHT_DWG_ENABLE_CALL_LOG,\n" +
                "BIMWRIGHT_DWG_ALLOW_LAN_BIND, BIMWRIGHT_DWG_LOG_LEVEL.\n" +
                "\n" +
                "  -h, --help             Show this help and exit.");
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
