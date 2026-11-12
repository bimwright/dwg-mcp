using System;
using System.Globalization;
using System.Linq;
using Bimwright.Dwg.Plugin.Localization;
using Bimwright.Dwg.Plugin.Reports.Model;

namespace Bimwright.Dwg.Plugin.Reports.Presentation
{
    internal static class ReportPresentationText
    {
        public static string Outcome(string outcome)
        {
            switch (outcome)
            {
                case "completed": return L.T("reports.outcome.completed");
                case "failed": return L.T("reports.outcome.failed");
                case "cancelled": return L.T("reports.outcome.cancelled");
                default: return outcome ?? "";
            }
        }
        public static string Time(string createdUtc, DateTime now)
        {
            if (!DateTimeOffset.TryParse(createdUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value)) return "";
            var local = value.LocalDateTime;
            var time = local.ToString("HH:mm", CultureInfo.InvariantCulture);
            if (local.Date == now.Date) return L.T("reports.time.today", ("time", time));
            if (local.Date == now.Date.AddDays(-1)) return L.T("reports.time.yesterday", ("time", time));
            return local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }
        public static bool Matches(AgentReport report, ActiveDocumentIdentity active) =>
            report.Documents != null && report.Documents.Any(d => ReportDocumentMatch.Matches(d, active));
        public static string DrawingName(AgentReport report) => report.Documents?.FirstOrDefault()?.Title ?? "";
    }
}
