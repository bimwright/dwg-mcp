using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Bimwright.Dwg.Plugin.Localization;
using Bimwright.Dwg.Plugin.ToolCatalog;
using Bimwright.Dwg.Plugin.Views;
using Bimwright.Dwg.Plugin.Views.Settings;
using Bimwright.Dwg.Plugin.Views.Toast;
using Bimwright.Dwg.Plugin.Views.Update;

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
        private static ReleaseNoticeCoordinator _releaseNotice;
        private static HistoryWindow _history;
        private static System.Windows.Threading.Dispatcher _uiDispatcher;

        // Card title ("dwg-mcp 2024"): tells cards from parallel gateways and AutoCAD years apart.
        private const string ToastTitle = Views.BrandAssets.ProductName + " " + PluginTarget.AutoCadYear;

        /// <summary>Set at startup; CommandDispatcher fires completion toasts through it.</summary>
        public static McpToastNotifier ToastNotifier { get; private set; }

        internal static bool ToastEnabled => _toastEnabled;
        internal static bool ListenerRunning => _server != null && _server.IsRunning;
        internal static bool ListenerUsesPipe => _server != null && _server.Kind == TransportKind.Pipe;
        internal static int? ListenerPort => ListenerRunning ? _server.Port : null;
        internal static string ListenerConnectionInfo => ListenerRunning ? _server.ConnectionInfo : null;
        internal static bool AutoStartTransport { get; private set; } = PluginSettings.DefaultAutoStartTransport;
        internal static string AutoStartSource { get; private set; } = "default";
        internal static string AutoStartSaveError { get; private set; }

        internal static bool ClientRecentlyPresent =>
            _lastClientActivityUtc != System.DateTime.MinValue
            && System.DateTime.UtcNow - _lastClientActivityUtc < ClientPresenceGap;

        public void Initialize()
        {
            L.Initialize(PluginSettings.LoadUiLanguage());
            DiagnosticLog.MaintainLegacyLogs();
            OutputArtifactStore.MaintainInBackground();
            Reports.Acad.ReportsModule.CleanupInBackground();
            LoadStartupPreference();
            try
            {
                _releaseNotice = new ReleaseNoticeCoordinator();
                _releaseNotice.SetEnabled(PluginSettings.LoadReleaseNotifications());
            }
            catch (System.Exception ex) { PluginLog.Debug("Release check could not start: " + ex.GetType().Name); }
            try
            {
                InitializeToast();
                if (!AutoStartTransport)
                {
                    WriteLine("Bimwright DWG loaded; auto-start is off. Run MCPSTART to start listening.");
                    return;
                }
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
            try { _releaseNotice?.SetEnabled(false); } catch { }
            _releaseNotice = null;
            try { _history?.Close(); } catch { }
            _history = null;
            try { Reports.Acad.ReportsModule.Shutdown(); } catch { }
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
                SaveStartupPreference(true);
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
                return;
            }
            SaveStartupPreference(true);
        }

        [CommandMethod("MCPSTOP", CommandFlags.Session)]
        public static void McpStop()
        {
            StopListener(closeSettings: true);
        }

        private static void LoadStartupPreference()
        {
            AutoStartTransport = PluginSettings.LoadAutoStartTransport(out var source);
            AutoStartSource = source;
        }

        private static void SaveStartupPreference(bool enabled)
        {
            PluginSettings.SaveAutoStartTransport(enabled, out var error);
            AutoStartSaveError = error;
            LoadStartupPreference();
            if (error != null)
                WriteLine("Startup preference could not be saved; the listener keeps its current state. " + error);
            else
                WriteLine("Startup preference saved: " + (enabled ? "on." : "off."));
            if (AutoStartSource == PluginSettings.EnvAutoStartTransport)
                WriteLine(PluginSettings.EnvAutoStartTransport + " overrides the saved preference at next launch.");
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

        [CommandMethod("MCPHISTORY", CommandFlags.Session)]
        public static void McpHistory()
        {
            ShowOrFocusHistory();
        }

        [CommandMethod("MCPREPORTS", CommandFlags.Session)]
        public static void McpReports()
        {
            ShowReports(null);
        }

        /// <summary>Open the Reports window, on <paramref name="reportId"/> when given (a toast click).</summary>
        internal static void ShowReports(string reportId)
        {
            var dispatcher = _uiDispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                if (!dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
                    dispatcher.BeginInvoke(new System.Action(() => ShowReports(reportId)));
                return;
            }
            try
            {
                if (reportId == null) Reports.Acad.ReportsModule.ShowOrFocus();
                else Reports.Acad.ReportsModule.ShowReport(reportId);
            }
            catch (System.Exception ex) { WriteLine("Reports could not open: " + ex.Message); }
        }

        internal static void ShowOrFocusHistory()
        {
            var dispatcher = _uiDispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                if (!dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
                    dispatcher.BeginInvoke(new System.Action(ShowOrFocusHistory));
                return;
            }
            if (ToastNotifier == null)
                InitializeToast();
            if (_history != null)
            {
                if (_history.WindowState == System.Windows.WindowState.Minimized)
                    _history.WindowState = System.Windows.WindowState.Normal;
                _history.Activate();
                return;
            }
            var window = new HistoryWindow(ToastNotifier.History, ToastTitle);
            window.Closed += (_, __) =>
            {
                if (ReferenceEquals(_history, window))
                    _history = null;
            };
            _history = window;
            try { Application.ShowModelessWindow(window); }
            catch (System.Exception ex)
            {
                _history = null;
                try { window.Close(); } catch { }
                WriteLine("History could not open: " + ex.Message);
            }
        }

        internal static void SetToastEnabled(bool enabled)
        {
            _toastEnabled = enabled;
            var saved = PluginSettings.SaveEnableToast(enabled);
            WriteLine(enabled
                ? "Toast notifications enabled (saved)."
                : "Toast notifications disabled (saved).");
            if (!saved)
                WriteLine(L.T("toast.status.saveFailed"));
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
            SaveStartupPreference(false);
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
            window.ReleaseNotificationsChanged += enabled => _releaseNotice?.SetEnabled(enabled);
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
            ToastNotifier.SetInstanceInfo(ToastTitle);
            // A card whose latest result is an agent report opens that report; any other card opens History.
            ToastNotifier.SetActivityClickHandler(cardId =>
            {
                var reportId = ToastNotifier?.ReportFor(cardId);
                if (reportId != null) ShowReports(reportId);
                else ShowOrFocusHistory();
            });

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
                _uiDispatcher = dispatcher;
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
            try { _releaseNotice?.OnIdle(); } catch (System.Exception ex) { PluginLog.Debug("Release notice idle failed: " + ex.GetType().Name); }
            try { Reports.Acad.ReportsModule.OnIdle(); } catch (System.Exception ex) { PluginLog.Debug("Reports idle refresh failed: " + ex.Message); }
            var notifier = ToastNotifier;
            if (notifier == null)
                return;

            notifier.SetInstanceInfo(ToastTitle);
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
