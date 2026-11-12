using System;
using System.Collections.Generic;
using Bimwright.Dwg.Plugin.Reports.Model;

namespace Bimwright.Dwg.Plugin.Reports.Presentation
{
    public interface INavigationService
    {
        void Navigate(ReportNavigationRequest request);
        event EventHandler<ReportNavigationRequest> Started;
        event EventHandler<ReportNavigationResult> Completed;
    }

    public sealed class ReportNavigationRequest : EventArgs
    {
        internal long TraceId { get; set; }
        internal long QueuedTimestamp { get; set; }
        public ReportDocument Document { get; set; }
        public IReadOnlyList<ReportEntity> Entities { get; set; }
    }

    public enum ReportNavigationOutcome
    {
        Selected, EntitiesGone, NoExtents, NotMatched, Failed
    }

    public sealed class ReportNavigationResult : EventArgs
    {
        public ReportNavigationOutcome Outcome { get; set; }
        public int SelectedCount { get; set; }
        public int MissingCount { get; set; }
        /// <summary>The layout the entities were shown in (Model or a paper-space layout name).</summary>
        public string LayoutName { get; set; }
    }
}
