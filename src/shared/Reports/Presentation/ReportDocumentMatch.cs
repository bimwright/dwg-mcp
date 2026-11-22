using System;
using Bimwright.Dwg.Plugin.Reports.Model;

namespace Bimwright.Dwg.Plugin.Reports.Presentation
{
    public sealed class ActiveDocumentIdentity
    {
        /// <summary>The plugin's per-open document_id.</summary>
        public string Token { get; set; }
        /// <summary>Full DWG path; null for an unsaved drawing.</summary>
        public string Path { get; set; }
        public string Title { get; set; }
    }

    public static class ReportDocumentMatch
    {
        /// <summary>Same open drawing, or the same saved file reopened later (handles survive a reopen).</summary>
        public static bool Matches(ReportDocument doc, ActiveDocumentIdentity active)
        {
            if (doc == null || active == null) return false;
            return (doc.Token != null && string.Equals(doc.Token, active.Token, StringComparison.Ordinal))
                || (!string.IsNullOrEmpty(doc.Path) && string.Equals(doc.Path, active.Path, StringComparison.OrdinalIgnoreCase));
        }
    }
}
