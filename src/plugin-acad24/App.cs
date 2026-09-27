using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Bimwright.Dwg.Plugin.ToolCatalog;
using Bimwright.Dwg.Plugin.Views.Settings;
using Bimwright.Dwg.Plugin.Views.Toast;

[assembly: ExtensionApplication(typeof(Bimwright.Dwg.Plugin.App))]
[assembly: CommandClass(typeof(Bimwright.Dwg.Plugin.App))]

namespace Bimwright.Dwg.Plugin
{
    public class App : IExtensionApplication
    {
        private static ITransportServer _server;
        private static bool _toastEnabled;
        private static bool _wasClientPresent;
        private static bool _idleHooked;
        private static System.DateTime _lastClientActivityUtc = System.DateTime.MinValue;

        // The dwg wire is connect-per-request: every tool call opens a fresh
        // socket, so a raw IsClientConnected edge would fire on each call.
        // Treat the agent as present while a socket is open or a command ran
        // within this window; the "agent connected" toast then fires once per
        // burst of activity instead of once per call.
        private static readonly System.TimeSpan ClientPresenceGap = System.TimeSpan.FromSeconds(30);
        private static SettingsWindow _settings;

        /// <summary>Set at startup; CommandDispatcher fires completion toasts through it.</summary>
        public static McpToastNotifier ToastNotifier { get; private set; }

        internal static bool ToastEnabled => _toastEnabled;
        internal static bool ListenerRunning => _server != null && _server.IsRunning;
        internal static bool ListenerUsesPipe => _server != null && _server.Kind == TransportKind.Pipe;
        internal static int? ListenerPort => ListenerRunning ? _server.Port : null;

        internal static bool ClientRecentlyPresent =>
            _lastClientActivityUtc != System.DateTime.MinValue
            && System.DateTime.UtcNow - _lastClientActivityUtc < ClientPresenceGap;

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
            if (_idleHooked)
            {
                try { Application.Idle -= OnIdleToast; } catch { }
                _idleHooked = false;
            }
            try { _server?.Stop(); } catch { }
            try { ToolCatalogStore.Clear(); } catch { }
            try { CloseSettings(); } catch { }
            try { ToastNotifier?.Shutdown(); } catch { }
            try { DocumentInvoker.Shutdown(); } catch { }
            try { View.DrawingRevision.Shutdown(); } catch { }
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
            StopListener(closeSettings: true);
        }

        [CommandMethod("MCPTOAST", CommandFlags.Session)]
        public static void McpToast()
        {
            SetToastEnabled(!_toastEnabled);
        }

        [CommandMethod("MCPSETTINGS", CommandFlags.Session)]
        public static void McpSettings()
        {
            ShowOrFocusSettings();
        }

        internal static void SetToastEnabled(bool enabled)
        {
            _toastEnabled = enabled;
            var saved = PluginSettings.SaveEnableToast(enabled);
            WriteLine(enabled
                ? "Toast notifications enabled (saved)."
                : "Toast notifications disabled (saved).");
            if (!saved)
                WriteLine("Toast preference could not be saved; this session is still using the new state.");
            if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable(PluginSettings.EnvEnableToast)))
                WriteLine($"{PluginSettings.EnvEnableToast} is set and re-applies at next launch.");
            ToastNotifier?.OnToastEnabledChanged(enabled, saved);
        }

        internal static void StartListener()
        {
            McpStart();
        }

        internal static void StopListener(bool closeSettings)
        {
            if (_server != null && _server.IsRunning)
            {
                _server.Stop();
                _server = null;
            }
            ToolCatalogStore.Clear();
            ToastNotifier?.DismissAll();
            if (closeSettings)
                CloseSettings();
            WriteLine("Bimwright DWG stopped.");
        }

        internal static void ShowOrFocusSettings()
        {
            if (_settings != null)
            {
                try { _settings.Activate(); } catch { }
                return;
            }

            var window = new SettingsWindow();
            window.Closed += (_, __) =>
            {
                if (ReferenceEquals(_settings, window))
                    _settings = null;
            };
            _settings = window;
            try
            {
                Application.ShowModelessWindow(window);
            }
            catch (System.Exception ex)
            {
                _settings = null;
                try { window.Close(); } catch { }
                WriteLine("Settings could not open: " + ex.Message);
            }
        }

        internal static void CloseSettings()
        {
            var window = _settings;
            _settings = null;
            if (window == null)
                return;
            try { window.Close(); } catch { }
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
            ToolCatalogStore.Clear();
            DocumentInvoker.Initialize();
            InitializeToast();
            CommandDispatcher.SetSendCodeEnabled(true);
#if ACAD2025_OR_GREATER
            _server = new PipeTransportServer(PluginTarget.AutoCadYear);
#else
            _server = new TcpTransportServer(PluginTarget.AutoCadYear);
#endif
            _server.Start();
        }

        private static void InitializeToast()
        {
            if (ToastNotifier != null)
                return;

            _toastEnabled = PluginSettings.LoadToastEnabled();
            var host = new McpToastHost();
            ToastNotifier = new McpToastNotifier(host, () => _toastEnabled);
            ToastNotifier.SetInstanceInfo("AutoCAD " + PluginTarget.AutoCadYear);

            // Anchor toasts to the AutoCAD window and reuse the host WPF dispatcher
            // when AutoCAD already runs one (its ribbon is WPF).
            try
            {
                var hwnd = Application.MainWindow != null ? Application.MainWindow.Handle : System.IntPtr.Zero;
                if (hwnd != System.IntPtr.Zero)
                    ToastNotifier.SetOwnerHandle(hwnd);
            }
            catch { }
            try
            {
                var dispatcher = System.Windows.Application.Current?.Dispatcher
                    ?? System.Windows.Threading.Dispatcher.CurrentDispatcher;
                ToastNotifier.SetHostDispatcher(dispatcher);
            }
            catch { }

            if (!_idleHooked)
            {
                Application.Idle += OnIdleToast;
                _idleHooked = true;
            }
        }

        /// <summary>
        /// Runs on the AutoCAD main thread: flushes toasts held while the frame was
        /// minimized/modal, and toasts when an agent becomes present (first attach
        /// or return after a ClientPresenceGap of silence).
        /// </summary>
        private static void OnIdleToast(object sender, System.EventArgs args)
        {
            var notifier = ToastNotifier;
            if (notifier == null)
                return;

            notifier.SetInstanceInfo("AutoCAD " + PluginTarget.AutoCadYear);
            notifier.FlushPendingIfUsable();

            var now = System.DateTime.UtcNow;
            var active = _server != null && _server.IsRunning
                && (_server.IsClientConnected
                    || (_server.LastCommandTime.HasValue
                        && now - _server.LastCommandTime.Value < ClientPresenceGap));
            if (active)
                _lastClientActivityUtc = now;

            var present = _lastClientActivityUtc != System.DateTime.MinValue
                && now - _lastClientActivityUtc < ClientPresenceGap;
            if (present && !_wasClientPresent)
                notifier.OnClientConnected(_server.ConnectionInfo);
            _wasClientPresent = present;
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
