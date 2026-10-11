using System.Linq;

namespace Bimwright.Dwg.Server
{
    public partial class Program
    {
        internal static readonly string ServerInformationalVersion = typeof(Program).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .Cast<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "0.0.0";
        internal static readonly string ServerVersion = ServerInformationalVersion.Split('+')[0];

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
            opts.ServerInstructions = ServerInstructions.Text;
        }
    }
}
