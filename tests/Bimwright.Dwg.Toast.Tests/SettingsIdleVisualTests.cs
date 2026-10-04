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
    }

    private static T Field<T>(object instance, string name) =>
        (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance);
}
