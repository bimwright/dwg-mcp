using System;
using System.IO;
using System.Linq;
using Bimwright.Dwg.Server;
using Bimwright.Dwg.Server.Tools;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests;

public sealed class ChangeHistoryRecordsTests : IDisposable
{
    private static readonly string Key = new('a', 64);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "dwg-records-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void RecordRejectsInvalidGroupsAtomicallyAndReusesSuccessfulRequest()
    {
        var store = new ChangeHistoryCapture(_directory);
        var call = Seed(store, "AB");
        var request = Guid.NewGuid().ToString("N");
        Assert.Throws<ArgumentException>(() => store.Record(Key, new[] { call, Guid.NewGuid().ToString("N") }, request, "Sửa tuyến", null));
        var change = store.Record(Key, new[] { call }, request, "Sửa tuyến", null);
        Assert.Equal(change, store.Record(Key, new[] { call }, request, "Sửa tuyến", null));
        Assert.Throws<ArgumentException>(() => store.Record(Key, new[] { call }, request, "Different goal", null));
        Assert.Throws<ArgumentException>(() => store.Record(Key, new[] { call }, "next", "Same call", null));
    }

    [Fact]
    public void QueryKeepsItsUpperBoundAcrossWritesAndReadsCurrentReasons()
    {
        var store = new ChangeHistoryCapture(_directory);
        var first = Seed(store, "AB");
        var second = Seed(store, "AB");
        var page = store.Query(Key, "00ab", null, null, 1, null);
        Assert.Equal(second, page["calls"][0].Value<string>("call_id"));
        var cursor = page.Value<string>("next_cursor");
        Assert.NotNull(cursor);
        Seed(store, "AB");
        store.Record(Key, new[] { first }, "annotate", "Sửa tuyến", "Xác nhận từ người dùng");
        var next = store.Query(Key, "AB", null, null, 1, cursor);
        Assert.Equal(first, next["calls"][0].Value<string>("call_id"));
        Assert.Equal("Xác nhận từ người dùng", next["calls"][0].Value<string>("reason"));
        Assert.Null(next.Value<string>("next_cursor"));
        Assert.Throws<InvalidOperationException>(() => store.Query(Key, "AC", null, null, 1, cursor));
    }

    [Fact]
    public void MissingHistoryIsReadOnlyAndDateFiltersAreHalfOpen()
    {
        var store = new ChangeHistoryCapture(_directory);
        Assert.Equal("no_history", store.Query(Key, null, null, null, 20, null).Value<string>("status"));
        Assert.False(Directory.Exists(_directory));
        var call = Seed(store, "AB");
        var recorded = store.Query(Key, null, null, null, 20, null)["calls"][0].Value<string>("recorded_utc");
        Assert.Equal(call, store.Query(Key, null, recorded, null, 20, null)["calls"][0].Value<string>("call_id"));
        Assert.Equal("no_matches", store.Query(Key, null, null, recorded, 20, null).Value<string>("status"));
    }

    [Fact]
    public void SchemaOneHistoryReadsWithoutMigrationAndAnnotationUpgradesIt()
    {
        var store = new ChangeHistoryCapture(_directory);
        var call = Seed(store, "AB");
        using var db = new SqliteConnection($"Data Source={Path.Combine(_directory, Key + ".db")};Pooling=False");
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = "DROP TABLE change_record_calls; DROP TABLE change_records; PRAGMA user_version=1";
        command.ExecuteNonQuery();
        Assert.Equal("unassigned", store.Query(Key, null, null, null, 20, null)["calls"][0].Value<string>("reason_status"));
        command.CommandText = "PRAGMA user_version";
        Assert.Equal(1L, command.ExecuteScalar());
        var id = store.Record(Key, new[] { call }, "upgrade", "Adjust layout", null);
        Assert.Equal(2L, command.ExecuteScalar());
        var row = store.Query(Key, null, null, null, 20, null)["calls"][0];
        Assert.Equal(id, row.Value<string>("change_id"));
        Assert.Equal("unknown", row.Value<string>("reason_status"));
    }

    [Fact]
    public void FittedPageRetainsAContinuationFromTheLastDeliveredCall()
    {
        var store = new ChangeHistoryCapture(_directory);
        var older = Seed(store, "AB");
        var newer = Seed(store, "AB");
        var reason = string.Concat(Enumerable.Repeat("Việt Nam ", 145));
        store.Record(Key, new[] { older }, "older", "Adjust layout", reason);
        store.Record(Key, new[] { newer }, "newer", "Adjust layout", reason);
        var page = store.Query(Key, null, null, null, 20, null);
        var exact = ChangeHistoryResponse.Encode(new McpResponse { Ok = true, Result = page }, 4096);
        var result = (JObject)JObject.Parse(exact)["result"];
        Assert.Null(result["_checkpoints"]);
        Assert.Equal(newer, Assert.Single((JArray)result["calls"]).Value<string>("call_id"));
        Assert.Equal(older, store.Query(Key, null, null, null, 20, result.Value<string>("next_cursor"))["calls"][0].Value<string>("call_id"));
    }

    [Fact]
    public void ReadOnlySurfaceExcludesRecordButIncludesBothReads()
    {
        var tools = ReadOnlyPolicy.ExposedMethods(typeof(ChangeHistoryTools), readOnly: true).Select(method => method.Name).ToArray();
        Assert.Equal(new[] { nameof(ChangeHistoryTools.GetChangeRecords), nameof(ChangeHistoryTools.SurveyChangeImpact) }, tools.OrderBy(name => name));
    }

    private static string Seed(ChangeHistoryCapture store, string handle)
    {
        var call = Guid.NewGuid().ToString("N");
        var response = new McpResponse
        {
            Ok = true, Target = new JObject { ["document_id"] = "doc", ["pid"] = 100 },
            ChangeCapture = new JObject
            {
                ["version"] = 1, ["status"] = "captured", ["drawing_key"] = Key, ["coverage"] = "full", ["observed"] = 1,
                ["deltas"] = new JArray(new JObject { ["handle"] = handle, ["operation"] = "create", ["after"] = new JObject { ["type"] = "Line" } })
            }
        };
        store.Complete("create_line", call, true, response);
        Assert.Equal("stored", response.History.Value<string>("status"));
        return call;
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
