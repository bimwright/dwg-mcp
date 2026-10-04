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
        public static void SetToastEnabled(bool enabled) => ToastEnabled = enabled;
        public static void StartListener() { }
        public static void StopListener(bool closeSettings = true) { }
    }
}
