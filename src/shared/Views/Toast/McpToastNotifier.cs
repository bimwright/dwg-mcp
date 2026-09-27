using System;
using System.Runtime.InteropServices;
using Bimwright.Dwg.Plugin.Localization;

namespace Bimwright.Dwg.Plugin.Views.Toast
{
    /// <summary>
    /// Adapts finished tool calls to the activity state machine. The notifier never
    /// constructs a window. ActivityAggregator is the source of truth for the card.
    /// </summary>
    public sealed class McpToastNotifier
    {
        private readonly McpToastHost _host;
        private readonly ActivityAggregator _activity;
        private readonly Func<bool> _isEnabled;
        private IntPtr _ownerHwnd;

        public McpToastNotifier(McpToastHost host, Func<bool> isEnabled)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _activity = host.Aggregator;
            _isEnabled = isEnabled ?? throw new ArgumentNullException(nameof(isEnabled));
            _host.SetFrameUsableProvider(IsOwnerFrameUsable);
        }

        public void SetOwnerHandle(IntPtr hwnd)
        {
            _ownerHwnd = hwnd;
            _host.SetOwnerHandle(hwnd);
        }

        public bool ShowBranding => _host.ShowBranding;

        public void SetShowBranding(bool show) => _host.SetShowBranding(show);

        public void SetIdleSeconds(int seconds) => _host.SetIdleSeconds(seconds);

        /// <summary>Session footer, for example "AutoCAD 2024". Applies to the next card.</summary>
        public void SetInstanceInfo(string info) => _host.SetInstanceIdentity(info);

        public void SetHostDispatcher(System.Windows.Threading.Dispatcher dispatcher) =>
            _host.SetHostDispatcher(dispatcher);

        public void OnCompleted(
            string toolName,
            string resultJson,
            bool success,
            string errorMessage,
            string toolDescription)
        {
            if (!_isEnabled())
                return;

            var vm = ToastContentBuilder.BuildCompleted(
                toolName,
                resultJson,
                success,
                errorMessage,
                toolDescription);

            var imagePath = ToastContentBuilder.IsSafeImagePath(vm.ThumbnailPath) ? vm.ThumbnailPath : null;
            Record(vm.Title, vm.Body, vm.Success, vm.Success && imagePath != null, imagePath);
        }

        /// <summary>One-shot connection confirmation. Does not cover an open activity card.</summary>
        public void OnClientConnected(string connectionInfo)
        {
            if (!_isEnabled())
                return;

            Func<ActivityStatusText> localize = () => new ActivityStatusText(
                L.T("toast.connected.title"),
                string.IsNullOrWhiteSpace(connectionInfo)
                    ? L.T("toast.connected.summary")
                    : L.T("toast.connected.summary") + " · " + connectionInfo);
            var initial = localize();
            ShowStatus(initial.Title, initial.Body, 6, statusTextProvider: localize);
        }

        /// <summary>
        /// Shared command/Settings transition. Turning off clears activity first and
        /// then shows exactly one status card.
        /// </summary>
        public void OnToastEnabledChanged(bool enabled, bool persisted = true)
        {
            var resetRequestedRender = _activity.Reset();
            Func<ActivityStatusText> localize = enabled
                ? (Func<ActivityStatusText>)(() => new ActivityStatusText(
                    StatusText("toast.status.enabled", "Toast notifications enabled"),
                    StatusSummary("toast.status.enabled.summary", "New activity will appear here.", persisted)))
                : () => new ActivityStatusText(
                    StatusText("toast.status.disabled", "Toast notifications disabled"),
                    StatusSummary("toast.status.disabled.summary", "New activity is hidden until toast notifications are enabled.", persisted));
            var initial = localize();
            ShowStatus(initial.Title, initial.Body, 3, allowWhenDisabled: true, statusTextProvider: localize);
            if (resetRequestedRender)
                _host.Post(manager => manager.Render());
        }

        public void FlushPendingIfUsable()
        {
            if (_activity.FlushIfUsable(IsOwnerFrameUsable()))
                _host.Post(manager => manager.Render());
        }

        public void DismissAll()
        {
            _host.DismissAll(synchronous: false);
        }

        public void Shutdown()
        {
            _activity.Reset();
            _host.Shutdown();
        }

        private void Record(string title, string body, bool success, bool hasImage, string imagePath)
        {
            if (_activity.RecordResult(title, body, success, hasImage, IsOwnerFrameUsable(), imagePath))
                _host.Post(manager => manager.Render());
        }

        private void ShowStatus(string title, string body, int seconds, bool allowWhenDisabled = false,
            Func<ActivityStatusText> statusTextProvider = null)
        {
            if (!allowWhenDisabled && !_isEnabled())
                return;
            if (_activity.ShowStatus(title, body, seconds, statusTextProvider))
                _host.Post(manager => manager.Render());
        }

        private static string StatusText(string key, string fallback)
        {
            var value = L.T(key);
            return string.IsNullOrWhiteSpace(value) || string.Equals(value, key, StringComparison.Ordinal)
                ? fallback
                : value;
        }

        private static string StatusSummary(string key, string fallback, bool persisted)
        {
            var summary = StatusText(key, fallback);
            if (persisted)
                return summary;
            var warning = StatusText(
                "toast.status.saveFailed",
                "Preference could not be saved; this session is still using the new state.");
            return summary + " · " + warning;
        }

        private bool IsOwnerFrameUsable()
        {
            var hwnd = _ownerHwnd;
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
                return true;
            return IsWindowVisible(hwnd) && !IsIconic(hwnd) && IsWindowEnabled(hwnd);
        }

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowEnabled(IntPtr hWnd);
    }
}
