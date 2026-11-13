using System;
using System.Collections.Generic;
using System.Linq;
using Bimwright.Dwg.Plugin.Reports.Model;

namespace Bimwright.Dwg.Plugin.Reports.Presentation
{
    public sealed class ReportDetailViewModel
    {
        private readonly Func<DateTime> _now;
        public ReportDetailViewModel(AgentReport report, INavigationService navigation,
            Func<ReportDocument, bool> isDocumentOpen, Func<DateTime> now)
        {
            Report = report ?? throw new ArgumentNullException(nameof(report));
            _now = now ?? throw new ArgumentNullException(nameof(now));
            var links = report.Links ?? new List<ReportLink>();
            Body = MarkdownLiteParser.Parse(report.Body, ReportParseMode.Stored, links.Count);
            Links = links.Select(link => new ReportLinkViewModel(
                report.Documents?.FirstOrDefault(d => d != null && d.Ref == link.Doc), link, navigation, isDocumentOpen)).ToList();
        }
        public AgentReport Report { get; }
        public string Summary => Report.Summary;
        public string Outcome => Report.Outcome;
        public string OutcomeLabel => ReportPresentationText.Outcome(Outcome);
        public string TimeText => ReportPresentationText.Time(Report.CreatedUtc, _now());
        public string TaskId => Report.TaskId;
        public string DrawingName => ReportPresentationText.DrawingName(Report);
        public MarkdownParseResult Body { get; }
        public IReadOnlyList<ReportLinkViewModel> Links { get; }
        // Reading state belongs to the detail, not a disposable Compact/Wide visual tree.
        internal Dictionary<int, bool> ExpandedBodySections { get; } = new Dictionary<int, bool>();
        internal Dictionary<int, int> BodyTablePages { get; } = new Dictionary<int, int>();
        public void UpdateActiveDocument()
        {
            foreach (var link in Links) link.UpdateActiveDocument();
        }
    }
}
