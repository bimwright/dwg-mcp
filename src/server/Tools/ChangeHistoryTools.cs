using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server.Tools;

[McpServerToolType]
public static class ChangeHistoryTools
{
    [McpServerTool(Name = "dwg_record_change", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false),
        Description("Attach a user-supplied goal and reason to 1–100 distinct stored call_ids in a single drawing. Use drawing_key and call_id from _history receipts. Reuse request_id only for the same payload. A missing reason is unknown. Requires change history enabled and full mode. Writes local SQLite and works offline.")]
    public static string RecordChange(string drawing_key, string[] call_ids, string request_id, string goal, string reason = null)
    {
        if (ServerState.IsReadOnly) return ServerState.ReadOnlyError("dwg_record_change");
        if (ServerState.Config?.EnableChangeHistoryOrDefault != true) return Error("HISTORY_DISABLED: enable change history before recording a reason.");
        try
        {
            var id = ChangeHistoryCapture.Default.Record(drawing_key, call_ids, request_id, goal, reason);
            return Encode(new McpResponse { Ok = true, Result = new { drawing_key, change_id = id, call_count = call_ids.Length,
                reason_status = string.IsNullOrWhiteSpace(reason) ? "unknown" : "recorded" } });
        }
        catch (ArgumentException ex) { return Error(ex.Message); }
        catch (Exception ex) when (StorageError(ex)) { return Error("HISTORY_UNAVAILABLE: local drawing history could not be written."); }
    }

    [McpServerTool(Name = "dwg_get_change_records", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
        Description("Read local drawing history newest first. Use drawing_key for offline reads, including in read-only mode. Omit it to use the verified active drawing. Filter by a hexadecimal handle and UTC range [from_utc, until_utc). Follow next_cursor with the same drawing and filters. A call shows up to ten sampled deltas and its total stored delta count.")]
    public static async Task<string> GetChangeRecords(string drawing_key = null, string handle = null, string from_utc = null,
        string until_utc = null, int limit = 20, string cursor = null, int? max_response_bytes = null)
    {
        try
        {
            ChangeHistoryResponse.ValidateCap(max_response_bytes);
            JObject target = null;
            if (drawing_key == null)
            {
                var identity = await ToolGateway.SendRaw("history_drawing_key", new { });
                if (!identity.Ok) return Encode(identity);
                target = identity.Target;
                drawing_key = (identity.Result as JObject)?.Value<string>("drawing_key");
                if (!ChangeHistoryCapture.IsKey(drawing_key)) return Error("HISTORY_IDENTITY_UNAVAILABLE: a saved DWG identity is required.");
            }
            var result = ChangeHistoryCapture.Default.Query(drawing_key, handle, from_utc, until_utc, limit, cursor);
            return ChangeHistoryResponse.Encode(new McpResponse { Ok = true, Result = result, Target = target }, max_response_bytes);
        }
        catch (Exception ex) when (ex is ArgumentException or McpException) { return Error(ex.Message); }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("CURSOR_", StringComparison.Ordinal)
            || ex.Message.StartsWith("QUERY_CHANGED:", StringComparison.Ordinal)) { return Error(ex.Message); }
        catch (Exception ex) when (StorageError(ex)) { return Error("HISTORY_UNAVAILABLE: local drawing history could not be read."); }
    }

    [McpServerTool(Name = "dwg_survey_change_impact", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
        Description("Inspect up to 50 distinct hexadecimal handles on the verified active drawing. Reports direct owner, layer state and block definition relationships, with erased or unresolved handles called out. Requires dwg_switch_target confirmation, including after changing drawings. Leaves view, selection and drawing unchanged.")]
    public static async Task<string> SurveyChangeImpact(string[] handles, int? max_response_bytes = null)
    {
        try
        {
            ChangeHistoryResponse.ValidateCap(max_response_bytes);
            if (handles is not { Length: >= 1 and <= 50 }) return Error("handles must contain 1–50 distinct DWG handles.");
            var normalized = handles.Select(ChangeHistoryCapture.NormalizeHandle).ToArray();
            if (normalized.Distinct(StringComparer.Ordinal).Count() != normalized.Length) return Error("handles must be distinct.");
            var result = await ToolGateway.SendRaw("survey_change_impact", new { handles = normalized });
            return ChangeHistoryResponse.Encode(result, max_response_bytes);
        }
        catch (Exception ex) when (ex is ArgumentException or McpException) { return Error(ex.Message); }
    }

    private static bool StorageError(Exception ex) => ex is SqliteException or IOException or UnauthorizedAccessException or JsonException or InvalidOperationException;
    private static string Error(string error) => Encode(new McpResponse { Ok = false, Error = error });
    private static string Encode(McpResponse response) => JsonConvert.SerializeObject(response);
}
