using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.Localization;
using Bimwright.Dwg.Plugin.Reports.Model;
using Bimwright.Dwg.Plugin.Reports.Presentation;
using Bimwright.Dwg.Plugin.Reports.Views;
using Bimwright.Dwg.Plugin.Views;
using Bimwright.Dwg.Plugin.Views.Settings;
using Bimwright.Dwg.Plugin.Views.Toast;
using Bimwright.Dwg.Plugin.Views.Update;

internal static class LocalizationVisualTests
{
    internal static void Run()
    {
        var path = Path.GetTempFileName();
        var original = PluginSettings.FilePathOverride;
        PluginSettings.FilePathOverride = path;
        try
        {
            foreach (var locale in LocaleResolver.SupportedLocales)
            {
                L.Initialize(locale);
                var settings = new SettingsWindow();
                try
                {
                    Measure(settings);
                    var combo = LogicalChildren(settings).OfType<ComboBox>().Single(c => AutomationProperties.GetName(c) == L.T("settings.language.label"));
                    Check(combo.Items.Cast<ComboBoxItem>().Select(i => (string)i.Content).SequenceEqual(new[] { "English", "Tiếng Việt", "简体中文" }), "Language choices");
                    var next = locale == "vi" ? "zh-CN" : "vi";
                    combo.SelectedValue = next;
                    Check(PluginSettings.LoadUiLanguage() == next && L.Locale == locale, "Locale must change only at startup");
                    Check(LogicalChildren(settings).OfType<TextBlock>().Any(t => t.Text == L.T("settings.language.restart") && t.TextWrapping == TextWrapping.Wrap), "Restart notice");
                    File.WriteAllText(path, "{bad");
                    combo.SelectedValue = "en";
                    Check((string)combo.SelectedValue == next && File.ReadAllText(path) == "{bad", "Failed write rolls selection back");
                    Check(LogicalChildren(settings).OfType<TextBlock>().Any(t => t.Text == L.T("settings.language.saveFailed")), "Save failure notice");
                }
                finally { settings.Close(); File.WriteAllText(path, "{}"); }

                var toast = new McpToastWindow(new ActivitySnapshot(1, false, 2, 1, 1, "dwg_create_line", "fixture", true, true), null, null, null, null, null);
                try { Measure(toast); Check(LogicalChildren(toast).OfType<TextBlock>().Any(t => t.Text == L.T("toast.activity.success")), "Toast counters"); }
                finally { toast.CloseImmediate(); }
                var history = new HistoryWindow(new ActivityHistory(), "dwg-mcp 2025");
                try { Measure(history); Check(history.Title.EndsWith(L.T("history.title", ("identity", "dwg-mcp 2025"))), "History title"); }
                finally { history.Close(); }
                foreach (var mode in new[] { ReportsWindowMode.Wide, ReportsWindowMode.Compact })
                {
                    using var vm = new ReportsViewModel(new EmptyReports(), new NoNavigation(), mode == ReportsWindowMode.Wide ? DisplayMode.Wide : DisplayMode.Compact);
                    using var reports = new ReportsWindow(vm, new ReportsWindowState(mode), null);
                    try { Measure(reports); Check(reports.Title.Contains(L.T("reports.window.title")), "Reports title " + mode); }
                    finally { reports.CloseForShutdown(); }
                }
                var update = new ReleaseNoticeWindow("2.1.2", StableRelease.FromTag("v9.9.0"));
                try
                {
                    Measure(update);
                    Check(update.Title == L.T("releaseNotice.title") && LogicalChildren(update).OfType<TextBlock>().Any(t => t.Text.Contains("9.9.0")), "Release notice");
                }
                finally { update.Close(); }
                Console.WriteLine("PASS: WPF locale " + locale + " (Settings, toast, History, Reports wide/compact and release notice)");
            }
        }
        finally { L.Initialize("en"); PluginSettings.FilePathOverride = original; File.Delete(path); }
    }

    private static IEnumerable<DependencyObject> LogicalChildren(DependencyObject parent)
    {
        yield return parent;
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
            foreach (var item in LogicalChildren(child)) yield return item;
    }

    private static void Measure(Window window)
    {
        if (window.Content is UIElement root)
        {
            var size = new Size(700, 500);
            root.Measure(size);
            root.Arrange(new Rect(size));
            root.UpdateLayout();
        }
    }

    private static void Check(bool ok, string what)
    {
        if (!ok) throw new InvalidOperationException(L.Locale + ": " + what);
    }

    private sealed class EmptyReports : IReportsRuntime
    {
        public IReadOnlyList<AgentReport> LoadReports() => Array.Empty<AgentReport>();
        public void DeleteReport(string id) { }
        public event EventHandler ReportsChanged { add { } remove { } }
        public ActiveDocumentIdentity ActiveDocument => null;
        public event EventHandler ActiveDocumentChanged { add { } remove { } }
        public void Post(Action action) => action();
        public bool CopyText(string text) => true;
        public DateTime Now => DateTime.Now;
        public TimeZoneInfo LocalTimeZone => TimeZoneInfo.Local;
        public ReportHostStatus HostStatus { get; } = new ReportHostStatus(1234, false, null);
        public event EventHandler HostStatusChanged { add { } remove { } }
    }

    private sealed class NoNavigation : INavigationService
    {
        public void Navigate(ReportNavigationRequest request) { }
        public event EventHandler<ReportNavigationRequest> Started { add { } remove { } }
        public event EventHandler<ReportNavigationResult> Completed { add { } remove { } }
    }
}
