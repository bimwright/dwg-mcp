using System;
using System.Collections.Generic;
using Bimwright.Dwg.Plugin.Reports.Model;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server;

internal sealed class ReportCallReceipts(int capacity = 4096)
{
    private sealed record Receipt(ReportCallReference Call, int Pid, DateTime Start);

    internal static readonly ReportCallReceipts Default = new();
    private readonly object _gate = new();
    private readonly Queue<string> _order = new();
    private readonly Dictionary<string, Receipt> _receipts = new(StringComparer.Ordinal);

    internal void Remember(string tool, string session, McpResponse reply)
    {
        if (reply.History is not JObject history || reply.Target is not JObject target
            || target.Value<int?>("pid") is not > 0 || target.Value<DateTime?>("process_start_utc") is not DateTime start) return;
        var reference = new ReportCallReference
        {
            CallId = history.Value<string>("call_id"), ClientSession = session, Tool = tool,
            CaptureStatus = history.Value<string>("status"), DrawingKey = history.Value<string>("drawing_key")
        };
        Add(reference, target.Value<int>("pid"), start.ToUniversalTime());
    }

    internal void Add(ReportCallReference call, int pid, DateTime start)
    {
        lock (_gate)
        {
            if (!_receipts.TryAdd(call.CallId, new Receipt(call, pid, start))) return;
            _order.Enqueue(call.CallId);
            if (_order.Count > capacity) _receipts.Remove(_order.Dequeue());
        }
    }

    internal ReportChanges Resolve(string[] ids, string session, int pid, DateTime? start)
    {
        var changes = new ReportChanges();
        lock (_gate)
        {
            foreach (var id in ids)
            {
                if (!_receipts.TryGetValue(id, out var receipt) || receipt.Call.ClientSession != session
                    || receipt.Pid != pid || receipt.Start != start)
                    throw new ArgumentException("REPORT_CALL_UNAVAILABLE: a call is unknown, expired, or belongs to a different gateway or AutoCAD process.");
                changes.Calls.Add(receipt.Call);
            }
        }
        return changes;
    }
}
