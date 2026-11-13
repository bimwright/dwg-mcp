using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Autodesk.AutoCAD.ApplicationServices;
using Bimwright.Dwg.Plugin.Reports.Model;
using Bimwright.Dwg.Plugin.Reports.Presentation;
using Bimwright.Dwg.Plugin.Reports.Storage;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace Bimwright.Dwg.Plugin.Reports.Acad
{
    /// <summary>
    /// The Reports view model's view of AutoCAD: the shared report store and its folder watcher, the active
    /// drawing (followed through DocumentManager events), the WPF dispatcher and the clipboard.
    /// </summary>
    internal sealed class AcadReportsRuntime : IReportsRuntime, IDisposable
    {
        private readonly Dispatcher _dispatcher;
        private readonly ReportFolderWatcher _watcher;
        private volatile ActiveDocumentIdentity _active;
        private static readonly int OwnerPid = CapturePid();
        private ReportHostStatus _host = CaptureHost();
        private static int CapturePid() { using (var process = Process.GetCurrentProcess()) return process.Id; }

        /// <summary>AutoCAD main thread.</summary>
        public AcadReportsRuntime(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _watcher = new ReportFolderWatcher(log: PluginLog.Debug);
            _watcher.Changed += OnFolderChanged;
            var documents = Application.DocumentManager;
            documents.DocumentCreated += OnDocumentCreated;
            documents.DocumentActivated += OnDocumentActivated;
            documents.DocumentDestroyed += OnDocumentDestroyed;
            RefreshActiveDocument();
        }

        public event EventHandler ReportsChanged;
        public event EventHandler ActiveDocumentChanged;
        public event EventHandler HostStatusChanged;
        public ReportHostStatus HostStatus => _host;
        private static ReportHostStatus CaptureHost() => new ReportHostStatus(OwnerPid, App.ListenerRunning, App.ListenerConnectionInfo);

        internal void RefreshHostStatus()
        {
            var next = CaptureHost();
            if (_host.Pid == next.Pid && _host.Running == next.Running && _host.Transport == next.Transport && _host.Endpoint == next.Endpoint) return;
            _host = next;
            HostStatusChanged?.Invoke(this, EventArgs.Empty);
        }

        public ActiveDocumentIdentity ActiveDocument => _active;
        public bool IsDocumentOpen(ReportDocument document) => AcadEntityNavigator.Find(document) != null;
        public DateTime Now => DateTime.Now;
        public TimeZoneInfo LocalTimeZone
        {
            get
            {
                // A visible Reports window must notice an OS time-zone change without restarting AutoCAD.
                TimeZoneInfo.ClearCachedData();
                return TimeZoneInfo.Local;
            }
        }

        public IReadOnlyList<AgentReport> LoadReports() => ReportServices.Store.LoadNewest();

        public void DeleteReport(string reportId) => ReportServices.Store.Delete(reportId);

        public void Post(Action action)
        {
            if (action == null || _dispatcher.HasShutdownStarted) return;
            _dispatcher.BeginInvoke(action);
        }

        public bool CopyText(string text)
        {
            try
            {
                Clipboard.SetText(text ?? "");
                return true;
            }
            catch (Exception ex) // Another program can hold the clipboard open.
            {
                PluginLog.Debug("Reports: copy to clipboard failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>Main thread: re-read the active drawing, including a path a save-as just gave it.</summary>
        internal void RefreshActiveDocument(bool openDocumentsChanged = false)
        {
            ActiveDocumentIdentity next = null;
            try { next = Identify(Application.DocumentManager.MdiActiveDocument); }
            catch (Exception ex) { PluginLog.Debug("Reports: cannot read the active drawing: " + ex.Message); }
            var current = _active;
            if (!openDocumentsChanged && current?.Token == next?.Token && string.Equals(current?.Path, next?.Path, StringComparison.OrdinalIgnoreCase)) return;
            _active = next;
            ActiveDocumentChanged?.Invoke(this, EventArgs.Empty);
        }

        internal static ActiveDocumentIdentity Identify(Document document)
        {
            var described = AcadReportResolver.Describe(document);
            return described == null ? null : new ActiveDocumentIdentity { Token = described.Token, Path = described.Path, Title = described.Title };
        }

        public void Dispose()
        {
            _watcher.Changed -= OnFolderChanged;
            _watcher.Dispose();
            try
            {
                var documents = Application.DocumentManager;
                documents.DocumentCreated -= OnDocumentCreated;
                documents.DocumentActivated -= OnDocumentActivated;
                documents.DocumentDestroyed -= OnDocumentDestroyed;
            }
            catch (Exception) { } // AutoCAD is shutting down.
        }

        private void OnDocumentCreated(object sender, DocumentCollectionEventArgs e) => RefreshActiveDocument(openDocumentsChanged: true);
        private void OnDocumentActivated(object sender, DocumentCollectionEventArgs e) => RefreshActiveDocument();
        private void OnDocumentDestroyed(object sender, DocumentDestroyedEventArgs e) => RefreshActiveDocument(openDocumentsChanged: true);
        private void OnFolderChanged(object sender, EventArgs e) => ReportsChanged?.Invoke(this, EventArgs.Empty);
    }
}
