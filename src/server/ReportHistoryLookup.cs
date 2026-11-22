using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.Reports.Model;
using Bimwright.Dwg.Plugin.Reports.Storage;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server;

internal sealed class ReportHistoryLookup(ReportStore reports, ChangeHistoryCapture history)
{
    internal static readonly ReportHistoryLookup Default = new(new ReportStore(), ChangeHistoryCapture.Default);

    internal JObject Read(string reportId, string cursor, string callId, int limit)
    {
        reportId = NormalizeId(reportId, "report_id");
        if (callId != null) callId = NormalizeId(callId, "call_id");
        if (limit is < 1 or > 25) throw new ArgumentException("limit must be between 1 and 25.");
        var result = new JObject { ["report_id"] = reportId, ["status"] = "report_not_found", ["next_cursor"] = null };
        var report = reports.ReadById(reportId, out var revision);
        if (report == null) return result;

        var scope = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"report-history\n{reportId}\n{callId}")));
        var after = cursor == null ? 0 : checked((int)EntityQueryCursor.Read(cursor, revision, scope));
        result["outcome"] = report.Outcome;
        var references = report.Changes?.Calls;
        result["total_calls"] = references?.Count ?? 0;
        result["status"] = "no_provenance";
        if (references == null || references.Count == 0) return result;
        if (!report.Changes.IsValid(report.Source?.ClientSession)) throw new IOException("Invalid report history references.");

        result["status"] = "references";
        if (callId == null)
        {
            var calls = new JArray(references.Skip(after).Take(limit).Select(reference => ReadCall(reference, false, 0, 0).Call));
            result["calls"] = calls;
            AddContinuation(result, calls.Count, after, references.Count, revision, scope);
            return result;
        }

        var selected = references.Find(reference => reference.CallId == callId)
            ?? throw new ArgumentException("REPORT_CALL_NOT_FOUND: call_id is not attached to this report.");
        var detail = ReadCall(selected, true, after, limit);
        result["call"] = detail.Call;
        result["deltas"] = detail.Deltas;
        AddContinuation(result, detail.Deltas.Count, after, detail.Call.Value<int?>("stored") ?? 0, revision, scope);
        return result;
    }

    private (JObject Call, JArray Deltas) ReadCall(ReportCallReference reference, bool detail, int after, int limit)
    {
        var call = JObject.FromObject(reference);
        call["status"] = reference.CaptureStatus;
        if (reference.CaptureStatus != "stored") return (call, new JArray());
        try
        {
            var evidence = history.ReadReportCall(reference, detail, after, limit);
            if (evidence.Call != null)
            {
                call.Merge(evidence.Call);
                return (call, evidence.Deltas);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException or JsonException or InvalidOperationException) { }
        call["status"] = "history_unavailable";
        return (call, new JArray());
    }

    private static void AddContinuation(JObject result, int count, int after, int total, string revision, string scope)
    {
        var checkpoints = new JArray(Enumerable.Range(after + 1, count)
            .Select(index => EntityQueryCursor.Issue(revision, scope, index.ToString("X", CultureInfo.InvariantCulture))));
        result["_checkpoints"] = checkpoints;
        if (count > 0 && after + count < total) result["next_cursor"] = checkpoints.Last.DeepClone();
    }

    private static string NormalizeId(string id, string field) => Guid.TryParseExact(id, "N", out var guid) ? guid.ToString("N")
        : throw new ArgumentException(field + " must be a 32-character hexadecimal ID.");
}

internal sealed partial class ChangeHistoryCapture
{
    internal (JObject Call, JArray Deltas) ReadReportCall(ReportCallReference reference, bool detail, int after, int limit)
    {
        ValidateKey(reference.DrawingKey);
        using var db = OpenExisting(reference.DrawingKey, SqliteOpenMode.ReadOnly);
        if (db == null) return (null, null);
        using var tx = db.BeginTransaction(deferred: true);
        var version = EnsureSchema(db, tx, reference.DrawingKey, writable: false);
        using var query = db.CreateCommand();
        query.Transaction = tx;
        var fields = version == 2 ? "r.change_id, r.goal, r.reason" : "NULL, NULL, NULL";
        var join = version == 2 ? "LEFT JOIN change_record_calls AS l USING (call_id) LEFT JOIN change_records AS r USING (change_id)" : "";
        query.CommandText = $"""
            SELECT c.coverage, c.coverage_reason, c.observed, {fields}, (SELECT count(*) FROM entity_deltas WHERE call_id = c.call_id)
            FROM calls AS c {join}
            WHERE c.call_id = $call AND c.client_session = $session
            """;
        Bind(query, "$call", reference.CallId);
        Bind(query, "$session", reference.ClientSession);
        JObject call;
        using (var row = query.ExecuteReader())
        {
            if (!row.Read() || row.GetInt64(6) == 0) return (null, null);
            var changeId = ReadText(row, 3);
            var reason = ReadText(row, 5);
            call = new JObject
            {
                ["coverage"] = row.GetString(0), ["coverage_reason"] = ReadText(row, 1), ["observed"] = row.GetInt64(2),
                ["change_id"] = changeId, ["goal"] = ReadText(row, 4), ["reason"] = reason, ["stored"] = row.GetInt64(6),
                ["reason_status"] = changeId == null ? "unassigned" : reason == null ? "unknown" : "recorded"
            };
        }
        var deltas = new JArray();
        if (!detail) return (call, deltas);

        query.Parameters.Clear();
        query.CommandText = """
            SELECT handle, step_index, operation, before_json, after_json FROM entity_deltas
            WHERE call_id = $call ORDER BY ordinal LIMIT $limit OFFSET $offset
            """;
        Bind(query, "$call", reference.CallId);
        Bind(query, "$limit", limit);
        Bind(query, "$offset", after);
        using var rows = query.ExecuteReader();
        while (rows.Read()) deltas.Add(new JObject
        {
            ["handle"] = rows.GetString(0), ["step_index"] = ReadInteger(rows, 1), ["operation"] = rows.GetString(2),
            ["before"] = ReadJson(rows, 3), ["after"] = ReadJson(rows, 4)
        });
        return (call, deltas);
    }
}
