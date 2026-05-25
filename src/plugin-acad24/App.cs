using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;

[assembly: ExtensionApplication(typeof(Bimwright.Dwg.Plugin.App))]
[assembly: CommandClass(typeof(Bimwright.Dwg.Plugin.App))]

namespace Bimwright.Dwg.Plugin
{
    public class App : IExtensionApplication
    {
        private static ITransportServer _server;

        public void Initialize()
        {
            try
            {
                StartServerInternal();
                WriteLine($"Bimwright DWG loaded and listening on {DescribeTransport(_server)}.");
            }
            catch (System.Exception ex)
            {
                WriteLine($"Bimwright DWG loaded; auto-start failed ({ex.Message}). Run MCPSTART to retry.");
            }
        }

        public void Terminate()
        {
            try { _server?.Stop(); } catch { }
        }

        [CommandMethod("MCPSTART", CommandFlags.Session)]
        public static void McpStart()
        {
            if (_server != null && _server.IsRunning)
            {
                WriteLine($"Bimwright DWG already running on {DescribeTransport(_server)}.");
                return;
            }
            try
            {
                StartServerInternal();
                WriteLine($"Bimwright DWG listening on {DescribeTransport(_server)}.");
            }
            catch (System.Exception ex)
            {
                WriteLine($"Bimwright DWG start failed: {ex.Message}");
            }
        }

        [CommandMethod("MCPSTOP", CommandFlags.Session)]
        public static void McpStop()
        {
            if (_server == null || !_server.IsRunning)
            {
                WriteLine("Bimwright DWG not running.");
                return;
            }
            _server.Stop();
            _server = null;
            WriteLine("Bimwright DWG stopped.");
        }

        [CommandMethod("MCPENABLECODE", CommandFlags.Session)]
        public static void McpEnableCode()
        {
            CommandDispatcher.SetSendCodeEnabled(true);
            WriteLine("send_code enabled for this AutoCAD session. Start the MCP server with --enable-send-code to expose it.");
        }

        [CommandMethod("MCPDISABLECODE", CommandFlags.Session)]
        public static void McpDisableCode()
        {
            CommandDispatcher.SetSendCodeEnabled(false);
            WriteLine("send_code disabled for this AutoCAD session.");
        }

        private static void StartServerInternal()
        {
            CommandDispatcher.SetSendCodeEnabled(false);
#if ACAD2025_OR_GREATER
            _server = new PipeTransportServer(PluginTarget.AutoCadYear);
#else
            _server = new TcpTransportServer(PluginTarget.AutoCadYear);
#endif
            _server.Start();
        }

        private static string DescribeTransport(ITransportServer server)
        {
            if (server == null)
            {
                return "no active transport";
            }

            return server.Kind == TransportKind.Pipe
                ? "pipe " + server.PipeName
                : "port " + server.Port;
        }

        private static void WriteLine(string message)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            doc?.Editor.WriteMessage($"\n[Bimwright.Dwg] {message}\n");
        }
    }
}
