using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Bimwright.Dwg.Plugin.Reports.Model;
using Bimwright.Dwg.Plugin.Reports.Storage;

namespace Bimwright.Dwg.Plugin.Reports.Binding
{
    public sealed class ReportBuildResult
    {
        public AgentReport Report { get; set; }
        public ReportLinkCheck LinkCheck { get; set; }
        public ReportSaveResult Saved { get; set; }
    }

    public sealed class ReportLinkCheck
    {
        [JsonProperty("total")]
        public int Total { get; set; }
        [JsonProperty("resolved")]
        public int Resolved { get; set; }
        [JsonProperty("unresolved")]
        public List<ReportUnresolvedLink> Unresolved { get; set; } = new List<ReportUnresolvedLink>();
    }

    public sealed class ReportUnresolvedLink
    {
        [JsonProperty("label")]
        public string Label { get; set; }
        [JsonProperty("target")]
        public string Target { get; set; }
        /// <summary>bad_syntax, limit_exceeded, no_document, document_not_open or entity_not_found.</summary>
        [JsonProperty("reason")]
        public string Reason { get; set; }
        [JsonProperty("handles", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> Handles { get; set; }
    }

    public static class ReportBuilder
    {
        public static ReportBuildResult BuildAndSave(string taskId, string outcome, string summary, string body,
            IReportResolver resolver, ReportSource source, ReportStore store, ReportChanges changes = null)
        {
            var result = Build(taskId, outcome, summary, body, resolver, source);
            result.Report.Changes = changes;
            result.Saved = store.Save(result.Report);
            return result;
        }

        public static ReportBuildResult Build(string taskId, string outcome, string summary, string body,
            IReportResolver resolver, ReportSource source)
        {
            var report = new AgentReport
            {
                TaskId = taskId,
                Outcome = outcome,
                Summary = SecretMasker.Mask(summary),
                Source = source
            };
            var check = new ReportLinkCheck();
            var references = new Dictionary<string, string>();
            var active = resolver?.ActiveDocument;
            AddDocument(report, references, active);

            if (body != null)
            {
                var parsed = MarkdownLiteParser.Parse(MaskBody(body), ReportParseMode.Raw);
                var indices = new Dictionary<int, int>();
                for (var i = 0; i < parsed.Links.Count; i++)
                {
                    var occurrence = parsed.Links[i];
                    check.Total++;
                    if (occurrence.Reason != null)
                    {
                        AddUnresolved(check, occurrence, occurrence.Reason);
                        continue;
                    }

                    var syntax = occurrence.Syntax;
                    var document = syntax.DocumentToken == null ? active : resolver?.FindDocument(syntax.DocumentToken);
                    if (document == null || document.Token == null)
                    {
                        AddUnresolved(check, occurrence, syntax.DocumentToken == null ? "no_document" : "document_not_open");
                        continue;
                    }

                    var link = new ReportLink { Label = occurrence.Label };
                    var missing = new List<string>();
                    foreach (var handle in syntax.Ids)
                    {
                        var entity = resolver?.FindEntity(document.Token, handle);
                        if (entity == null) missing.Add(handle);
                        else link.Entities.Add(entity);
                    }
                    if (link.Entities.Count == 0)
                    {
                        AddUnresolved(check, occurrence, "entity_not_found", missing);
                        continue;
                    }
                    if (missing.Count > 0) AddUnresolved(check, occurrence, "entity_not_found", missing);
                    link.Doc = AddDocument(report, references, document);
                    indices.Add(i, report.Links.Count);
                    report.Links.Add(link);
                    check.Resolved++;
                }
                report.Body = ReportBodyCanonicalizer.Canonicalize(parsed.Body, parsed.Links, indices);
            }
            return new ReportBuildResult { Report = report, LinkCheck = check };
        }

        public static object CreateResponse(ReportBuildResult result, ReportSaveResult save)
            => new
            {
                report_id = save.ReportId,
                replaced = save.Replaced,
                links = result.LinkCheck,
                history = new
                {
                    status = result.Report.Changes?.Calls.Count > 0 ? "references" : "no_provenance",
                    calls = result.Report.Changes?.Calls ?? new List<ReportCallReference>()
                }
            };

        // SecretMasker writes "[REDACTED]"; followed by "(" it would read as link markup the agent never wrote.
        private static string MaskBody(string body)
        {
            return SecretMasker.Mask(body).Replace("[REDACTED](", @"\[REDACTED\](");
        }

        private static void AddUnresolved(ReportLinkCheck check, MarkdownLinkOccurrence occurrence, string reason, List<string> handles = null)
        {
            check.Unresolved.Add(new ReportUnresolvedLink { Label = occurrence.Label, Target = occurrence.Target, Reason = reason, Handles = handles });
        }

        private static string AddDocument(AgentReport report, Dictionary<string, string> references, ReportDocument document)
        {
            if (document?.Token == null) return null;
            if (references.TryGetValue(document.Token, out var reference)) return reference;
            reference = "d" + report.Documents.Count.ToString(CultureInfo.InvariantCulture);
            references.Add(document.Token, reference);
            report.Documents.Add(new ReportDocument { Ref = reference, Token = document.Token, Title = document.Title, Path = document.Path });
            return reference;
        }
    }
}
