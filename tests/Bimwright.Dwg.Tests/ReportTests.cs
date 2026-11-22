using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bimwright.Dwg.Plugin.Reports.Binding;
using Bimwright.Dwg.Plugin.Reports.Model;
using Bimwright.Dwg.Plugin.Reports.Storage;
using Bimwright.Dwg.Server.Tools;
using Bimwright.Dwg.Tests.Helpers;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests;

public sealed class ReportTests
{
    private const string Active = "0123456789abcdef0123456789abcdef";
    private const string Other = "fedcba9876543210fedcba9876543210";

    private sealed class FakeResolver : IReportResolver
    {
        public readonly Dictionary<string, HashSet<string>> Entities = new()
        {
            [Active] = new HashSet<string> { "2A", "2B", "1F4" },
            [Other] = new HashSet<string> { "9C" }
        };
        public bool HasActive = true;
        public ReportDocument ActiveDocument => HasActive ? Doc(Active, "plan.dwg") : null;
        public ReportDocument FindDocument(string token) => Entities.ContainsKey(token) ? Doc(token, token == Active ? "plan.dwg" : "site.dwg") : null;
        public ReportEntity FindEntity(string token, string handle)
            => Entities.TryGetValue(token, out var set) && set.Contains(handle) ? new ReportEntity { Handle = handle, Type = "Line", Layer = "0", Space = "*Model_Space" } : null;
        private static ReportDocument Doc(string token, string title) => new() { Token = token, Title = title, Path = @"C:\work\" + title };
    }

    [Theory]
    [InlineData("dwg:2A", new[] { "2A" }, null)]
    [InlineData("dwg:002a,2B,2a", new[] { "2A", "2B" }, null)]
    [InlineData("DWG:1f4?doc=0123456789ABCDEF0123456789ABCDEF", new[] { "1F4" }, Active)]
    public void Raw_links_normalize_handles_and_document(string target, string[] handles, string doc)
    {
        var parsed = LinkSyntax.Parse(target);
        Assert.True(parsed.IsValid);
        Assert.Equal(handles, parsed.Ids);
        Assert.Equal(doc, parsed.DocumentToken);
    }

    [Theory]
    [InlineData("revit:123")]
    [InlineData("dwg:")]
    [InlineData("dwg:0")]
    [InlineData("dwg:000")]
    [InlineData("dwg:2G")]
    [InlineData("dwg:2A,")]
    [InlineData("dwg:12345678901234567")]
    [InlineData("dwg:2A?view=5")]
    [InlineData("dwg:2A?doc=0123")]
    [InlineData("dwg:2A?doc=0123456789abcdef0123456789abcdef&x=1")]
    public void Bad_targets_are_rejected(string target) => Assert.Equal("bad_syntax", LinkSyntax.Parse(target).Reason);

    [Fact]
    public void Resolved_links_are_stored_by_index_and_unresolved_ones_are_reported()
    {
        var body = "Fixed [walls](dwg:2A,2B) and [title](dwg:9C?doc=" + Other + ").\n" +
                   "Missing [gone](dwg:FFF), partial [mixed](dwg:1F4,ABC), closed [x](dwg:2A?doc=" + new string('1', 32) + "), bad [y](dwg:zz), " +
                   "plain [web](https://example.com).";
        var result = ReportBuilder.Build("job-1", "completed", "Fixed walls", body, new FakeResolver(), new ReportSource());
        var report = result.Report;

        Assert.Equal(6, result.LinkCheck.Total);
        Assert.Equal(3, result.LinkCheck.Resolved);
        Assert.Equal(new[] { "entity_not_found", "entity_not_found", "document_not_open", "bad_syntax" },
            result.LinkCheck.Unresolved.Select(u => u.Reason));
        Assert.Equal(new[] { "FFF" }, result.LinkCheck.Unresolved[0].Handles);
        Assert.Equal(new[] { "ABC" }, result.LinkCheck.Unresolved[1].Handles);

        Assert.Equal(3, report.Links.Count);
        Assert.Equal(new[] { "2A", "2B" }, report.Links[0].Entities.Select(e => e.Handle));
        Assert.Equal("d0", report.Links[0].Doc);
        Assert.Equal("d1", report.Links[1].Doc);
        Assert.Equal(new[] { "1F4" }, report.Links[2].Entities.Select(e => e.Handle));
        Assert.Equal(new[] { Active, Other }, report.Documents.Select(d => d.Token));
        Assert.Contains("[walls](dwg:#0)", report.Body);
        Assert.Contains("[title](dwg:#1)", report.Body);
        Assert.Contains("[mixed](dwg:#2)", report.Body);
        Assert.Contains("[gone](dwg-FFF)", report.Body);
        Assert.Contains("[y](dwg-zz)", report.Body);
        Assert.Contains("[web](https://example.com)", report.Body);

        // Stored bodies parse back only to their own link table.
        var stored = MarkdownLiteParser.Parse(report.Body, ReportParseMode.Stored, report.Links.Count);
        Assert.Equal(new int?[] { 0, 1, 2 }, stored.Links.Select(l => l.Syntax.StoredIndex));
    }

    [Fact]
    public void Links_without_an_active_drawing_are_no_document()
    {
        var resolver = new FakeResolver { HasActive = false };
        var result = ReportBuilder.Build("job", "failed", "x", "[a](dwg:2A) [b](dwg:9C?doc=" + Other + ")", resolver, new ReportSource());
        Assert.Equal(new[] { "no_document" }, result.LinkCheck.Unresolved.Select(u => u.Reason));
        Assert.Equal(1, result.LinkCheck.Resolved);
    }

    [Fact]
    public void Summary_and_body_secrets_are_masked()
    {
        var result = ReportBuilder.Build("job", "completed", "used token=PLAINSECRET123",
            "Header was Bearer PLAINSECRET456 and api_key: PLAINSECRET789", new FakeResolver(), new ReportSource());
        Assert.DoesNotContain("PLAINSECRET", result.Report.Summary);
        Assert.DoesNotContain("PLAINSECRET", result.Report.Body);
    }

    [Fact]
    public void Resending_a_task_replaces_only_within_the_same_gateway_session()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dwg-reports-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ReportStore(dir);
            var a = Guid.NewGuid().ToString("N"); var b = Guid.NewGuid().ToString("N");
            var first = ReportBuilder.BuildAndSave("job", "completed", "first", null, new FakeResolver(), new ReportSource { ClientSession = a }, store);
            var second = ReportBuilder.BuildAndSave("job", "failed", "second", null, new FakeResolver(), new ReportSource { ClientSession = a }, store);
            var other = ReportBuilder.BuildAndSave("job", "completed", "other", null, new FakeResolver(), new ReportSource { ClientSession = b }, store);
            Assert.False(first.Saved.Replaced);
            Assert.True(second.Saved.Replaced);
            Assert.Equal(first.Saved.ReportId, second.Saved.ReportId);
            Assert.False(other.Saved.Replaced);
            Assert.NotEqual(first.Saved.ReportId, other.Saved.ReportId);
            var all = store.LoadNewest();
            Assert.Equal(2, all.Count);
            Assert.Contains(all, r => r.ReportId == first.Saved.ReportId && r.Summary == "second" && r.Outcome == "failed");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData("", "completed", "s")]
    [InlineData("job", "done", "s")]
    [InlineData("job", "completed", "two\nlines")]
    public async Task Gateway_refuses_invalid_reports_without_contacting_AutoCAD(string task, string outcome, string summary)
    {
        var response = JObject.Parse(await ReportTools.ReportTaskResult(task, outcome, summary));
        Assert.False(response.Value<bool>("ok"));
    }

    [Fact]
    public async Task Gateway_sends_its_session_and_full_text()
    {
        var sent = await Capture.Send(() => ReportTools.ReportTaskResult("job", "completed", "Done", "Body [a](dwg:2A)"));
        Assert.Equal("report_task_result", sent.Command);
        var wire = sent.Json();
        Assert.Equal("Body [a](dwg:2A)", wire.Value<string>("body"));
        Assert.True(Guid.TryParseExact(wire.Value<string>("client_session"), "N", out _));
    }
}
