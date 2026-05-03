using System;
using System.Threading.Tasks;
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
            var builder = Host.CreateApplicationBuilder(args);
            // MCP stdio transport owns stdout. Route logs to stderr.
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
            var mcp = builder.Services
                .AddMcpServer()
                .WithStdioServerTransport()
                .WithTools<Tools>();

            if (IsSendCodeEnabled(args))
            {
                mcp.WithTools<CodeTools>();
            }

            await builder.Build().RunAsync();
        }

        public static bool IsSendCodeEnabled(string[] args)
        {
            if (args != null)
            {
                foreach (var arg in args)
                {
                    if (string.Equals(arg, "--enable-send-code", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            var env = Environment.GetEnvironmentVariable("BIMWRIGHT_DWG_ENABLE_SEND_CODE");
            return string.Equals(env, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(env, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(env, "yes", StringComparison.OrdinalIgnoreCase);
        }
    }
}
