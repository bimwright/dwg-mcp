using System;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.Views;
using Bimwright.Dwg.Plugin.Views.Toast;

internal static class ToastHistoryTests
{
    internal static void Run()
    {
        CheckNotifierWhileToastsAreOff();
        CheckFailureCounterAndNoActivate();
        CheckHistory();
        CheckClickFailure();
    }

    private static void CheckNotifierWhileToastsAreOff()
    {
        var previous = PluginSettings.FilePathOverride;
        var sandbox = Path.Combine(Path.GetTempPath(), "dwg-toast-history-" + Guid.NewGuid().ToString("N"));
        PluginSettings.FilePathOverride = Path.Combine(sandbox, "settings.json");
        var host = new McpToastHost();
        try
        {
            var notifier = new McpToastNotifier(host, () => false);
            notifier.OnCompleted("send_code", "{\"ok\":false,\"error\":\"compile error\"}", true, null, null, 21);
            var entry = host.History.Snapshot().Single();
            if (entry.Success || entry.DurationMs != 21 || !entry.Summary.Contains("compile error")
                || Field<McpToastManager>(host, "_manager") != null || Directory.Exists(sandbox))
                throw new Exception("Toast off must retain compact History without creating a window or persistent files.");
            notifier.DismissAll();
            if (host.History.Snapshot().Length != 1)
                throw new Exception("Dismissing toasts must not clear History.");
        }
        finally { host.Shutdown(); PluginSettings.FilePathOverride = previous; }
        Console.WriteLine("PASS: real notifier records History while toast is off, without windows or new files");
    }

    private static void CheckFailureCounterAndNoActivate()
    {
        var activity = new ActivityAggregator();
        var manager = new McpToastManager(Dispatcher.CurrentDispatcher, activity, motionEnabled: () => false);
        try
        {
            var outcome = ToastContentBuilder.BuildCompleted("send_code", "{\"ok\":false,\"error\":\"compile error\"}", true, null, null);
            activity.RecordResult(outcome.Title, outcome.Body, outcome.Success, false, true);
            manager.Render();
            var window = Field<McpToastWindow>(manager, "_window");
            if (Field<RollingToastNumber>(window, "_failedCount").Value != 1
                || Field<RollingToastNumber>(window, "_successCount").Value != 0
                || window.ShowActivated || window.ShowInTaskbar || !window.Topmost)
                throw new Exception("Script failure must show Failed=1, Success=0 on a non-activating tool window.");
            var style = GetWindowLongPtr(new WindowInteropHelper(window).Handle, -20).ToInt64();
            if ((style & 0x08000000L) == 0 || (style & 0x00000080L) == 0)
                throw new Exception("The real toast HWND must carry WS_EX_NOACTIVATE and WS_EX_TOOLWINDOW.");
        }
        finally { manager.DismissAllImmediate(); manager.Dispose(); }
        Console.WriteLine("PASS: script errors reach Failed and the real HWND does not activate");
    }

    private static void CheckHistory()
    {
        var history = new ActivityHistory();
        history.Record(ToastContentBuilder.BuildCompleted("list_layers", "{\"count\":2}", true, null, null, 10));
        history.Record(ToastContentBuilder.BuildCompleted("send_code", "{\"ok\":false,\"error\":\"compile error\"}", true, null, null, 20));
        var window = new HistoryWindow(history, "dwg-mcp 2024");
        try
        {
            window.Show();
            var grid = Field<DataGrid>(window, "_grid");
            var search = Field<TextBox>(window, "_search");
            var status = Field<ComboBox>(window, "_status");
            var kind = Field<ComboBox>(window, "_kind");
            if (!window.Title.Contains("dwg-mcp 2024") || grid.Columns.Count != 6 || grid.Items.Count != 2)
                throw new Exception("History must show the instance identity and RVT-style outcome table.");
            grid.SelectedIndex = 0;
            if (!Field<TextBlock>(window, "_summary").Text.Contains("compile error")
                || Field<Border>(window, "_detail").Visibility != Visibility.Visible)
                throw new Exception("Selecting a failed call must show its compact outcome.");
            search.Text = "compile";
            if (grid.Items.Count != 1) throw new Exception("History search did not filter summaries.");
            search.Text = "";
            status.SelectedIndex = 1;
            if (grid.Items.Count != 1 || !((ActivityHistoryEntry)grid.Items[0]).Success)
                throw new Exception("History success filter is incorrect.");
            status.SelectedIndex = 0;
            kind.SelectedIndex = 2;
            if (grid.Items.Count != 1 || ((ActivityHistoryEntry)grid.Items[0]).Kind != ToolActivityKind.Write)
                throw new Exception("History write filter is incorrect.");
            kind.SelectedIndex = 0;
            history.Record(ToastContentBuilder.BuildCompleted("create_line", null, true, null, null));
            typeof(HistoryWindow).GetMethod("RefreshEntries", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(window, new object[] { false });
            if (grid.Items.Count != 3) throw new Exception("History did not pick up a new completion.");
            var toolbar = (WrapPanel)((Grid)window.Content).Children[0];
            toolbar.Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (grid.Items.Count != 0 || history.Snapshot().Length != 0
                || Field<Border>(window, "_detail").Visibility != Visibility.Collapsed)
                throw new Exception("New session must clear the table and its stale detail.");
        }
        finally { window.Close(); }
        if (Field<DispatcherTimer>(window, "_refresh").IsEnabled)
            throw new Exception("Closed History retained its refresh timer.");
        Console.WriteLine("PASS: History table, live refresh, details, search, status/kind filters and cleanup");
    }

    private static void CheckClickFailure()
    {
        var activity = new ActivityAggregator();
        var manager = new McpToastManager(Dispatcher.CurrentDispatcher, activity,
            motionEnabled: () => false, onClick: _ => throw new InvalidOperationException("History cannot open"));
        try
        {
            activity.RecordResult("List Layers", "Done", true, false, true);
            manager.Render();
            var window = Field<McpToastWindow>(manager, "_window");
            typeof(McpToastManager).GetMethod("OnCardClicked", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(manager, new object[] { window.CardId });
            if (Field<McpToastWindow>(manager, "_window") != null)
                throw new Exception("A failed History open must still dismiss the toast.");
        }
        finally { manager.DismissAllImmediate(); manager.Dispose(); }
        Console.WriteLine("PASS: a failed History callback still dismisses the card");
    }

    private static T Field<T>(object owner, string name) => (T)owner.GetType()
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(owner);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
}
