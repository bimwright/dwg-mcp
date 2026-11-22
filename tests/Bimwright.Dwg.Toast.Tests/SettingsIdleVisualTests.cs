using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.Views.Settings;
using Bimwright.Dwg.Plugin.Views.Toast;
using Newtonsoft.Json.Linq;

internal static class SettingsIdleVisualTests
{
    internal static void Run()
    {
        var previousPath = PluginSettings.FilePathOverride;
        var previousNotifier = App.ToastNotifier;
        var sandbox = Path.Combine(Path.GetTempPath(), "dwg-toast-idle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
        var path = Path.Combine(sandbox, "settings.json");
        File.WriteAllText(path, "{\"toastIdleSeconds\":20,\"unrelated\":true}");
        PluginSettings.FilePathOverride = path;
        var host = new McpToastHost();
        App.ToastNotifier = new McpToastNotifier(host, () => true);
        var window = new SettingsWindow();
        try
        {
            var idle = Field<ComboBox>(window, "_idle");
            var apply = Field<Button>(window, "_apply");
            var note = Field<TextBlock>(window, "_applyNote");
            foreach (ComboBoxItem item in idle.Items)
                if ((int)item.Tag == 30) idle.SelectedItem = item;
            if (!apply.IsEnabled) throw new Exception("Changing idle duration did not enable Apply.");

            // The real settings writer must reject malformed user data without overwriting it.
            File.WriteAllText(path, "[broken");
            apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (File.ReadAllText(path) != "[broken" || Field<int>(host, "_idleSeconds") != 20
                || Field<int>(window, "_savedIdle") != 20 || !apply.IsEnabled
                || (int)((ComboBoxItem)idle.SelectedItem).Tag != 30
                || !note.Text.Contains("previous duration is still active") || !note.Text.Contains("Try Apply again"))
                throw new Exception("Failed idle save must keep the active duration, retain the draft, show an error and allow retry.");

            File.WriteAllText(path, "{\"toastIdleSeconds\":20,\"unrelated\":true}");
            apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var saved = JObject.Parse(File.ReadAllText(path));
            if (saved.Value<int>("toastIdleSeconds") != 30 || !saved.Value<bool>("unrelated")
                || Field<int>(host, "_idleSeconds") != 30 || Field<int>(window, "_savedIdle") != 30
                || apply.IsEnabled || !string.IsNullOrEmpty(note.Text))
                throw new Exception("Retry must persist and apply the staged duration while preserving unrelated settings.");
        }
        finally
        {
            // Discard any remaining draft before closing so this harness cannot open a confirmation modal.
            typeof(SettingsWindow).GetMethod("RevertIdle", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, null);
            window.Close();
            host.Shutdown();
            App.ToastNotifier = previousNotifier;
            PluginSettings.FilePathOverride = previousPath;
            File.Delete(path);
            Directory.Delete(sandbox);
        }
        Console.WriteLine("PASS: real Settings idle save failure preserves active duration and supports retry");
        CheckPauseForTodaySetting();
    }

    // FC-TST-04
    private static void CheckPauseForTodaySetting()
    {
        var previousPath = PluginSettings.FilePathOverride;
        var previousNotifier = App.ToastNotifier;
        var previousEnabled = App.ToastEnabled;
        var sandbox = Path.Combine(Path.GetTempPath(), "dwg-toast-snooze-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
        var path = Path.Combine(sandbox, "settings.json");
        File.WriteAllText(path, "{\"toastIdleSeconds\":20,\"unrelated\":true}");
        PluginSettings.FilePathOverride = path;
        App.SetToastEnabled(true);
        var host = new McpToastHost();
        // Reuse this thread's dispatcher: a dedicated toast thread would create (and on Shutdown end) the AppDomain's
        // WPF Application, after which no later window in this process becomes visible.
        host.SetHostDispatcher(System.Windows.Threading.Dispatcher.CurrentDispatcher);
        App.ToastNotifier = new McpToastNotifier(host, () => App.ToastEnabled);
        var window = new SettingsWindow();
        try
        {
            var enabled = Field<CheckBox>(window, "_toastEnabled");
            var snoozed = Field<CheckBox>(window, "_toastSnoozed");
            var apply = Field<Button>(window, "_apply");
            if (!snoozed.IsEnabled || snoozed.IsChecked == true || ToastSnooze.IsActive())
                throw new Exception("Pause for today must start enabled and unchecked.");

            snoozed.IsChecked = true;
            var saved = JObject.Parse(File.ReadAllText(path));
            if (!ToastSnooze.IsActive() || string.IsNullOrWhiteSpace(saved.Value<string>("toastSnoozedDate"))
                || !saved.Value<bool>("unrelated") || apply.IsEnabled)
                throw new Exception("Pause for today must persist immediately without Apply while keeping other keys.");

            snoozed.IsChecked = false;
            if (ToastSnooze.IsActive() || JObject.Parse(File.ReadAllText(path))["toastSnoozedDate"] != null)
                throw new Exception("Unchecking Pause for today must remove toastSnoozedDate immediately.");

            if (!ToastSnooze.PauseForToday(out var error))
                throw new Exception("External pause failed: " + error);
            if (snoozed.IsChecked != true)
                throw new Exception("Open Settings window did not sync live when ToastSnooze.Changed fired.");

            enabled.IsChecked = false;
            if (App.ToastEnabled || snoozed.IsEnabled)
                throw new Exception("Turning toasts off must disable Pause for today.");

            enabled.IsChecked = true;
            if (!App.ToastEnabled || !snoozed.IsEnabled || snoozed.IsChecked == true || ToastSnooze.IsActive())
                throw new Exception("Turning toasts back on must clear an active pause and re-enable the switch.");
        }
        finally
        {
            typeof(SettingsWindow).GetMethod("RevertIdle", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, null);
            window.Close();
            host.Shutdown();
            App.SetToastEnabled(previousEnabled);
            App.ToastNotifier = previousNotifier;
            PluginSettings.FilePathOverride = previousPath;
            File.Delete(path);
            Directory.Delete(sandbox);
        }
        Console.WriteLine("PASS: Settings Pause for today saves immediately, tracks ToastSnooze.Changed, and clears when toasts turn on");
    }

    private static T Field<T>(object instance, string name) =>
        (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance);
}
