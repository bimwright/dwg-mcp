using System;
using System.IO;
using Bimwright.Dwg.Server;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests;

public sealed class ChangeHistoryCaptureTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dwg-history-test-" + Guid.NewGuid().ToString("N"));
    private static readonly string Key = new('a', 64);

    [Fact]
    public void BatchAbortDoesNotStoreDeltaAndCommitStoresExactlyOneCallAcrossRetry()
    {
        var history = new ChangeHistoryCapture(_root);
        var aborted = Reply("no_committed_changes", new JArray());
        history.Complete("batch_execute", Guid.NewGuid().ToString("N"), true, aborted);
        Assert.Equal("no_committed_changes", aborted.History.Value<string>("status"));
        Assert.False(Directory.Exists(_root));

        var callId = Guid.NewGuid().ToString("N");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var committed = Reply("captured", new JArray(new JObject
            {
                ["handle"] = "AB", ["step_index"] = 0, ["operation"] = "create",
                ["after"] = new JObject { ["type"] = "Line" }
            }));
            history.Complete("batch_execute", callId, true, committed);
            Assert.Equal("stored", committed.History.Value<string>("status"));
            Assert.Null(committed.ChangeCapture);
        }

        using var db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(_root, Key + ".db"), Mode = SqliteOpenMode.ReadOnly, Pooling = false
        }.ToString());
        db.Open();
        using var query = db.CreateCommand();
        query.CommandText = "SELECT call_id, handle, step_index FROM entity_deltas";
        using var rows = query.ExecuteReader();
        Assert.True(rows.Read());
        Assert.Equal(callId, rows.GetString(0));
        Assert.Equal("AB", rows.GetString(1));
        Assert.Equal(0, rows.GetInt32(2));
        Assert.False(rows.Read());
    }

    [Fact]
    public void DisabledCaptureAndStorageFailurePreserveCadOutcome()
    {
        var history = new ChangeHistoryCapture(_root);
        var reply = Reply("captured", new JArray());
        history.Complete("create_line", Guid.NewGuid().ToString("N"), false, reply);
        Assert.Equal("disabled", reply.History.Value<string>("status"));
        Assert.True(reply.Ok);
        Assert.False(Directory.Exists(_root));

        Directory.CreateDirectory(_root);
        var file = Path.Combine(_root, "file");
        File.WriteAllText(file, "test");
        var blocked = new ChangeHistoryCapture(file);
        var committed = Reply("captured", new JArray(new JObject { ["handle"] = "AB", ["operation"] = "create" }));
        blocked.Complete("create_line", Guid.NewGuid().ToString("N"), true, committed);
        Assert.Equal("storage_failed", committed.History.Value<string>("status"));
        Assert.True(committed.Ok);
    }

    private static McpResponse Reply(string status, JArray deltas) => new()
    {
        Ok = true,
        Target = new JObject { ["document_id"] = "doc", ["pid"] = 100 },
        ChangeCapture = new JObject
        {
            ["version"] = 1, ["status"] = status, ["drawing_key"] = Key, ["coverage"] = "full",
            ["observed"] = deltas.Count, ["deltas"] = deltas
        }
    };

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
