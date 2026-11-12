using System;
using System.Collections.Generic;
using System.Linq;
using Bimwright.Dwg.Server.Tools;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace Bimwright.Dwg.Server
{
    public partial class Program
    {
        internal static readonly (string[] Toolsets, Type ToolType, bool ExcludeWhenReadOnly,
            Func<IMcpServerBuilder, IMcpServerBuilder> Register)[] ToolRegistrations =
        {
            (new[] { "query" }, typeof(QueryTools), false, static mcp => mcp.WithTools<QueryTools>()),
            (new[] { "query" }, typeof(HealthTools), false, RegisterHealthToolsWithRuntimeMeta),
            (new[] { "modify" }, typeof(ModifyTools), true, static mcp => mcp.WithTools<ModifyTools>()),
            (new[] { "meta" }, typeof(MetaTools), false, RegisterMetaToolsWithReadOnlyPolicy),
            (new[] { "meta" }, typeof(BatchTools), true, static mcp => mcp.WithTools<BatchTools>()),
            (new[] { "meta" }, typeof(ReportTools), false, static mcp => mcp.WithTools<ReportTools>()),
            (new[] { "toolbaker" }, typeof(ToolBakerTools), false, static mcp => mcp.WithTools<ToolBakerTools>()),
            (new[] { "toolbaker" }, typeof(ToolBakerWriteTools), true, static mcp => mcp.WithTools<ToolBakerWriteTools>()),
            // meta carries code on the default surface; code remains an explicit alias.
            (new[] { "meta", "code" }, typeof(CodeTools), true, RegisterCodeToolsWithSendCodePolicy),
            (new[] { "annotation" }, typeof(AnnotationTools), true, static mcp => mcp.WithTools<AnnotationTools>()),
            (new[] { "block" }, typeof(BlockTools), false, static mcp => mcp.WithTools<BlockTools>()),
            (new[] { "block" }, typeof(BlockWriteTools), true, static mcp => mcp.WithTools<BlockWriteTools>()),
            (new[] { "dimension" }, typeof(DimensionTools), true, static mcp => mcp.WithTools<DimensionTools>()),
            (new[] { "view" }, typeof(ViewTools), false, RegisterViewToolsWithReadOnlyPolicy),
            (new[] { "export" }, typeof(ExportTools), true, static mcp => mcp.WithTools<ExportTools>()),
            (new[] { "drawing" }, typeof(DrawingTools), false, static mcp => mcp.WithTools<DrawingTools>()),
            (new[] { "drawing" }, typeof(DrawingWriteTools), true, static mcp => mcp.WithTools<DrawingWriteTools>())
        };

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
            foreach (var registration in ToolRegistrations)
                if ((!readOnly || !registration.ExcludeWhenReadOnly)
                    && registration.Toolsets.Any(enabled.Contains))
                    toolTypes.Add(registration.ToolType);
            return toolTypes;
        }

        private static IMcpServerBuilder RegisterToolType(IMcpServerBuilder mcp, Type toolType)
        {
            foreach (var registration in ToolRegistrations)
                if (registration.ToolType == toolType)
                    return registration.Register(mcp);

            throw new InvalidOperationException("Unsupported MCP tool type: " + toolType.FullName);
        }

        private static IMcpServerBuilder RegisterHealthToolsWithRuntimeMeta(IMcpServerBuilder mcp)
        {
            var tool = McpServerTool.Create(typeof(HealthTools).GetMethod(nameof(HealthTools.Health)), _ => new HealthTools());
            tool.ProtocolTool.Meta = new System.Text.Json.Nodes.JsonObject
            {
                [HealthRuntimeInfo.ClientRuntimeMetadataKey] = System.Text.Json.Nodes.JsonNode.Parse(
                    HealthRuntimeInfo.ClientRuntime(GatewayRuntime.Identity).ToString())
            };
            return mcp.WithTools(new[] { tool });
        }

        private static IMcpServerBuilder RegisterCodeToolsWithSendCodePolicy(IMcpServerBuilder mcp)
            => mcp.WithTools(SendCodePolicy.ExposedMethods(typeof(CodeTools),
                ServerState.Config?.EnableSendCodeOrDefault ?? true)
                .Select(method => McpServerTool.Create(method)));
        private static IMcpServerBuilder RegisterMetaToolsWithReadOnlyPolicy(IMcpServerBuilder mcp)
            => mcp.WithTools(ReadOnlyPolicy.ExposedMethods(typeof(MetaTools), ServerState.IsReadOnly)
                .Select(method => McpServerTool.Create(method)));
        private static IMcpServerBuilder RegisterViewToolsWithReadOnlyPolicy(IMcpServerBuilder mcp)
            => mcp.WithTools(ReadOnlyPolicy.ExposedMethods(typeof(ViewTools), ServerState.IsReadOnly)
                .Select(method => McpServerTool.Create(method)));

    }
}
