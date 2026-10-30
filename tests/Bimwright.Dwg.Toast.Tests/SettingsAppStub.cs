using Bimwright.Dwg.Plugin.Views.Toast;

namespace Bimwright.Dwg.Plugin
{
    // SettingsWindow uses this host facade; the WPF harness never starts AutoCAD.
    internal static class App
    {
        public static McpToastNotifier ToastNotifier { get; set; }
        public static bool ToastEnabled { get; private set; } = true;
        public static bool ListenerRunning => false;
        public static bool ListenerUsesPipe => false;
        public static int? ListenerPort => null;
        public static bool ClientRecentlyPresent => false;
        public static bool AutoStartTransport => PluginSettings.LoadAutoStartTransport(out _);
        public static string AutoStartSource { get { PluginSettings.LoadAutoStartTransport(out var source); return source; } }
        public static string AutoStartSaveError => null;
        public static void SetToastEnabled(bool enabled) => ToastEnabled = enabled;
        public static void StartListener() { }
        public static int HistoryOpenCount { get; private set; }
        public static void ShowOrFocusHistory() => HistoryOpenCount++;
        public static void StopListener(bool closeSettings = true) { }
    }
}
