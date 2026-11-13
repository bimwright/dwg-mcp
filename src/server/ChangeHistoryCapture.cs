using System;
using System.IO;
using System.Linq;
using System.Text;
using Bimwright.Dwg.Plugin;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server;

internal sealed partial class ChangeHistoryCapture(string directory)
{
    internal static readonly ChangeHistoryCapture Default = new(Path.Combine(
        Environment.GetEnvironmentVariable("LOCALAPPDATA") is { Length: > 0 } local ? local
            : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bimwright", "dwg-mcp", "history"));

    internal void Complete(string tool, string callId, bool enabled, McpResponse response)
    {
        var capture = response.ChangeCapture;
        response.ChangeCapture = null;
        if (!ChangeCaptureContract.Tracks(tool)) return;

        var receipt = new JObject { ["call_id"] = callId, ["status"] = "disabled", ["coverage"] = "none",
            ["observed"] = 0, ["stored"] = 0, ["reason_status"] = "unassigned" };
        response.History = receipt;
        if (!enabled) return;

        if (capture?["version"]?.Type != JTokenType.Integer || capture.Value<int?>("version") != ChangeCaptureContract.Version)
        {
            receipt["status"] = response.Ok ? "unsupported" : "outcome_unknown";
            return;
        }
        var status = capture.Value<string>("status");
        var key = capture.Value<string>("drawing_key");
        var coverage = capture.Value<string>("coverage");
        if (IsKey(key)) receipt["drawing_key"] = key;
        receipt["status"] = status;
        receipt["coverage"] = coverage;
        receipt["observed"] = capture.Value<int?>("observed") ?? 0;
        if (capture["coverage_reason"] is JValue reason) receipt["coverage_reason"] = reason.DeepClone();
        if (status != "captured") return;

        if (!response.Ok || response.Target?.Value<string>("document_id") is not { Length: > 0 }
            || !IsKey(key) || capture["deltas"] is not JArray deltas || deltas.Count is 0 or > ChangeCaptureContract.MaxDeltas
            || (tool == "batch_execute" && ((response.Result as JObject)?.Value<bool?>("rolled_back") == true))
            || Encoding.UTF8.GetByteCount(capture.ToString(Formatting.None)) > ChangeCaptureContract.MaxCaptureBytes)
        {
            receipt["status"] = "outcome_unknown";
            receipt["coverage"] = "none";
            return;
        }

        try
        {
            Store(tool, callId, key, coverage, capture, deltas, response.Target);
            receipt["status"] = "stored";
            receipt["stored"] = deltas.Count;
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            // DWG may already have committed. Keep its result and never retry the CAD call.
            receipt["status"] = "storage_failed";
        }
    }

    private void Store(string tool, string callId, string key, string coverage, JObject capture, JArray deltas, JObject target)
    {
        Directory.CreateDirectory(directory);
        using var db = Open(key, SqliteOpenMode.ReadWriteCreate);
        using var tx = db.BeginTransaction();
        EnsureSchema(db, tx, key, writable: true);
        using var command = db.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            INSERT INTO calls (call_id, client_session, tool, recorded_utc, document_id, target_id, pid, process_start_utc, acad_year,
                coverage, coverage_reason, observed)
            VALUES ($call, $session, $tool, $utc, $document, $target, $pid, $start, $year, $coverage, $reason, $observed)
            ON CONFLICT(call_id) DO NOTHING
            """;
        Bind(command, "$call", callId);
        Bind(command, "$session", Tools.ReportTools.GatewaySession);
        Bind(command, "$tool", tool);
        Bind(command, "$utc", DateTime.UtcNow.ToString("O"));
        Bind(command, "$document", target.Value<string>("document_id"));
        Bind(command, "$target", target.Value<string>("target_id"));
        Bind(command, "$pid", target.Value<int?>("pid"));
        Bind(command, "$start", target.Value<DateTime?>("process_start_utc")?.ToUniversalTime().ToString("O"));
        Bind(command, "$year", target.Value<int?>("acad_year"));
        Bind(command, "$coverage", coverage);
        Bind(command, "$reason", capture.Value<string>("coverage_reason"));
        Bind(command, "$observed", capture.Value<int>("observed"));
        if (command.ExecuteNonQuery() != 0)
        {
            command.CommandText = """
                INSERT INTO entity_deltas (call_id, ordinal, handle, step_index, operation, before_json, after_json)
                VALUES ($call, $ordinal, $handle, $step, $operation, $before, $after)
                """;
            for (var i = 0; i < deltas.Count; i++)
            {
                var delta = deltas[i];
                command.Parameters.Clear();
                Bind(command, "$call", callId);
                Bind(command, "$ordinal", i);
                Bind(command, "$handle", delta.Value<string>("handle"));
                Bind(command, "$step", delta.Value<int?>("step_index"));
                Bind(command, "$operation", delta.Value<string>("operation"));
                Bind(command, "$before", Json(delta["before"]));
                Bind(command, "$after", Json(delta["after"]));
                command.ExecuteNonQuery();
            }
        }
        tx.Commit();
    }

    private static void Bind(SqliteCommand command, string name, object value)
        => command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static string Json(JToken token) => token == null || token.Type == JTokenType.Null ? null : token.ToString(Formatting.None);

    internal static bool IsKey(string key) => key?.Length == 64 && key.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
