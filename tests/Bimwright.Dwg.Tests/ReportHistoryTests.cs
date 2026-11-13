using System;
using System.IO;
using System.Linq;
using Bimwright.Dwg.Plugin.Reports.Model;
using Bimwright.Dwg.Plugin.Reports.Storage;
using Bimwright.Dwg.Server;
using Bimwright.Dwg.Server.Tools;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests;

public sealed class ReportHistoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "dwg-report-history-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void ReportResendKeepsReferencesUnlessExplicitlyReplacedOrCleared()
    {
        var store = new ReportStore(_directory);
        var session = Guid.NewGuid().ToString("N");
        var first = Report(session, "First", new ReportChanges { Calls = { Reference(session, "disabled") } });
        var original = store.Save(first);
        var keep = Report(session, "Keep");
        Assert.Equal(original.ReportId, store.Save(keep).ReportId);
        Assert.Equal(first.Changes.Calls[0].CallId, Assert.Single(store.LoadNewest()).Changes.Calls[0].CallId);
        var replace = Report(session, "Replace", new ReportChanges { Calls = { Reference(session, "unsupported") } });
        store.Save(replace);
        Assert.Equal(replace.Changes.Calls[0].CallId, Assert.Single(store.LoadNewest()).Changes.Calls[0].CallId);
        store.Save(Report(session, "Clear", new ReportChanges()));
        Assert.Empty(Assert.Single(store.LoadNewest()).Changes.Calls);
    }

    [Fact]
    public void ReportReceiptsAreBoundToTheOriginatingProcessAndExpireTogether()
    {
        var receipts = new ReportCallReceipts(2);
        var session = Guid.NewGuid().ToString("N");
        var start = DateTime.UtcNow;
        var a = Reference(session, "disabled");
        var b = Reference(session, "unsupported");
        receipts.Add(a, 100, start);
        receipts.Add(b, 100, start);
        Assert.Equal(new[] { b.CallId, a.CallId }, receipts.Resolve(new[] { b.CallId, a.CallId }, session, 100, start).Calls.Select(c => c.CallId));
        Assert.Throws<ArgumentException>(() => receipts.Resolve(new[] { a.CallId }, session, 100, start.AddTicks(1)));
        Assert.Throws<ArgumentException>(() => receipts.Resolve(new[] { a.CallId }, session, 101, start));
        receipts.Add(Reference(session, "disabled"), 100, start);
        Assert.Throws<ArgumentException>(() => receipts.Resolve(new[] { b.CallId, a.CallId }, session, 100, start));
    }

    [Fact]
    public void OfflineHistoryUsesExplicitOrderAndInvalidatesCursorAfterReplacement()
    {
        var store = new ReportStore(_directory);
        var history = new ReportHistoryLookup(store, new ChangeHistoryCapture(Path.Combine(_directory, "db")));
        var session = Guid.NewGuid().ToString("N");
        var a = Reference(session, "disabled");
        var b = Reference(session, "stored", new string('a', 64));
        var report = store.Save(Report(session, "First", new ReportChanges { Calls = { b, a } }));
        var page = history.Read(report.ReportId, null, null, 1);
        Assert.Equal(b.CallId, page["calls"][0].Value<string>("call_id"));
        Assert.Equal("history_unavailable", page["calls"][0].Value<string>("status"));
        var cursor = page.Value<string>("next_cursor");
        Assert.Equal("disabled", history.Read(report.ReportId, cursor, null, 1)["calls"][0].Value<string>("status"));
        store.Save(Report(session, "Second"));
        Assert.Throws<InvalidOperationException>(() => history.Read(report.ReportId, cursor, null, 1));
        store.Delete(report.ReportId);
        Assert.Equal("report_not_found", history.Read(report.ReportId, null, null, 10).Value<string>("status"));
    }

    [Fact]
    public void LookupJoinsExactDrawingAndSessionAndPaginatesCommittedDeltas()
    {
        var store = new ReportStore(_directory);
        var capture = new ChangeHistoryCapture(Path.Combine(_directory, "db"));
        var lookup = new ReportHistoryLookup(store, capture);
        var key = new string('a', 64);
        var call = Guid.NewGuid().ToString("N");
        var response = new McpResponse
        {
            Ok = true, Target = new JObject { ["document_id"] = "doc" },
            ChangeCapture = new JObject
            {
                ["version"] = 1, ["status"] = "captured", ["drawing_key"] = key, ["coverage"] = "full", ["observed"] = 2,
                ["deltas"] = new JArray(new[] { "A", "B" }.Select(handle => new JObject { ["handle"] = handle, ["operation"] = "create" }))
            }
        };
        capture.Complete("create_line", call, true, response);
        var change = capture.Record(key, new[] { call }, "annotate", "Move path", "Người dùng xác nhận");
        var session = ReportTools.GatewaySession;
        var id = store.Save(Report(session, "Exact", new ReportChanges { Calls = { Reference(session, "stored", key, call) } })).ReportId;
        var wrongSession = Guid.NewGuid().ToString("N");
        var other = store.Save(Report(wrongSession, "Wrong session", new ReportChanges { Calls = { Reference(wrongSession, "stored", key, call) } })).ReportId;
        var legacy = store.Save(Report(Guid.NewGuid().ToString("N"), "Legacy")).ReportId;
        Assert.Equal("no_provenance", lookup.Read(legacy, null, null, 10).Value<string>("status"));
        Assert.Equal("history_unavailable", lookup.Read(other, null, null, 10)["calls"][0].Value<string>("status"));
        Assert.Equal(change, lookup.Read(id, null, null, 10)["calls"][0].Value<string>("change_id"));
        var detail = lookup.Read(id, null, call, 1);
        Assert.Equal("A", detail["deltas"][0].Value<string>("handle"));
        Assert.Equal("B", lookup.Read(id, detail.Value<string>("next_cursor"), call, 1)["deltas"][0].Value<string>("handle"));
        Assert.Throws<InvalidOperationException>(() => lookup.Read(id, detail.Value<string>("next_cursor"), null, 1));
    }

    private static AgentReport Report(string session, string summary, ReportChanges changes = null) => new()
    {
        TaskId = "job", Summary = summary, Outcome = "completed", Source = new ReportSource { ClientSession = session }, Changes = changes
    };

    private static ReportCallReference Reference(string session, string status, string key = null, string call = null) => new()
    {
        CallId = call ?? Guid.NewGuid().ToString("N"), ClientSession = session, Tool = "create_line", CaptureStatus = status, DrawingKey = key
    };

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
