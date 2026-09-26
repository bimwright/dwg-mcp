using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Threading;
using Autodesk.Windows;
using AcadApplication = Autodesk.AutoCAD.ApplicationServices.Application;

namespace Bimwright.Dwg.Plugin.Views.Toast
{
    // All WPF/ribbon access is on AutoCAD's UI thread. Transport workers only enqueue DTOs.
    internal static class ToastRuntime
    {
        private const string TabId = "BIMWRIGHT_DWG_TAB";
        private const string ButtonId = "BIMWRIGHT_DWG_TOAST";
        private static readonly ToastNotificationQueue Queue = new ToastNotificationQueue();
        private static McpToastManager _manager;
        private static bool _initialized;

        public static void Initialize()
        {
            if (_initialized) return;
            Queue.Enabled = ToastPreferences.Load();
            AcadApplication.Idle += OnIdle;
            _initialized = true;
        }

        public static Action<McpToastViewModel> Start()
        {
            Initialize();
            return Queue.Start();
        }

        public static void Stop()
        {
            Queue.Stop();
            try { _manager?.DismissAllImmediate(); } catch (Exception ex) { Log(ex); }
        }

        public static void Shutdown()
        {
            Stop();
            if (_initialized) AcadApplication.Idle -= OnIdle;
            _initialized = false;
            _manager = null;
            try
            {
                var ribbon = ComponentManager.Ribbon;
                var tab = ribbon?.FindTab(TabId);
                if (tab != null) ribbon.Tabs.Remove(tab);
            }
            catch (Exception ex) { Log(ex); }
        }

        public static void Toggle()
        {
            Queue.Enabled = !Queue.Enabled;
            if (!Queue.Enabled) _manager?.DismissAllImmediate();
            var saved = ToastPreferences.TrySave(Queue.Enabled, out var error);
            AcadApplication.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
                "\n[Bimwright.Dwg] Toast " + (Queue.Enabled ? "ON" : "OFF") + (saved ? "." : ". " + error) + "\n");
            EnsureRibbon();
        }

        private static void OnIdle(object sender, EventArgs args)
        {
            try { EnsureRibbon(); } catch (Exception ex) { Log(ex); }
            try
            {
                var owner = AcadApplication.MainWindow.Handle;
                if (owner == IntPtr.Zero || IsIconic(owner) || !IsWindowEnabled(owner))
                {
                    _manager?.DismissAllImmediate();
                    return;
                }
                _manager?.RefreshOwnerPosition();
                var pending = Queue.Drain();
                if (pending.Length == 0) return;
                if (_manager == null)
                    _manager = new McpToastManager(Dispatcher.CurrentDispatcher,
                        window => AcadApplication.ShowModelessWindow(AcadApplication.MainWindow.Handle, window, false));
                _manager.SetOwnerHandle(owner);
                foreach (var vm in pending) _manager.Complete(vm);
            }
            catch (Exception ex) { Log(ex); }
        }

        private static void EnsureRibbon()
        {
            var ribbon = ComponentManager.Ribbon;
            if (ribbon == null) return; // RIBBONCLOSE/startup: the command remains available.
            var tab = ribbon.FindTab(TabId);
            if (tab == null)
            {
                tab = new RibbonTab { Id = TabId, Title = "bimwright" };
                var panel = new RibbonPanelSource { Title = "DWG MCP" };
                panel.Items.Add(new RibbonToggleButton
                {
                    Id = ButtonId, ShowText = true, Size = RibbonItemSize.Large,
                    ToolTip = "Show completion notifications for DWG MCP calls. Also available as MCPTOAST.",
                    CommandHandler = new ToggleCommand()
                });
                tab.Panels.Add(new RibbonPanel { Source = panel });
                ribbon.Tabs.Add(tab);
            }
            foreach (var panel in tab.Panels)
                foreach (var item in panel.Source.Items)
                    if (item is RibbonToggleButton button && button.Id == ButtonId)
                    {
                        button.IsChecked = Queue.Enabled;
                        button.Text = Queue.Enabled ? "Toast ON" : "Toast OFF";
                    }
        }

        private sealed class ToggleCommand : ICommand
        {
            public event EventHandler CanExecuteChanged { add { } remove { } }
            public bool CanExecute(object parameter) => true;
            public void Execute(object parameter) { try { Toggle(); } catch (Exception ex) { Log(ex); } }
        }

        private static void Log(Exception ex) => Trace.WriteLine("[DWG Toast] " + McpResponsePrivacy.SanitizeError(ex.Message));
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr hwnd);
    }
}
