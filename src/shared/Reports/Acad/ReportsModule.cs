using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Bimwright.Dwg.Plugin.Reports.Presentation;
using Bimwright.Dwg.Plugin.Reports.Storage;
using Bimwright.Dwg.Plugin.Reports.Views;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace Bimwright.Dwg.Plugin.Reports.Acad
{
    /// <summary>
    /// Owns the Reports window and what it needs inside AutoCAD: the runtime over the shared store, link
    /// navigation, the view model and the window. Created on first use. AutoCAD main thread only.
    /// </summary>
    internal static class ReportsModule
    {
        private static AcadReportsRuntime _runtime;
        private static AcadNavigationService _navigation;
        private static ReportsViewModel _viewModel;
        private static ReportsWindow _window;

        /// <summary>Add-in startup: remove reports older than the retention period, off the main thread.</summary>
        internal static void CleanupInBackground()
        {
            try
            {
                var retention = TimeSpan.FromDays(PluginSettings.LoadReportRetentionDays());
                Task.Run(() => ReportRetention.Cleanup(ReportStore.DefaultDirectory, retention, DateTime.UtcNow, PluginLog.Debug));
            }
            catch (Exception ex) { PluginLog.Debug("Reports: retention cleanup did not start: " + ex.Message); }
        }

        /// <summary>Create the window on first use, then show, restore and activate it.</summary>
        internal static void ShowOrFocus()
        {
            if (!EnsureRuntime()) return;
            _runtime.RefreshHostStatus();
            _runtime.RefreshActiveDocument();
            if (_window == null)
            {
                var state = PluginSettings.LoadReportsWindow();
                _viewModel = new ReportsViewModel(_runtime, _navigation,
                    state.Mode == ReportsWindowMode.Compact ? DisplayMode.Compact : DisplayMode.Wide);
                _window = new ReportsWindow(_viewModel, state, SaveState);
                var owner = Application.MainWindow?.Handle ?? IntPtr.Zero;
                if (owner != IntPtr.Zero) new WindowInteropHelper(_window).Owner = owner;
                _window.Closed += OnWindowClosed;
                _window.Show();
            }
            else if (!_window.IsVisible) _window.Show();
            if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
            _window.Activate();
        }

        /// <summary>Open the window on <paramref name="reportId"/>; the filter switches to all drawings when needed.</summary>
        internal static void ShowReport(string reportId)
        {
            ShowOrFocus();
            _viewModel?.ShowReport(reportId);
        }

        /// <summary>AutoCAD Idle: cheap checks that keep an open window's status and drawing filter current.</summary>
        internal static void OnIdle()
        {
            if (_runtime == null) return;
            _runtime.RefreshHostStatus();
            _runtime.RefreshActiveDocument();
        }

        internal static void Shutdown()
        {
            _window?.CloseForShutdown();
            ReleaseWindow();
            _navigation?.Dispose();
            _runtime?.Dispose();
            _navigation = null;
            _runtime = null;
        }

        private static bool EnsureRuntime()
        {
            if (_runtime != null) return true;
            try
            {
                var dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
                _runtime = new AcadReportsRuntime(dispatcher);
                _navigation = new AcadNavigationService();
                return true;
            }
            catch (Exception ex)
            {
                PluginLog.Debug("Reports: module failed to start: " + ex.Message);
                Shutdown();
                return false;
            }
        }

        private static void SaveState(ReportsWindowState state)
        {
            if (!PluginSettings.SaveReportsWindow(state, out var error))
                PluginLog.Debug("Reports: cannot save the window state: " + error);
        }

        private static void OnWindowClosed(object sender, EventArgs e) => ReleaseWindow();

        private static void ReleaseWindow()
        {
            if (_window != null) _window.Closed -= OnWindowClosed;
            _window = null;
            _viewModel?.Dispose();
            _viewModel = null;
        }
    }
}
