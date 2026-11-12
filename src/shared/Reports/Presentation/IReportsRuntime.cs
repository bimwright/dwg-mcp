using System;
using System.Collections.Generic;
using Bimwright.Dwg.Plugin.Reports.Model;

namespace Bimwright.Dwg.Plugin.Reports.Presentation
{
    public interface IReportsRuntime
    {
        IReadOnlyList<AgentReport> LoadReports();
        void DeleteReport(string reportId);
        event EventHandler ReportsChanged;
        ActiveDocumentIdentity ActiveDocument { get; }
        event EventHandler ActiveDocumentChanged;
        void Post(Action action);
        bool CopyText(string text);
        DateTime Now { get; }
        TimeZoneInfo LocalTimeZone { get; }
        ReportHostStatus HostStatus { get; }
        event EventHandler HostStatusChanged;
    }
}
