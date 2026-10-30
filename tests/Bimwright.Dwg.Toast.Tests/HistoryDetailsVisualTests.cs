using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.Views;
using Bimwright.Dwg.Plugin.Views.Settings;
using Bimwright.Dwg.Plugin.Views.Toast;
using Newtonsoft.Json.Linq;

internal static class HistoryDetailsVisualTests
{
    internal static void CheckNativeClipboardCopy()
    {
        // Materialize every existing format before any test write. Backup stays in RAM.
        var previous = new DataObject();
        var current = Clipboard.GetDataObject();
        if (current != null)
            foreach (var format in current.GetFormats(false))
            {
                var data = current.GetData(format, false);
                if (data is MemoryStream stream) data = new MemoryStream(stream.ToArray());
                if (data != null) previous.SetData(format, data);
            }
        var history = new ActivityHistory();
        history.Record(ToastContentBuilder.BuildCompleted("list_layers", "{\"count\":2,\"detail\":\"Nguyễn-秘密\\nsecond line\"}", true, null, "Read layers", 17),
            new JObject { ["note"] = "Response: {\"password\":\"hidden-marker\"}" }, "{\"count\":2,\"detail\":\"Nguyễn-秘密\\nsecond line\"}");
        var entry = history.Snapshot()[0];
        var window = new HistoryWindow(history);
        uint lastWrite = 0;
        try
        {
            window.Show(); Field<DataGrid>(window, "_grid").SelectedIndex = 0; Pump();
            var expected = new[] { entry.What, entry.Input, entry.Output };
            var buttons = new[] { "_copyWhat", "_copyInput", "_copyOutput" };
            for (var i = 0; i < buttons.Length; i++)
            {
                Field<Button>(window, buttons[i]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(Field<TextBlock>(window, "_copyNote").Text == "Copied", "Native Copy did not succeed.");
                lastWrite = GetClipboardSequenceNumber();
                var actual = Clipboard.GetText();
                Check(actual == expected[i] && !actual.Contains("hidden-marker"), "Native clipboard differs from sanitized detail.");
                Console.WriteLine("PASS: native Windows clipboard " + buttons[i] + " exact sanitized text");
            }
        }
        finally
        {
            window.Close();
            // Preserve a newer clipboard change made by the user or another app.
            if (lastWrite != 0 && GetClipboardSequenceNumber() == lastWrite)
            {
                Clipboard.SetDataObject(previous, true);
                Console.WriteLine("PASS: pre-test clipboard formats restored from memory");
            }
        }
    }
    internal static void Run(string screenshotPath)
    {
        CheckDetailsAndClipboard(screenshotPath);
        CheckNativeClipboardDenial();
        CheckRefreshAndClear();
        CheckCloseAndReopen();
        CheckSettingsNavigation(screenshotPath);
    }

    private static void CheckDetailsAndClipboard(string screenshotPath)
    {
        var history = new ActivityHistory();
        var input = new JObject { ["layer"] = "WALLS", ["label"] = "Nguyễn / 秘密\nsecond line", ["password"] = "hidden-marker",
            ["detail"] = "Response body: {\"password\":\"hidden-marker with spaces\"}" };
        var output = new JObject { ["count"] = 2, ["note"] = new string('x', 600), ["path"] = @"C:\Users\Private\drawing.dwg",
            ["detail"] = "Response body: {\"access_token\":\"hidden-marker\"}" };
        history.Record(ToastContentBuilder.BuildCompleted("query_entities", output.ToString(), true, null, "Read matching entities", 27), input, output.ToString(), description: "Read matching entities");
        var entry = history.Snapshot()[0];
        string copied = null;
        var failCopy = false;
        var window = new HistoryWindow(history, "dwg-mcp 2024", value =>
        {
            if (failCopy) throw new COMException("Fixture clipboard unavailable");
            copied = value;
        });
        try
        {
            window.Show(); Pump();
            Field<DataGrid>(window, "_grid").SelectedIndex = 0; Pump();
            var tabs = Find<TabControl>(Field<Border>(window, "_detail"));
            var values = new[] { entry.What, entry.Input, entry.Output };
            var fields = new[] { "_what", "_input", "_output" };
            var buttons = new[] { "_copyWhat", "_copyInput", "_copyOutput" };
            for (var i = 0; i < 3; i++)
            {
                tabs.SelectedIndex = i; Pump();
                var text = Field<TextBox>(window, fields[i]);
                Check(text.IsReadOnly && text.Text == values[i] && text.ActualHeight > 50, "Detail must be readable and display the exact sanitized value.");
                Check(!text.Text.Contains("hidden-marker") && !text.Text.Contains("Private"), "Detail leaked a secret/path.");
                var copy = Field<Button>(window, buttons[i]);
                Check(copy.IsEnabled, "Non-empty detail cannot be copied.");
                copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(copied == values[i], "Copy differs from stored detail (wrapping/truncation).");
                Check(Field<TextBlock>(window, "_copyNote").Text == "Copied", "Copy success is not visible.");
            }
            Console.WriteLine("PASS: real WPF What/Input/Output copy buttons send exact sanitized multiline/Unicode details to the clipboard adapter");

            failCopy = true;
            Field<Button>(window, "_copyOutput").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(Field<TextBlock>(window, "_copyNote").Text.StartsWith("Copy failed"), "Clipboard failure was hidden.");
            failCopy = false;
            Field<Button>(window, "_copyOutput").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(copied == entry.Output && Field<TextBlock>(window, "_copyNote").Text == "Copied", "Clipboard adapter recovery failed.");
            Console.WriteLine("PASS: clipboard adapter failure is surfaced and a later Copy succeeds");
            if (screenshotPath != null)
            {
                Pump(); SaveImage(window, screenshotPath);
                window.Width = window.MinWidth; window.Height = window.MinHeight; Pump();
                Check(Field<TextBox>(window, "_output").ActualHeight > 40, "Details collapsed at the minimum window size.");
                SaveImage(window, Path.Combine(Path.GetDirectoryName(screenshotPath), "history-minimum.png"));
            }
        }
        finally { window.Close(); Pump(); }

        var emptyHistory = new ActivityHistory();
        emptyHistory.Record(ToastContentBuilder.BuildCompleted("list_layers", null, true, null, null));
        var empty = new HistoryWindow(emptyHistory);
        try
        {
            empty.Show(); Field<DataGrid>(empty, "_grid").SelectedIndex = 0; Pump();
            Check(!Field<Button>(empty, "_copyInput").IsEnabled && !Field<Button>(empty, "_copyOutput").IsEnabled, "Empty detail Copy should be disabled.");
        }
        finally { empty.Close(); }
        Console.WriteLine("PASS: absent input/output remain empty with Copy disabled");
    }

    private static void CheckNativeClipboardDenial()
    {
        // Never replace the user's clipboard when access is available. On a
        // restricted desktop, exercise the actual default WPF clipboard failure.
        if (OpenClipboard(IntPtr.Zero)) { CloseClipboard(); Console.WriteLine("SKIP: native clipboard denial is not present"); return; }
        var error = Marshal.GetLastWin32Error();
        Check(error == 5, "Unexpected clipboard probe failure: " + error);
        var history = new ActivityHistory(); history.Record(new McpToastViewModel { Title = "Clipboard failure fixture" });
        var window = new HistoryWindow(history);
        try
        {
            window.Show(); Field<DataGrid>(window, "_grid").SelectedIndex = 0; Pump();
            Field<Button>(window, "_copyWhat").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(Field<TextBlock>(window, "_copyNote").Text.StartsWith("Copy failed"), "Actual clipboard denial reported success.");
        }
        finally { window.Close(); }
        Console.WriteLine("PASS: actual WPF Clipboard access denial is shown honestly; native clipboard success remains unverified");
    }

    private static void CheckRefreshAndClear()
    {
        var history = new ActivityHistory();
        for (var i = 0; i < 100; i++) history.Record(new McpToastViewModel { Title = "Tool " + i, Summary = "Outcome " + i, Success = i % 2 == 0 });
        var window = new HistoryWindow(history);
        try
        {
            window.Show(); Pump();
            var grid = Field<DataGrid>(window, "_grid");
            grid.SelectedIndex = 30; Pump();
            var selected = grid.SelectedItem;
            var source = grid.ItemsSource;
            var scroll = Find<ScrollViewer>(grid);
            scroll.ScrollToVerticalOffset(25); Pump();
            var anchor = grid.Items[(int)scroll.VerticalOffset];
            history.Record(new McpToastViewModel { Title = "New", Success = true });
            Refresh(window, false); Pump();
            Check(ReferenceEquals(source, grid.ItemsSource) && ReferenceEquals(selected, grid.SelectedItem), "Refresh rebound the view/lost selection.");
            Check(ReferenceEquals(anchor, grid.Items[(int)scroll.VerticalOffset]), "Refresh moved the scrolled row anchor.");
            ToolbarButton(window, "Refresh").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            ToolbarButton(window, "Refresh").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Check(grid.Items.Count == 101 && ReferenceEquals(selected, grid.SelectedItem), "Repeated refresh duplicated rows or lost selection.");
            Console.WriteLine("PASS: new completion and repeated Refresh retain view identity, selected entry and scrolled row without duplicates");

            Field<ComboBox>(window, "_status").SelectedIndex = ((ActivityHistoryEntry)selected).Success ? 2 : 1;
            Pump();
            Check(grid.SelectedItem == null && Field<Border>(window, "_detail").Visibility == Visibility.Collapsed, "Filtering left a stale selected detail.");
            Field<ComboBox>(window, "_status").SelectedIndex = 0;
            grid.SelectedIndex = 0;
            ToolbarButton(window, "New session").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Check(grid.Items.Count == 0 && history.Snapshot().Length == 0 && grid.SelectedItem == null, "Clear did not reset the session.");
            foreach (var field in new[] { "_what", "_input", "_output" }) Check(Field<TextBox>(window, field).Text.Length == 0, "Clear retained stale detail.");
            foreach (var field in new[] { "_copyWhat", "_copyInput", "_copyOutput" }) Check(!Field<Button>(window, field).IsEnabled, "Clear left Copy enabled.");
            history.Record(new McpToastViewModel { Title = "Fresh", Success = true }); Refresh(window, false); Pump();
            Check(grid.Items.Count == 1 && history.Snapshot()[0].Index == 1 && grid.SelectedItem == null, "New session numbering/selection is wrong.");
        }
        finally { window.Close(); }
        Console.WriteLine("PASS: filters clear hidden selection; New session clears details/buttons and starts positive numbering without auto-selection");
    }

    private static void CheckCloseAndReopen()
    {
        var history = new ActivityHistory();
        history.Record(new McpToastViewModel { Title = "Retained", Success = true });
        var references = Enumerable.Range(0, 3).Select(_ => OpenAndClose(history)).ToArray();
        Pump(); GC.Collect(); GC.WaitForPendingFinalizers(); Pump(); GC.Collect();
        Check(references.All(r => !r.IsAlive), "Closed History windows remain rooted.");
        var reopened = new HistoryWindow(history);
        try
        {
            history.Record(new McpToastViewModel { Title = "While closed", Success = true });
            reopened.Show(); Pump();
            Check(Field<DataGrid>(reopened, "_grid").Items.Count == 2 && Field<DispatcherTimer>(reopened, "_refresh").IsEnabled, "Reopened History lost session entries or failed to start refresh.");
        }
        finally { reopened.Close(); }
        Console.WriteLine("PASS: close stops timers/releases view references; three closed windows collect; reopen retains the session and restarts refresh");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference OpenAndClose(ActivityHistory history)
    {
        var window = new HistoryWindow(history); window.Show(); Pump(); window.Close(); Pump();
        Check(!Field<DispatcherTimer>(window, "_refresh").IsEnabled && Field<DataGrid>(window, "_grid").ItemsSource == null, "Closed window retained its timer/view.");
        return new WeakReference(window);
    }

    private static void CheckSettingsNavigation(string screenshotPath)
    {
        var root = Path.Combine(Path.GetTempPath(), "dwg-history-settings-" + Guid.NewGuid().ToString("N"));
        var previous = PluginSettings.FilePathOverride;
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "settings.json"); const string original = "{\"enableToast\":false,\"keep\":true}";
        File.WriteAllText(path, original); PluginSettings.FilePathOverride = path;
        try
        {
            for (var i = 0; i < 2; i++)
            {
                var window = new SettingsWindow();
                try
                {
                    window.Show(); Pump();
                    var count = App.HistoryOpenCount;
                    var button = Descendants<Button>(window).Single(b => (b.Content as string) == "Open session History");
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(App.HistoryOpenCount == count + 1, "Settings did not open the existing History route.");
                    Check(Descendants<TextBlock>(window).Any(t => t.Text.Contains("logging remains opt-in")), "Settings hides the memory/logging distinction.");
                    Check(File.ReadAllText(path) == original, "Opening History changed user settings.");
                    if (i == 0 && screenshotPath != null) SaveImage(window, Path.Combine(Path.GetDirectoryName(screenshotPath), "settings-history.png"));
                }
                finally { window.Close(); }
                Check(!Field<DispatcherTimer>(window, "_refresh").IsEnabled, "Closed Settings retained its timer.");
            }
        }
        finally { PluginSettings.FilePathOverride = previous; File.Delete(path); Directory.Delete(root); }
        Console.WriteLine("PASS: Settings History navigation/close/reopen leaves settings unchanged and logging opt-in");
    }

    private static void SaveImage(Window window, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(path)) encoder.Save(stream);
    }
    private static Button ToolbarButton(HistoryWindow window, string text) => ((WrapPanel)((Grid)window.Content).Children[0]).Children.OfType<Button>().Single(b => (string)b.Content == text);
    private static void Refresh(HistoryWindow window, bool force) => typeof(HistoryWindow).GetMethod("RefreshEntries", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(window, new object[] { force });
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(owner);
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static T Find<T>(DependencyObject root) where T : DependencyObject => Descendants<T>(root).First();
    private static System.Collections.Generic.IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item) yield return item;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var found in Descendants<T>(VisualTreeHelper.GetChild(root, i))) yield return found;
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
}
