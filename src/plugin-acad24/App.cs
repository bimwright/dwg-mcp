using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Bimwright.Dwg.Plugin.Views.Toast;

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
            try { ToastRuntime.Shutdown(); } catch { }
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
            ToastRuntime.Stop();
            if (_server == null || !_server.IsRunning)
            {
                WriteLine("Bimwright DWG not running.");
                return;
            }
            _server.Stop();
            _server = null;
            WriteLine("Bimwright DWG stopped.");
        }

        [CommandMethod("MCPTOAST", CommandFlags.Session)]
        public static void McpToast()
        {
            try { ToastRuntime.Toggle(); }
            catch (System.Exception ex) { WriteLine(McpResponsePrivacy.SanitizeError(ex.Message)); }
        }

        [CommandMethod("MCPENABLECODE", CommandFlags.Session)]
        public static void McpEnableCode()
        {
            CommandDispatcher.SetSendCodeEnabled(true);
            WriteLine("send_code re-enabled for this AutoCAD session.");
        }

        [CommandMethod("MCPDISABLECODE", CommandFlags.Session)]
        public static void McpDisableCode()
        {
            CommandDispatcher.SetSendCodeEnabled(false);
            WriteLine("send_code disabled for this AutoCAD session.");
        }

        private static void StartServerInternal()
        {
            CommandDispatcher.SetSendCodeEnabled(true);
            System.Action<McpToastViewModel> notify = null;
            try { notify = ToastRuntime.Start(); }
            catch (System.Exception ex) { WriteLine("Toast unavailable: " + McpResponsePrivacy.SanitizeError(ex.Message)); }
#if ACAD2025_OR_GREATER
            _server = new PipeTransportServer(PluginTarget.AutoCadYear, notify: notify);
#else
            _server = new TcpTransportServer(PluginTarget.AutoCadYear, notify);
#endif
            try { _server.Start(); }
            catch { ToastRuntime.Stop(); throw; }
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
