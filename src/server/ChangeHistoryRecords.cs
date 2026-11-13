using System;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.Cad;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server;

internal sealed partial class ChangeHistoryCapture
{
    internal JObject Query(string key, string handle, string fromUtc, string untilUtc, int limit, string cursor)
    {
        ValidateKey(key);
        if (limit is < 1 or > 100) throw new ArgumentException("limit must be between 1 and 100.");
        if (handle != null) handle = NormalizeHandle(handle);
        var from = NormalizeUtc(fromUtc);
        var until = NormalizeUtc(untilUtc);
        if (from != null && until != null && string.CompareOrdinal(from, until) >= 0)
            throw new ArgumentException("from_utc must precede until_utc.");

        var items = new JArray();
        var page = new JObject { ["drawing_key"] = key, ["status"] = "no_history", ["calls"] = items, ["next_cursor"] = null };
        using var db = OpenExisting(key, SqliteOpenMode.ReadOnly);
        if (db == null)
        {
            if (cursor != null) throw new InvalidOperationException("QUERY_CHANGED: drawing history is no longer available; restart without a cursor.");
            return page;
        }
        using var tx = db.BeginTransaction(deferred: true);
        var version = EnsureSchema(db, tx, key, writable: false);
        using var command = db.CreateCommand();
        command.Transaction = tx;
        command.CommandText = "SELECT call_id FROM calls ORDER BY sequence LIMIT 1";
        var snapshot = command.ExecuteScalar() as string;
        if (snapshot == null && cursor == null) return page;

        var scope = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"history\n{key}\n{handle}\n{from}\n{until}")));
        var after = cursor == null ? (long?)null : checked((long)EntityQueryCursor.Read(cursor, snapshot, scope));
        var recordFields = version == 2 ? "r.change_id, r.goal, r.reason" : "NULL, NULL, NULL";
        var recordJoin = version == 2 ? "LEFT JOIN change_record_calls AS l USING (call_id) LEFT JOIN change_records AS r USING (change_id)" : "";
        command.CommandText = $"""
            SELECT c.sequence, c.call_id, c.tool, c.recorded_utc, c.document_id, c.target_id, c.pid, c.process_start_utc, c.acad_year,
                   c.coverage, c.coverage_reason, c.observed, {recordFields},
                   (SELECT count(*) FROM entity_deltas WHERE call_id = c.call_id)
            FROM calls AS c {recordJoin}
            WHERE ($after IS NULL OR c.sequence < $after)
                AND ($from IS NULL OR c.recorded_utc >= $from) AND ($until IS NULL OR c.recorded_utc < $until)
                AND ($handle IS NULL OR EXISTS (SELECT 1 FROM entity_deltas WHERE call_id = c.call_id AND handle = $handle))
            ORDER BY c.sequence DESC LIMIT $limit
            """;
        Bind(command, "$after", after);
        Bind(command, "$from", from);
        Bind(command, "$until", until);
        Bind(command, "$handle", handle);
        Bind(command, "$limit", limit + 1);
        var checkpoints = new JArray();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                if (items.Count == limit)
                {
                    page["next_cursor"] = checkpoints.Last.DeepClone();
                    break;
                }
                var changeId = ReadText(reader, 12);
                var reason = ReadText(reader, 14);
                items.Add(new JObject
                {
                    ["call_id"] = reader.GetString(1), ["tool"] = reader.GetString(2), ["recorded_utc"] = reader.GetString(3),
                    ["document_id"] = reader.GetString(4), ["target_id"] = ReadText(reader, 5), ["pid"] = ReadInteger(reader, 6),
                    ["process_start_utc"] = ReadText(reader, 7), ["acad_year"] = ReadInteger(reader, 8),
                    ["coverage"] = reader.GetString(9), ["coverage_reason"] = ReadText(reader, 10), ["observed"] = reader.GetInt64(11),
                    ["change_id"] = changeId, ["goal"] = ReadText(reader, 13), ["reason"] = reason, ["stored"] = reader.GetInt64(15),
                    ["reason_status"] = changeId == null ? "unassigned" : reason == null ? "unknown" : "recorded"
                });
                checkpoints.Add(EntityQueryCursor.Issue(snapshot, scope, reader.GetInt64(0).ToString("X", CultureInfo.InvariantCulture)));
            }
        }
        page["status"] = items.Count == 0 ? "no_matches" : "ok";
        page["_checkpoints"] = checkpoints;
        command.Parameters.Clear();
        command.CommandText = """
            SELECT handle, step_index, operation, before_json, after_json FROM entity_deltas
            WHERE call_id = $call AND ($handle IS NULL OR handle = $handle) ORDER BY ordinal LIMIT 10
            """;
        Bind(command, "$handle", handle);
        var callParameter = command.Parameters.Add("$call", SqliteType.Text);
        foreach (var item in items)
        {
            callParameter.Value = item.Value<string>("call_id");
            var sample = new JArray();
            using var reader = command.ExecuteReader();
            while (reader.Read()) sample.Add(new JObject
            {
                ["handle"] = reader.GetString(0), ["step_index"] = ReadInteger(reader, 1), ["operation"] = reader.GetString(2),
                ["before"] = ReadJson(reader, 3), ["after"] = ReadJson(reader, 4)
            });
            item["deltas"] = sample;
        }
        return page;
    }

    internal static string NormalizeHandle(string handle)
    {
        if (handle is not { Length: >= 1 and <= 16 } || !CadWire.TryParseHandleValue(handle, out var value, out _) || value == 0)
            throw new ArgumentException("handle must be a nonzero hexadecimal DWG handle.");
        return value.ToString("X", CultureInfo.InvariantCulture);
    }

    private static string NormalizeUtc(string value)
    {
        if (value == null) return null;
        if (value.Length > 33 || !Regex.IsMatch(value, @"\A[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(\.[0-9]{1,7})?(Z|\+00:00)\z")
            || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var utc))
            throw new ArgumentException("from_utc and until_utc must be ISO 8601 UTC timestamps.");
        return utc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
    }

    internal string Record(string key, string[] callIds, string requestId, string goal, string reason)
    {
        ValidateKey(key);
        if (callIds is not { Length: >= 1 and <= 100 } || callIds.Any(id => !Guid.TryParseExact(id, "N", out _)))
            throw new ArgumentException("call_ids must contain 1–100 server-issued call IDs.");
        var ids = callIds.Select(id => Guid.ParseExact(id, "N").ToString("N")).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            throw new ArgumentException("call_ids must be distinct.");
        if (string.IsNullOrWhiteSpace(requestId) || Encoding.UTF8.GetByteCount(requestId) > 128)
            throw new ArgumentException("request_id must contain 1–128 UTF-8 bytes.");
        if (string.IsNullOrWhiteSpace(goal) || Encoding.UTF8.GetByteCount(goal) > 512)
            throw new ArgumentException("goal must contain 1–512 UTF-8 bytes.");
        if (reason != null && Encoding.UTF8.GetByteCount(reason) > 2048)
            throw new ArgumentException("reason must fit within 2048 UTF-8 bytes.");
        if (string.IsNullOrWhiteSpace(reason)) reason = null;

        using var db = OpenExisting(key, SqliteOpenMode.ReadWrite)
            ?? throw new ArgumentException("HISTORY_NOT_FOUND: this drawing has no recorded calls.");
        using var tx = db.BeginTransaction();
        EnsureSchema(db, tx, key, writable: true);
        using var command = db.CreateCommand();
        command.Transaction = tx;
        command.CommandText = "SELECT change_id, goal, reason, call_ids FROM change_records WHERE request_id = $request";
        Bind(command, "$request", requestId);
        var callJson = new JArray(ids).ToString(Formatting.None);
        using (var reader = command.ExecuteReader())
        {
            if (reader.Read())
            {
                if (reader.GetString(1) != goal || ReadText(reader, 2) != reason || reader.GetString(3) != callJson)
                    throw new ArgumentException("CHANGE_REQUEST_CONFLICT: request_id was already used with a different payload.");
                return reader.GetString(0);
            }
        }

        var changeId = Guid.NewGuid().ToString("N");
        command.CommandText = "INSERT INTO change_records VALUES ($change, $request, $goal, $reason, $calls)";
        Bind(command, "$change", changeId);
        Bind(command, "$goal", goal);
        Bind(command, "$reason", reason);
        Bind(command, "$calls", callJson);
        command.ExecuteNonQuery();

        command.Parameters.Clear();
        command.CommandText = """
            INSERT INTO change_record_calls (call_id, change_id)
            SELECT $call, $change WHERE EXISTS (SELECT 1 FROM entity_deltas WHERE call_id = $call)
            ON CONFLICT(call_id) DO NOTHING
            """;
        Bind(command, "$change", changeId);
        var callParameter = command.Parameters.Add("$call", SqliteType.Text);
        foreach (var id in ids)
        {
            callParameter.Value = id;
            if (command.ExecuteNonQuery() != 1)
                throw new ArgumentException("CHANGE_CALL_UNAVAILABLE: a call is unknown, belongs to another drawing, has no saved delta, or is already assigned.");
        }
        tx.Commit();
        return changeId;
    }

    private SqliteConnection OpenExisting(string key, SqliteOpenMode mode)
    {
        try { return Open(key, mode); }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 14 && IsMissing(DatabasePath(key))) { return null; }
    }

    private static bool IsMissing(string path)
    {
        try { File.GetAttributes(path); return false; }
        catch (IOException ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return true; }
    }

    private string DatabasePath(string key) => Path.Combine(directory, key + ".db");

    private SqliteConnection Open(string key, SqliteOpenMode mode)
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath(key), Mode = mode, DefaultTimeout = 1, Pooling = false, ForeignKeys = true
        }.ToString());
        try { db.Open(); return db; }
        catch { db.Dispose(); throw; }
    }

    private static long EnsureSchema(SqliteConnection db, SqliteTransaction tx, string key, bool writable)
    {
        using var command = db.CreateCommand();
        command.Transaction = tx;
        command.CommandText = "PRAGMA user_version";
        var version = (long)command.ExecuteScalar();
        if (version == 0 && writable)
        {
            command.CommandText = """
                CREATE TABLE drawing (drawing_key TEXT PRIMARY KEY);
                CREATE TABLE calls (
                    sequence INTEGER PRIMARY KEY, call_id TEXT NOT NULL UNIQUE, client_session TEXT NOT NULL,
                    tool TEXT NOT NULL, recorded_utc TEXT NOT NULL, document_id TEXT NOT NULL,
                    target_id TEXT, pid INTEGER, process_start_utc TEXT, acad_year INTEGER,
                    coverage TEXT NOT NULL, coverage_reason TEXT, observed INTEGER NOT NULL
                );
                CREATE TABLE entity_deltas (
                    call_id TEXT NOT NULL REFERENCES calls(call_id), ordinal INTEGER NOT NULL,
                    handle TEXT NOT NULL, step_index INTEGER, operation TEXT NOT NULL, before_json TEXT, after_json TEXT,
                    PRIMARY KEY (call_id, ordinal)
                );
                CREATE INDEX entity_deltas_by_handle ON entity_deltas(handle, call_id);
                INSERT INTO drawing VALUES ($key);
                """;
            Bind(command, "$key", key);
            command.ExecuteNonQuery();
        }
        else if (version is not (1 or 2)) throw new IOException("Unsupported drawing history schema.");

        command.CommandText = "SELECT drawing_key FROM drawing";
        if (!string.Equals(command.ExecuteScalar() as string, key, StringComparison.Ordinal))
            throw new IOException("Drawing history identity mismatch.");

        if (writable && version < 2)
        {
            command.CommandText = """
                CREATE TABLE change_records (
                    change_id TEXT PRIMARY KEY, request_id TEXT NOT NULL UNIQUE,
                    goal TEXT NOT NULL, reason TEXT, call_ids TEXT NOT NULL
                );
                CREATE TABLE change_record_calls (
                    call_id TEXT PRIMARY KEY REFERENCES calls(call_id),
                    change_id TEXT NOT NULL REFERENCES change_records(change_id)
                );
                PRAGMA user_version = 2;
                """;
            command.ExecuteNonQuery();
            return 2;
        }
        return version;
    }

    private static string ReadText(SqliteDataReader reader, int i) => reader.IsDBNull(i) ? null : reader.GetString(i);
    private static long? ReadInteger(SqliteDataReader reader, int i) => reader.IsDBNull(i) ? null : reader.GetInt64(i);
    private static JToken ReadJson(SqliteDataReader reader, int i) => reader.IsDBNull(i) ? null : JToken.Parse(reader.GetString(i));

    internal static void ValidateKey(string key)
    {
        if (!IsKey(key)) throw new ArgumentException("drawing_key must be a server-issued drawing key.");
    }
}
