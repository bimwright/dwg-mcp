using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Bimwright.Dwg.Plugin.Views.Toast;

internal static class Program
{
    private static int _passed;
    [STAThread]
    private static int Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        int exit = 0;
        app.Startup += async (_, __) =>
        {
            try
            {
                await StackAndShutdown();
                await ImagesAndClicks();
                await TimersAndHover();
                Console.WriteLine($"PASS {_passed} WPF behavioral scenarios ({Environment.Version})");
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); exit = 1; }
            finally { app.Shutdown(); }
        };
        app.Run();
        return exit;
    }

    private static McpToastViewModel Vm(string title, bool success = true, ToolActivityKind kind = ToolActivityKind.Read)
        => new McpToastViewModel { Title = title, Summary = "Tiếng Việt · Đường ống", Success = success, Kind = kind };
    private static McpToastWindow[] Windows() => Application.Current.Windows.OfType<McpToastWindow>().ToArray();
    private static void Check(bool pass, string message) { if (!pass) throw new Exception(message); }
    private static void Pass(string name) { _passed++; Console.WriteLine("PASS " + name); }
    private static void Click(UIElement target) => target.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
    private static void Hover(UIElement target, bool enter) => target.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = enter ? UIElement.MouseEnterEvent : UIElement.MouseLeaveEvent });
    private static IEnumerable<T> Visuals<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Visuals<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    private static async Task StackAndShutdown()
    {
        var owner = new Window { Title = "DWG toast test owner", Left = 500, Top = 300, Width = 450, Height = 300, ShowActivated = false };
        owner.Show();
        var handle = new WindowInteropHelper(owner).Handle;
        var manager = new McpToastManager(Application.Current.Dispatcher);
        manager.SetOwnerHandle(handle);
        for (int i = 0; i < 20; i++) manager.Complete(Vm(i.ToString()));
        await Task.Delay(350);
        var windows = Windows().OrderBy(w => w.Top).ToArray();
        Check(windows.Length == 4, "burst cap must be four");
        Check(windows[0].ViewModel.Title == "19" && windows[3].ViewModel.Title == "16", "newest first; evict oldest");
        foreach (var w in windows)
        {
            Check(!double.IsNaN(w.Top) && !double.IsNaN(w.Left), "finite coordinates");
            Check(!w.ShowActivated && !w.ShowInTaskbar && new WindowInteropHelper(w).Owner == handle, "non-activating owned window");
            Check(Visuals<TextBlock>(w).Any(t => t.Text.Contains("Tiếng Việt")), "Vietnamese content rendered");
        }
        Pass("burst, order, NaN, owner and window configuration");
        var priorLeft = windows[0].Left;
        owner.Left += 100;
        manager.RefreshOwnerPosition();
        Check(windows[0].Left > priorLeft + 50, "stack follows owner movement");
        Pass("owner movement");
        Click(windows[0]); // Start a real close animation, then force shutdown immediately.
        manager.DismissAllImmediate();
        Check(Windows().Length == 0, "OFF/shutdown must close even an already fading card immediately");
        manager.Complete(Vm("restart"));
        await Task.Delay(300);
        Check(Windows().Length == 1, "close callback must not remove new card");
        manager.DismissAllImmediate();
        owner.Close();
        Pass("immediate shutdown during close and restart");
    }

    private static async Task ImagesAndClicks()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dwg-toast-wpf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var paths = new List<string>();
        try
        {
            var pixels = new byte[600 * 300 * 4];
            for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = 160; pixels[i + 1] = 100; pixels[i + 2] = 40; pixels[i + 3] = 255; }
            var bitmap = BitmapSource.Create(600, 300, 96, 96, PixelFormats.Bgra32, null, pixels, 600 * 4);
            foreach (var extension in new[] { "png", "jpg", "jpeg", "bmp" })
            {
                var path = Path.Combine(directory, "capture." + extension);
                paths.Add(path);
                BitmapEncoder encoder = extension == "png" ? (BitmapEncoder)new PngBitmapEncoder() : extension == "bmp" ? new BmpBitmapEncoder() : (BitmapEncoder)new JpegBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var file = File.Create(path)) encoder.Save(file);
                var vm = Vm(extension);
                vm.CommandName = "capture_view_image";
                vm.ThumbnailPath = path;
                var opened = new List<string>();
                int closed = 0;
                var window = new McpToastWindow(vm, _ => closed++, opened.Add);
                window.Show();
                window.PlayEnterAnimation();
                await Task.Delay(30);
                var source = Visuals<Image>(window).Single().Source as BitmapSource;
                Check(source != null && source.PixelWidth == 300, extension + " thumbnail bounded decode");
                var artifacts = Environment.GetEnvironmentVariable("DWG_TOAST_TEST_ARTIFACTS");
                if (extension == "png" && !string.IsNullOrWhiteSpace(artifacts))
                {
                    await Task.Delay(350);
                    Directory.CreateDirectory(artifacts);
                    var render = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                    render.Render(window);
                    var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(render));
                    using (var output = File.Create(Path.Combine(artifacts, "dwg-toast.png"))) png.Save(output);
                }
                using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                Click(window);
                Click(window);
                await Task.Delay(300);
                Check(opened.Count == 1 && opened[0] == path && closed == 1, "capture click opens/closes once");
            }
            Pass("PNG/JPG/JPEG/BMP thumbnails, file locks and click opening");
            foreach (var command in new[] { "count_entities", "capture_view_image" })
            {
                var vm = Vm("no open", command != "capture_view_image");
                vm.CommandName = command; vm.ThumbnailPath = paths[1];
                int opened = 0;
                var window = new McpToastWindow(vm, null, _ => opened++);
                window.Show(); Click(window); window.CloseImmediate();
                Check(opened == 0, "generic/error card must not open even a valid image");
            }
            var closeVm = Vm("close button"); closeVm.CommandName = "capture_view_image"; closeVm.ThumbnailPath = paths[1];
            int closeOpens = 0, closeEvents = 0;
            var closeCard = new McpToastWindow(closeVm, _ => closeEvents++, _ => closeOpens++);
            closeCard.Show();
            Click(Visuals<Border>(closeCard).Single(b => b.Width == 22));
            await Task.Delay(300);
            Check(closeOpens == 0 && closeEvents == 1 && !closeCard.IsVisible, "close button dismisses without opening");
            Pass("generic/error cards and close button never open images");
            var corrupt = Path.Combine(directory, "corrupt.png");
            paths.Add(corrupt);
            File.WriteAllText(corrupt, "not an image");
            foreach (var path in new[] { corrupt, Path.Combine(directory, "missing.png") })
            {
                var vm = Vm("fallback"); vm.ThumbnailPath = path;
                var window = new McpToastWindow(vm, null, _ => throw new Exception("generic card opened image"));
                window.Show();
                Check(Visuals<Image>(window).Single().Source == null, "corrupt/missing image degrades gracefully");
                Click(window);
                window.CloseImmediate();
            }
            var deleted = paths[0];
            var capture = Vm("deleted"); capture.CommandName = "capture_view_image"; capture.ThumbnailPath = deleted;
            int opens = 0;
            var card = new McpToastWindow(capture, null, _ => opens++);
            card.Show();
            File.Delete(deleted);
            Click(card);
            card.CloseImmediate();
            Check(opens == 0, "click revalidates deleted path");
            Pass("decode fallback and deleted image revalidation");
        }
        finally
        {
            foreach (var window in Windows()) window.CloseImmediate();
            foreach (var path in paths) File.Delete(path);
            Directory.Delete(directory);
        }
    }

    private static async Task TimersAndHover()
    {
        var query = new McpToastWindow(Vm("query"), null);
        var write = new McpToastWindow(Vm("write", kind: ToolActivityKind.Write), null);
        var failure = new McpToastWindow(Vm("failure", false), null);
        var thumbVm = Vm("thumbnail"); thumbVm.ThumbnailPath = "timing fixture only";
        var thumbnail = new McpToastWindow(thumbVm, null);
        foreach (var w in new[] { query, write, failure, thumbnail }) w.Show();
        await Task.Delay(3500);
        Check(!query.IsVisible && write.IsVisible && failure.IsVisible && thumbnail.IsVisible, "query 3 seconds");
        await Task.Delay(3000);
        Check(!write.IsVisible && failure.IsVisible && thumbnail.IsVisible, "write 6 seconds");
        await Task.Delay(2000);
        Check(!failure.IsVisible && thumbnail.IsVisible, "failure 8 seconds");
        await Task.Delay(1000);
        Check(!thumbnail.IsVisible, "thumbnail 9 seconds");
        Pass("real 3/6/8/9 second timers");
        int closed = 0;
        var hover = new McpToastWindow(Vm("hover"), _ => closed++);
        hover.Show();
        Hover(hover, true);
        await Task.Delay(3500);
        Check(hover.IsVisible, "hover pauses expiry");
        Hover(hover, false);
        await Task.Delay(3500);
        Check(!hover.IsVisible && closed == 1, "leave restarts full interval and closes once");
        Pass("hover pause and restart");
    }
}
